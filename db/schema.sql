-- TaxpayerAnalytics SQL Server schema (matches SharedLibrary EF Core model).
-- Run against a fresh database, or run individual sections to layer onto an
-- existing schema.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- =============================================================================
-- CampaignMaster
-- =============================================================================
IF OBJECT_ID('dbo.CampaignMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CampaignMaster (
        CampaignId       BIGINT IDENTITY(1,1) NOT NULL,
        CampaignCode     NVARCHAR(64)   NOT NULL,
        Name             NVARCHAR(256)  NOT NULL,
        Description      NVARCHAR(1024) NULL,
        PageTemplate     NVARCHAR(64)   NOT NULL CONSTRAINT DF_Campaign_PageTemplate DEFAULT N'Index',
        VideoUrl         NVARCHAR(2048) NULL,
        RegistrationUrl  NVARCHAR(2048) NULL,
        FilingUrl        NVARCHAR(2048) NULL,
        Status           INT            NOT NULL CONSTRAINT DF_Campaign_Status DEFAULT (1),
        StartDate        DATETIME2(3)   NOT NULL CONSTRAINT DF_Campaign_StartDate DEFAULT SYSUTCDATETIME(),
        EndDate          DATETIME2(3)   NULL,
        CreatedAt        DATETIME2(3)   NOT NULL CONSTRAINT DF_Campaign_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt        DATETIME2(3)   NULL,
        CONSTRAINT PK_CampaignMaster PRIMARY KEY CLUSTERED (CampaignId)
    );

    CREATE UNIQUE INDEX UX_Campaign_Code ON dbo.CampaignMaster(CampaignCode);
    CREATE INDEX IX_Campaign_Status ON dbo.CampaignMaster(Status);
END
GO

