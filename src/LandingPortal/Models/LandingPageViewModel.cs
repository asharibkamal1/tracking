namespace TaxpayerAnalytics.LandingPortal.Models;

public sealed class LandingPageViewModel
{
    public string Token { get; set; } = default!;
    public string CampaignCode { get; set; } = default!;
    public string CampaignName { get; set; } = default!;
    public string PageTemplate { get; set; } = "Index";
    public string? VideoUrl { get; set; }
    public string? RegistrationUrl { get; set; }
    public string? FilingUrl { get; set; }
    public string ApiBaseUrl { get; set; } = default!;
}

public sealed record InvalidPageModel(string Reason);
