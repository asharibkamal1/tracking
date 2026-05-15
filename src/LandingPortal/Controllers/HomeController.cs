using Microsoft.AspNetCore.Mvc;
using TaxpayerAnalytics.LandingPortal.Services;

namespace TaxpayerAnalytics.LandingPortal.Controllers;

/// <summary>
/// Startup page + test launcher. The route is /, and a clearly-marked test mode
/// page lists the 4 landing pages with real encrypted tokens against dummy NTNs
/// so you can click through and watch the tracker fire end-to-end.
/// </summary>
public sealed class HomeController(
    IDummyDataSeeder seeder,
    IWebHostEnvironment env,
    IConfiguration config) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!IsTestModeEnabled())
            return NotFound();

        var links = await seeder.EnsureSeededAsync(ct);
        ViewData["Title"] = "Taxpayer Analytics — Test Launcher";
        return View("TestLauncher", links);
    }

    [HttpGet("/dev/test")]
    public Task<IActionResult> DevTest(CancellationToken ct) => Index(ct);

    private bool IsTestModeEnabled()
    {
        if (env.IsDevelopment()) return true;
        // Explicit opt-in for non-Dev environments via config: "TestMode:Enabled": true.
        return config.GetValue<bool>("TestMode:Enabled");
    }
}
