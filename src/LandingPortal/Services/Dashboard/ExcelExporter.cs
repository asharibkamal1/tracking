using ClosedXML.Excel;
using TaxpayerAnalytics.Shared.Dtos;

namespace TaxpayerAnalytics.LandingPortal.Services.Dashboard;

public interface IExcelExporter
{
    byte[] BuildOverviewWorkbook(IEnumerable<CampaignOverviewDto> overviews, IEnumerable<TaxpayerRowDto> taxpayers);
    byte[] BuildTaxpayerWorkbook(TaxpayerDetailDto detail);
}

public sealed class ExcelExporter : IExcelExporter
{
    public byte[] BuildOverviewWorkbook(IEnumerable<CampaignOverviewDto> overviews, IEnumerable<TaxpayerRowDto> taxpayers)
    {
        using var wb = new XLWorkbook();

        var sum = wb.Worksheets.Add("Campaigns");
        var headers = new[]
        {
            "Code", "Name", "Template", "Recipients", "SMS Sent", "Visits", "Unique", "Return",
            "CTR %", "Avg Duration (s)", "Avg Scroll %", "Register Clicks", "File Clicks",
            "Bounce %", "Video Starts", "Video Completes", "Video Avg %", "Engaged", "Active Now"
        };
        for (var i = 0; i < headers.Length; i++) sum.Cell(1, i + 1).Value = headers[i];
        sum.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var o in overviews)
        {
            sum.Cell(row, 1).Value = o.CampaignCode;
            sum.Cell(row, 2).Value = o.Name;
            sum.Cell(row, 3).Value = o.PageTemplate;
            sum.Cell(row, 4).Value = o.TotalRecipients;
            sum.Cell(row, 5).Value = o.TotalSmsSent;
            sum.Cell(row, 6).Value = o.TotalVisits;
            sum.Cell(row, 7).Value = o.UniqueVisitors;
            sum.Cell(row, 8).Value = o.ReturnVisits;
            sum.Cell(row, 9).Value = o.ClickThroughRatePct;
            sum.Cell(row, 10).Value = o.AvgSessionDurationSec;
            sum.Cell(row, 11).Value = o.AvgScrollDepthPct;
            sum.Cell(row, 12).Value = o.RegisterClicks;
            sum.Cell(row, 13).Value = o.FileClicks;
            sum.Cell(row, 14).Value = o.BounceRatePct;
            sum.Cell(row, 15).Value = o.VideoStarts;
            sum.Cell(row, 16).Value = o.VideoCompletions;
            sum.Cell(row, 17).Value = o.VideoAvgWatchPct;
            sum.Cell(row, 18).Value = o.EngagedVisitors;
            sum.Cell(row, 19).Value = o.ActiveUsersNow;
            row++;
        }
        sum.Columns().AdjustToContents();

        var people = wb.Worksheets.Add("Taxpayers");
        people.Cell(1, 1).InsertTable(taxpayers.Select(t => new
        {
            t.RecipientId, t.CampaignCode, t.MaskedNtn, t.MobileMasked,
            t.FirstVisitAt, t.LastVisitAt, t.VisitCount,
            t.TotalDurationSec, t.MaxScrollDepth, t.VideoWatchPercent,
            t.RegisterClicked, t.FileClicked, t.EverBot
        }));
        people.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] BuildTaxpayerWorkbook(TaxpayerDetailDto d)
    {
        using var wb = new XLWorkbook();
        var summary = wb.Worksheets.Add("Summary");

        var kvs = new (string Label, object? Value)[]
        {
            ("Recipient Id",   d.RecipientId),
            ("Campaign",       $"{d.CampaignCode} - {d.CampaignName}"),
            ("Masked NTN",     d.MaskedNtn),
            ("Mobile",         d.MobileMasked ?? string.Empty),
            ("Language",       d.Language ?? string.Empty),
            ("Created At",     d.CreatedAt),
            ("SMS Sent At",    d.SmsSentAt ?? (object)string.Empty),
            ("First Visit",    d.FirstVisitAt ?? (object)string.Empty),
            ("Last Visit",     d.LastVisitAt ?? (object)string.Empty),
            ("Visit Count",    d.VisitCount),
            ("Total Sessions", d.Sessions.Count)
        };
        summary.Cell("A1").Value = "Field";
        summary.Cell("B1").Value = "Value";
        summary.Range("A1:B1").Style.Font.Bold = true;
        for (var i = 0; i < kvs.Length; i++)
        {
            summary.Cell(i + 2, 1).Value = kvs[i].Label;
            summary.Cell(i + 2, 2).Value = kvs[i].Value?.ToString() ?? string.Empty;
        }
        summary.Columns().AdjustToContents();

        var ss = wb.Worksheets.Add("Sessions");
        ss.Cell(1, 1).InsertTable(d.Sessions.Select(s => new
        {
            s.SessionId, s.StartedAt, s.EndedAt, s.LastHeartbeatAt,
            s.DurationSeconds, s.MaxScrollDepth, s.VideoWatchSeconds, s.VideoWatchPercent,
            s.RegisterClicked, s.FileClicked, s.TimeToFirstInteractionMs,
            s.IsBot, s.IsBounce, s.IsEngaged,
            s.Browser, s.OperatingSystem, Device = s.DeviceType.ToString(),
            s.IpAddress, s.Country, s.City, s.ScreenResolution, s.Referrer
        }));
        ss.Columns().AdjustToContents();

        var ev = wb.Worksheets.Add("Events");
        ev.Cell(1, 1).InsertTable(d.Sessions.SelectMany(s => s.Events.Select(e => new
        {
            s.SessionId,
            e.EventTime,
            EventType = e.EventTypeName,
            e.EventValue,
            e.DurationSeconds,
            e.ScrollDepth,
            e.PageUrl
        })));
        ev.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
