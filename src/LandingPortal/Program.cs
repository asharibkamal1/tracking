using Microsoft.EntityFrameworkCore;
using Serilog;
using TaxpayerAnalytics.LandingPortal.Services;
using TaxpayerAnalytics.Shared.Configuration;
using TaxpayerAnalytics.Shared.Data;
using TaxpayerAnalytics.Shared.Entities;
using TaxpayerAnalytics.Shared.Security;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
builder.Services.Configure<TrackingOptions>(builder.Configuration.GetSection(TrackingOptions.SectionName));

builder.Services.AddDbContext<AnalyticsDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("AnalyticsDb")));

builder.Services.AddSingleton<ITrackingTokenService, TrackingTokenService>();
builder.Services.AddSingleton<IPiiCipher, PiiCipher>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IDummyDataSeeder, DummyDataSeeder>();

builder.Services.AddControllersWithViews();
builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; o.Preload = true; });

var app = builder.Build();

// Idempotent DB bootstrap: creates the database if missing, applies db/schema.sql,
// seeds the 4 page-template campaigns. Skip with Database:AutoApplySchema=false.
await app.Services.InitializeAnalyticsDatabaseAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    var apiBase = builder.Configuration["Tracking:ApiBaseUrl"] ?? string.Empty;
    h["Content-Security-Policy"] =
        "default-src 'self'; " +
        "img-src 'self' data: https:; " +
        "media-src 'self' https:; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "connect-src 'self' " + apiBase + ";";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute(name: "default", pattern: "{controller=Campaign}/{action=Index}/{id?}");

app.Run();
