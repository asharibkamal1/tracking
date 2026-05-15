namespace TaxpayerAnalytics.TrackingApi.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Cross-Origin-Resource-Policy"] = "same-site";
        h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        return next(ctx);
    }
}