-- =============================================================================
-- TaxpayerRecipient (NTN/Mobile encrypted at rest; raw values never leave the
-- SMS pipeline)
-- =============================================================================
IF OBJECT_ID('dbo.TaxpayerRecipient', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TaxpayerRecipient (
        RecipientId      BIGINT IDENTITY(1,1) NOT NULL,
        CampaignId       BIGINT         NOT NULL,
        NtnHash          NVARCHAR(128)  NOT NULL,
        NtnEncrypted     VARBINARY(MAX) NOT NULL,
        MaskedNtn        NVARCHAR(32)   NULL,
        MobileMasked     NVARCHAR(32)   NULL,
        MobileEncrypted  VARBINARY(MAX) NOT NULL,
        Language         NVARCHAR(8)    NULL,
        TrackingToken    NVARCHAR(512)  NOT NULL,
        CreatedAt        DATETIME2(3)   NOT NULL CONSTRAINT DF_Recip_CreatedAt DEFAULT SYSUTCDATETIME(),
        SmsSentAt        DATETIME2(3)   NULL,
        FirstVisitAt     DATETIME2(3)   NULL,
        LastVisitAt      DATETIME2(3)   NULL,
        VisitCount       INT            NOT NULL CONSTRAINT DF_Recip_VisitCount DEFAULT (0),
        CONSTRAINT PK_TaxpayerRecipient PRIMARY KEY CLUSTERED (RecipientId),
        CONSTRAINT FK_TaxpayerRecipient_CampaignMaster
            FOREIGN KEY (CampaignId) REFERENCES dbo.CampaignMaster(CampaignId) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX UX_Recip_Campaign_NtnHash ON dbo.TaxpayerRecipient(CampaignId, NtnHash);
    CREATE UNIQUE INDEX UX_Recip_TrackingToken ON dbo.TaxpayerRecipient(TrackingToken);
    CREATE INDEX IX_Recip_CampaignId ON dbo.TaxpayerRecipient(CampaignId);
END
GO

-- =============================================================================
-- UserSession (one row per page load)
-- =============================================================================
IF OBJECT_ID('dbo.UserSession', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserSession (
        SessionId               UNIQUEIDENTIFIER NOT NULL,
        RecipientId             BIGINT          NOT NULL,
        CampaignId              BIGINT          NOT NULL,
        IpAddress               NVARCHAR(45)    NULL,
        Country                 NVARCHAR(128)   NULL,
        City                    NVARCHAR(128)   NULL,
        Region                  NVARCHAR(128)   NULL,
        Latitude                FLOAT           NULL,
        Longitude               FLOAT           NULL,
        UserAgent               NVARCHAR(1024)  NULL,
        Browser                 NVARCHAR(64)    NULL,
        BrowserVersion          NVARCHAR(64)    NULL,
        OperatingSystem         NVARCHAR(64)    NULL,
        OsVersion               NVARCHAR(64)    NULL,
        DeviceType              INT             NOT NULL,
        ScreenResolution        NVARCHAR(32)    NULL,
        Language                NVARCHAR(16)    NULL,
        Timezone                NVARCHAR(64)    NULL,
        Referrer                NVARCHAR(2048)  NULL,
        IsBot                   BIT             NOT NULL CONSTRAINT DF_Session_IsBot DEFAULT (0),
        IsBounce                BIT             NOT NULL CONSTRAINT DF_Session_IsBounce DEFAULT (1),
        IsEngaged               BIT             NOT NULL CONSTRAINT DF_Session_IsEngaged DEFAULT (0),
        StartedAt               DATETIME2(3)    NOT NULL CONSTRAINT DF_Session_StartedAt DEFAULT SYSUTCDATETIME(),
        EndedAt                 DATETIME2(3)    NULL,
        LastHeartbeatAt         DATETIME2(3)    NOT NULL CONSTRAINT DF_Session_LastHb DEFAULT SYSUTCDATETIME(),
        DurationSeconds         INT             NOT NULL CONSTRAINT DF_Session_Duration DEFAULT (0),
        MaxScrollDepth          INT             NOT NULL CONSTRAINT DF_Session_Scroll DEFAULT (0),
        VideoWatchSeconds       INT             NOT NULL CONSTRAINT DF_Session_VideoSec DEFAULT (0),
        VideoWatchPercent       INT             NOT NULL CONSTRAINT DF_Session_VideoPct DEFAULT (0),
        RegisterClicked         BIT             NOT NULL CONSTRAINT DF_Session_RegClick DEFAULT (0),
        FileClicked             BIT             NOT NULL CONSTRAINT DF_Session_FileClick DEFAULT (0),
        RegisterClickedAt       DATETIME2(3)    NULL,
        FileClickedAt           DATETIME2(3)    NULL,
        TimeToFirstInteractionMs INT            NOT NULL CONSTRAINT DF_Session_TTI DEFAULT (0),
        CONSTRAINT PK_UserSession PRIMARY KEY CLUSTERED (SessionId),
        CONSTRAINT FK_UserSession_TaxpayerRecipient
            FOREIGN KEY (RecipientId) REFERENCES dbo.TaxpayerRecipient(RecipientId)
    );

    CREATE INDEX IX_Session_RecipientId ON dbo.UserSession(RecipientId);
    CREATE INDEX IX_Session_CampaignId ON dbo.UserSession(CampaignId);
    CREATE INDEX IX_Session_Campaign_StartedAt ON dbo.UserSession(CampaignId, StartedAt);
    CREATE INDEX IX_Session_IsBot ON dbo.UserSession(IsBot);
END
GO

-- =============================================================================
-- EventLog (append-only, partition-friendly: EventTime is in the PK)
-- =============================================================================
IF OBJECT_ID('dbo.EventLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventLog (
        EventId          BIGINT IDENTITY(1,1) NOT NULL,
        EventTime        DATETIME2(3)    NOT NULL,
        SessionId        UNIQUEIDENTIFIER NOT NULL,
        RecipientId      BIGINT          NOT NULL,
        CampaignId       BIGINT          NOT NULL,
        EventType        INT             NOT NULL,
        EventTypeName    NVARCHAR(32)    NULL,
        EventValue       NVARCHAR(512)   NULL,
        DurationSeconds  INT             NULL,
        ScrollDepth      INT             NULL,
        PageUrl          NVARCHAR(2048)  NULL,
        ExtraJson        NVARCHAR(MAX)   NULL,
        ClientEventId    NVARCHAR(64)    NULL,
        CONSTRAINT PK_EventLog PRIMARY KEY CLUSTERED (EventTime, EventId),
        CONSTRAINT FK_EventLog_UserSession
            FOREIGN KEY (SessionId) REFERENCES dbo.UserSession(SessionId)
    );

    CREATE INDEX IX_Event_Campaign_Time   ON dbo.EventLog(CampaignId, EventTime);
    CREATE INDEX IX_Event_Session_Time    ON dbo.EventLog(SessionId, EventTime);
    CREATE INDEX IX_Event_Recipient_Time  ON dbo.EventLog(RecipientId, EventTime);
    CREATE INDEX IX_Event_Type_Time       ON dbo.EventLog(EventType, EventTime);
    CREATE INDEX IX_Event_ClientEventId   ON dbo.EventLog(ClientEventId)
        WHERE ClientEventId IS NOT NULL;
END
GO

-- =============================================================================
-- Idempotent column additions (for databases created before a column existed).
-- =============================================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.EventLog') AND name = 'EventTypeName')
BEGIN
    ALTER TABLE dbo.EventLog ADD EventTypeName NVARCHAR(32) NULL;
END
GO

-- Backfill existing rows so old events get readable names too.
UPDATE dbo.EventLog
SET EventTypeName = CASE EventType
    WHEN 1  THEN N'PageOpen'
    WHEN 2  THEN N'PageClose'
    WHEN 3  THEN N'Heartbeat'
    WHEN 4  THEN N'ScrollDepth'
    WHEN 10 THEN N'VideoPlay'
    WHEN 11 THEN N'VideoPause'
    WHEN 12 THEN N'VideoComplete'
    WHEN 13 THEN N'VideoProgress'
    WHEN 20 THEN N'RegisterClick'
    WHEN 21 THEN N'FileClick'
    WHEN 22 THEN N'CtaClick'
    WHEN 23 THEN N'OutboundRedirect'
    WHEN 30 THEN N'Bounce'
    WHEN 50 THEN N'Engagement'
    WHEN 90 THEN N'Error'
    WHEN 99 THEN N'BotDetected'
    ELSE CONCAT(N'Unknown(', EventType, N')')
END
WHERE EventTypeName IS NULL;
GO

-- =============================================================================
-- Seed campaigns (one per page template). Idempotent via MERGE.
-- =============================================================================
MERGE INTO dbo.CampaignMaster AS tgt
USING (VALUES
    (N'GENERAL',     N'General Awareness',  N'Index'),
    (N'ENFORCEMENT', N'Enforcement Notice', N'Enforcement'),
    (N'COMBINED',    N'Combined Messaging', N'Combined'),
    (N'CIVIC',       N'Civic Duty',         N'CivicDuty')
) AS src(CampaignCode, Name, PageTemplate)
ON tgt.CampaignCode = src.CampaignCode
WHEN NOT MATCHED THEN
    INSERT (CampaignCode, Name, PageTemplate, Status, StartDate, CreatedAt)
    VALUES (src.CampaignCode, src.Name, src.PageTemplate, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
GO
