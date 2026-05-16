using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxpayerAnalytics.LandingPortal.Models.Dashboard;

namespace TaxpayerAnalytics.LandingPortal.Controllers.Dashboard;

[Route("dashboard")]
public sealed class AuthController(IOptions<DashboardOptions> opt) : Controller
{
    private const string LoginView = "~/Views/Dashboard/Login.cshtml";

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null) =>
        View(LoginView, new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(LoginView, model);

        var admin = opt.Value.Admin;
        var ok = string.Equals(model.Username, admin.Username, StringComparison.Ordinal)
              && CryptographicEqual(model.Password, admin.Password);
        if (!ok)
        {
            // Generic message — don't leak which field was wrong.
            model.Error = "Invalid username or password.";
            return View(LoginView, model);
        }

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, model.Username),
                new Claim(ClaimTypes.Role, "Admin")
            },
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(opt.Value.CookieLifetimeMinutes)
            });

        var safeReturn = !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl)
            ? model.ReturnUrl
            : "/dashboard";
        return LocalRedirect(safeReturn);
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/dashboard/login");
    }

    private static bool CryptographicEqual(string a, string b)
    {
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
