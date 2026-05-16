using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TaxpayerAnalytics.Shared.Dtos;

namespace TaxpayerAnalytics.LandingPortal.Services.Dashboard;

public interface IPdfExporter
{
    byte[] BuildOverviewReport(IEnumerable<CampaignOverviewDto> overviews);
    byte[] BuildTaxpayerReport(TaxpayerDetailDto detail);
}

public sealed class PdfExporter : IPdfExporter
{
    static PdfExporter() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] BuildOverviewReport(IEnumerable<CampaignOverviewDto> overviews) =>
        Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(36);
                p.DefaultTextStyle(t => t.FontSize(10));
                p.Header().Column(c =>
                {
                    c.Item().Text("Taxpayer Analytics — Overview").FontSize(18).Bold();
                    c.Item().Text($"Generated {DateTime.UtcNow:u}").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
                p.Content().PaddingVertical(10).Column(col =>
                {
                    foreach (var o in overviews)
                    {
                        col.Item().PaddingVertical(6).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(c =>
                        {
                            c.Item().Text($"{o.CampaignCode} — {o.Name}").FontSize(13).Bold();
                            c.Item().Text($"Template: {o.PageTemplate}").FontSize(9).FontColor(Colors.Grey.Darken1);
                            c.Item().PaddingTop(6).Table(t =>
                            {
                                t.ColumnsDefinition(d =>
                                {
                                    d.RelativeColumn();
                                    d.RelativeColumn();
                                    d.RelativeColumn();
                                    d.RelativeColumn();
                                });
                                Row(t, "Recipients", o.TotalRecipients.ToString("N0"));
                                Row(t, "SMS Sent", o.TotalSmsSent.ToString("N0"));
                                Row(t, "Visits", o.TotalVisits.ToString("N0"));
                                Row(t, "Unique", o.UniqueVisitors.ToString("N0"));
                                Row(t, "CTR", $"{o.ClickThroughRatePct:F2}%");
                                Row(t, "Avg Duration", $"{o.AvgSessionDurationSec:F1}s");
                                Row(t, "Avg Scroll", $"{o.AvgScrollDepthPct:F1}%");
                                Row(t, "Bounce", $"{o.BounceRatePct:F2}%");
                                Row(t, "Register", o.RegisterClicks.ToString("N0"));
                                Row(t, "File", o.FileClicks.ToString("N0"));
                                Row(t, "Video Start", o.VideoStarts.ToString("N0"));
                                Row(t, "Video Done", o.VideoCompletions.ToString("N0"));
                            });
                        });
                    }
                });
                p.Footer().AlignCenter().Text(x => { x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
            });
        }).GeneratePdf();

    public byte[] BuildTaxpayerReport(TaxpayerDetailDto d) =>
        Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(36);
                p.DefaultTextStyle(t => t.FontSize(10));
                p.Header().Column(c =>
                {
                    c.Item().Text($"Taxpayer Activity Report").FontSize(18).Bold();
                    c.Item().Text($"{d.CampaignCode} — {d.CampaignName} — NTN {d.MaskedNtn}")
                        .FontSize(11).FontColor(Colors.Grey.Darken2);
                });
                p.Content().PaddingVertical(10).Column(col =>
                {
                    col.Item().Text("Summary").Bold().FontSize(12);
                    col.Item().PaddingTop(4).Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.RelativeColumn();
                            cd.RelativeColumn();
                            cd.RelativeColumn();
                            cd.RelativeColumn();
                        });
                        Row(t, "Recipient Id", d.RecipientId.ToString());
                        Row(t, "Visit Count", d.VisitCount.ToString());
                        Row(t, "First Visit", d.FirstVisitAt?.ToString("u") ?? "-");
                        Row(t, "Last Visit", d.LastVisitAt?.ToString("u") ?? "-");
                    });

                    foreach (var s in d.Sessions)
                    {
                        col.Item().PaddingTop(12).Text($"Session {s.SessionId:N}").Bold().FontSize(11);
                        col.Item().Text($"{s.StartedAt:u} → {(s.EndedAt?.ToString("u") ?? "open")}  ·  " +
                                        $"{s.Browser} on {s.OperatingSystem} ({s.DeviceType})  ·  {s.City ?? "-"}, {s.Country ?? "-"}")
                            .FontSize(9).FontColor(Colors.Grey.Darken1);
                        col.Item().Text($"Duration {s.DurationSeconds}s · Scroll {s.MaxScrollDepth}% · Video {s.VideoWatchPercent}% · " +
                                        $"Register {(s.RegisterClicked ? "Y" : "N")} · File {(s.FileClicked ? "Y" : "N")}")
                            .FontSize(9);

                        col.Item().PaddingTop(4).Table(t =>
                        {
                            t.ColumnsDefinition(cd =>
                            {
                                cd.ConstantColumn(140);
                                cd.RelativeColumn();
                                cd.ConstantColumn(60);
                                cd.ConstantColumn(60);
                            });
                            t.Header(h =>
                            {
                                h.Cell().Text("Time").SemiBold();
                                h.Cell().Text("Event").SemiBold();
                                h.Cell().Text("Dur s").SemiBold();
                                h.Cell().Text("Scroll").SemiBold();
                            });
                            foreach (var e in s.Events)
                            {
                                t.Cell().Text(e.EventTime.ToString("yyyy-MM-dd HH:mm:ss"));
                                t.Cell().Text(string.IsNullOrEmpty(e.EventValue) ? e.EventTypeName : $"{e.EventTypeName}: {e.EventValue}");
                                t.Cell().AlignRight().Text((e.DurationSeconds ?? 0).ToString());
                                t.Cell().AlignRight().Text(e.ScrollDepth?.ToString() ?? "");
                            }
                        });
                    }
                });
                p.Footer().AlignCenter().Text(x => { x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
            });
        }).GeneratePdf();

    private static void Row(QuestPDF.Fluent.TableDescriptor t, string label, string value)
    {
        t.Cell().Text(label).SemiBold();
        t.Cell().AlignRight().Text(value);
    }
}
