using System.ComponentModel.DataAnnotations;

namespace TaxpayerAnalytics.LandingPortal.Models.Dashboard;

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    public AdminCredentials Admin { get; set; } = new();
    public int CookieLifetimeMinutes { get; set; } = 480;
}

public sealed class AdminCredentials
{
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "change-me";
}

public sealed class LoginViewModel
{
    [Required, MaxLength(64)]
    public string Username { get; set; } = default!;

    [Required, MaxLength(128), DataType(DataType.Password)]
    public string Password { get; set; } = default!;

    public string? ReturnUrl { get; set; }
    public string? Error { get; set; }
}
