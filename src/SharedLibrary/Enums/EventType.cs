namespace TaxpayerAnalytics.Shared.Enums;

public enum EventType
{
    PageOpen = 1,
    PageClose = 2,
    Heartbeat = 3,
    ScrollDepth = 4,
    VideoPlay = 10,
    VideoPause = 11,
    VideoComplete = 12,
    VideoProgress = 13,
    RegisterClick = 20,
    FileClick = 21,
    CtaClick = 22,
    OutboundRedirect = 23,
    Bounce = 30,
    Engagement = 50,
    Error = 90,
    BotDetected = 99
}

public enum DeviceType
{
    Unknown = 0,
    Desktop = 1,
    Mobile = 2,
    Tablet = 3,
    Bot = 4
}

public enum CampaignStatus
{
    Draft = 0,
    Active = 1,
    Paused = 2,
    Completed = 3,
    Archived = 4
}
