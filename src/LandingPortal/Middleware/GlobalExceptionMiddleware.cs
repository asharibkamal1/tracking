using System.Net;
using System.Text.Json;

namespace TaxpayerAnalytics.LandingPortal.Middleware;

/// <summary>
/// Catches any unhandled exception from the request pipeline, logs it with the
/// request path, and writes either a JSON ProblemDetails (for /api/* and SignalR)
/// or a friendly HTML page (for MVC pages). Stops a thrown exception from leaking
/// an internal stack trace to the caller in production.
/// </summary>
public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — not an error.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

            if (ctx.Response.HasStarted)
            {
                // Headers already flushed; can't write a clean error body.
                return;
            }

            ctx.Response.Clear();
            ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var wantsJson = ctx.Request.Path.StartsWithSegments("/api")
                            || ctx.Request.Path.StartsWithSegments("/hubs")
                            || (ctx.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase));

            if (wantsJson)
            {
                ctx.Response.ContentType = "application/problem+json";
                var problem = new
                {
                    type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                    title = "An unexpected error occurred",
                    status = 500,
                    traceId = ctx.TraceIdentifier
                };
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(problem));
            }
            else
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync($"""
                    <!doctype html><meta charset="utf-8"><title>Server error</title>
                    <body style="font:14px system-ui;padding:40px;max-width:700px;margin:auto;color:#1f2937">
                    <h2>Something went wrong</h2>
                    <p>The server hit an unexpected error. Trace id <code>{ctx.TraceIdentifier}</code>.</p>
                    <p>Check the application logs for details.</p>
                    </body>
                    """);
            }
        }
    }
}
