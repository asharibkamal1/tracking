using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TaxpayerAnalytics.Shared.Configuration;
using TaxpayerAnalytics.Shared.Data;
using TaxpayerAnalytics.Shared.Entities;
using TaxpayerAnalytics.Shared.Security;
using TaxpayerAnalytics.TrackingApi.BackgroundServices;
using TaxpayerAnalytics.TrackingApi.Middleware;
using TaxpayerAnalytics.TrackingApi.Repositories;
using TaxpayerAnalytics.TrackingApi.Services;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
builder.Services.Configure<GeoIpOptions>(builder.Configuration.GetSection(GeoIpOptions.SectionName));

builder.Services.AddDbContextPool<AnalyticsDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("AnalyticsDb"),
        sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(2), null)));

builder.Services.AddSingleton<ITrackingTokenService, TrackingTokenService>();
builder.Services.AddSingleton<IPiiCipher, PiiCipher>();
builder.Services.AddSingleton<IBotDetector, BotDetector>();
builder.Services.AddSingleton<IUserAgentParser, UserAgentParser>();
builder.Services.AddSingleton<IGeoIpService, GeoIpService>();
builder.Services.AddSingleton<IEventIngestionQueue, EventIngestionQueue>();

builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();

builder.Services.AddHostedService<EventFlushService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks().AddDbContextCheck<AnalyticsDbContext>();

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

builder.Services.AddCors(c => c.AddPolicy("landing", p =>
    p.WithOrigins(builder.Configuration["Cors:AllowedOrigins"]?.Split(',') ?? Array.Empty<string>())
     .WithMethods("POST", "OPTIONS")
     .WithHeaders("Content-Type")));

var app = builder.Build();

// Idempotent DB bootstrap: creates the database if missing, applies db/schema.sql,
// seeds the 4 page-template campaigns. Skip with Database:AutoApplySchema=false.
await app.Services.InitializeAnalyticsDatabaseAsync();

app.UseSerilogRequestLogging();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseHttpsRedirection();
app.UseCors("landing");
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers().RequireRateLimiting("tracking");
app.MapHealthChecks("/health");

app.Run();
