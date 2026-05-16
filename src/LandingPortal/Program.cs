using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TaxpayerAnalytics.LandingPortal.BackgroundServices;
using TaxpayerAnalytics.LandingPortal.Middleware;
using TaxpayerAnalytics.LandingPortal.Repositories;
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
builder.Services.Configure<GeoIpOptions>(builder.Configuration.GetSection(GeoIpOptions.SectionName));
builder.Services.Configure<TrackingOptions>(builder.Configuration.GetSection(TrackingOptions.SectionName));

builder.Services.AddSingleton<EventLogNameInterceptor>();
builder.Services.AddDbContext<AnalyticsDbContext>((sp, opt) =>
{
    opt.UseSqlServer(builder.Configuration.GetConnectionString("AnalyticsDb"),
        sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(2), null));
    opt.AddInterceptors(sp.GetRequiredService<EventLogNameInterceptor>());
});

// Shared services
builder.Services.AddSingleton<ITrackingTokenService, TrackingTokenService>();
builder.Services.AddSingleton<IPiiCipher, PiiCipher>();
builder.Services.AddSingleton<IBotDetector, BotDetector>();
builder.Services.AddSingleton<IUserAgentParser, UserAgentParser>();

// Tracking pipeline
builder.Services.AddSingleton<IGeoIpService, GeoIpService>();
builder.Services.AddSingleton<IEventIngestionQueue, EventIngestionQueue>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddHostedService<EventFlushService>();

// Test launcher
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IDummyDataSeeder, DummyDataSeeder>();

builder.Services.AddControllersWithViews();
builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; o.Preload = true; });

var security = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new();
builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opts.AddPolicy("tracking", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = security.RateLimitPerMinute,
                QueueLimit = 0
            }));
});

var app = builder.Build();

// Idempotent DB bootstrap: creates the database if missing, applies db/schema.sql,
// seeds the 4 page-template campaigns.
await app.Services.InitializeAnalyticsDatabaseAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();

app.Use(async (ctx, next) =>
{
    // CSP is per-portal; static + landing pages allow Bootstrap CDN + Google Fonts.
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "img-src 'self' data: https:; " +
        "media-src 'self' https:; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "connect-src 'self';";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();

// Tracking API endpoints (same-origin: tracking.js calls /api/v1/... relative).
app.MapControllers().RequireRateLimiting("tracking");
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.Run();
