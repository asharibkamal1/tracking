namespace TaxpayerAnalytics.LandingPortal.Models;

/// <summary>
/// One place that knows about the 4 landing-page templates and their dashboard
/// presentation (icon glyph + CSS accent class). Views call <see cref="For"/>
/// instead of duplicating switch blocks.
/// </summary>
public static class PageTemplateMeta
{
    private static readonly Dictionary<string, PageTemplateInfo> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Index"]       = new("bi-info-circle-fill",        "tpl-index",       "General awareness"),
            ["Enforcement"] = new("bi-shield-fill-exclamation", "tpl-enforcement", "Enforcement notice"),
            ["Combined"]    = new("bi-layers-fill",             "tpl-combined",    "Combined messaging"),
            ["CivicDuty"]   = new("bi-flag-fill",               "tpl-civicduty",   "Civic duty")
        };

    public static PageTemplateInfo For(string? template) =>
        template is not null && Map.TryGetValue(template, out var info)
            ? info
            : new PageTemplateInfo("bi-megaphone-fill", "tpl-index", "Campaign");
}

public sealed record PageTemplateInfo(string IconClass, string CssClass, string Description);
