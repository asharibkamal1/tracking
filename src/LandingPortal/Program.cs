using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TaxpayerAnalytics.LandingPortal.BackgroundServices;
using TaxpayerAnalytics.LandingPortal.Hubs;
using TaxpayerAnalytics.LandingPortal.Middleware;
using TaxpayerAnalytics.LandingPortal.Models.Dashboard;
using TaxpayerAnalytics.LandingPortal.Repositories;
using TaxpayerAnalytics.LandingPortal.Services;
using TaxpayerAnalytics.LandingPortal.Services.Dashboard;
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
builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection(DashboardOptions.SectionName));

builder.Services.AddSingleton<EventLogNameInterceptor>();
builder.Services.AddDbContext<AnalyticsDbContext>((sp, opt) =>
{
    opt.UseSqlServer(builder.Configuration.GetConnectionString("AnalyticsDb"),
        sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(2), null));
    opt.AddInterceptors(sp.GetRequiredService<EventLogNameInterceptor>());
});

// Shared crypto / parsing
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

// Dashboard
builder.Services.AddScoped<IDashboardQueryService, DashboardQueryService>();
builder.Services.AddSingleton<IRealtimeNotifier, RealtimeNotifier>();
builder.Services.AddSingleton<IExcelExporter, ExcelExporter>();
builder.Services.AddSingleton<IPdfExporter, PdfExporter>();
builder.Services.AddHostedService<ActiveUsersBroadcaster>();

// Cookie auth — single admin from config. Login form lives at /dashboard/login.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opts =>
    {
        opts.LoginPath = "/dashboard/login";
        opts.AccessDeniedPath = "/dashboard/login";
        opts.LogoutPath = "/dashboard/logout";
        opts.Cookie.Name = "tpaDash";
        opts.Cookie.SameSite = SameSiteMode.Lax;
        opts.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        opts.SlidingExpiration = true;
        opts.ExpireTimeSpan = TimeSpan.FromMinutes(
            builder.Configuration.GetValue<int>("Dashboard:CookieLifetimeMinutes", 480));
    });
builder.Services.AddAuthorization();

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
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

// Top-of-pipeline so it catches anything thrown by later middleware/MVC.
app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "img-src 'self' data: https:; " +
        "media-src 'self' https:; " +
        "font-src 'self' data: https://fonts.gstatic.com https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "connect-src 'self' wss: ws:;";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Tracking API endpoints are public (called by tracking.js from landing pages).
// Dashboard routes are gated by [Authorize(Roles="Admin")] on their controllers.
app.MapControllers().RequireRateLimiting("tracking");
app.MapHub<DashboardHub>("/hubs/dashboard");
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.Run();
