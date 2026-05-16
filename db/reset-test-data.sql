/* =================================================================
   Test data reset for TaxpayerAnalytics

   Three reset levels (run only the one you want):

     Level 1  Clear EventLog + UserSession; reset recipient visit counters
              Keeps recipients (and their tracking tokens) so existing
              SMS links keep working.

     Level 2  Level 1 + delete recipients. Next app start will recreate
              the 4 dummy recipients via DummyDataSeeder.

     Level 3  Wipe everything except CampaignMaster (rare; you usually
              don't need this).

   Plus inspection queries at the bottom for quick sanity checks.
   ================================================================= */

USE TaxpayerAnalytics;
GO

------------------------------------------------------------------
-- Level 1 — keep recipients, wipe their behaviour history
------------------------------------------------------------------
DELETE FROM dbo.EventLog;
DELETE FROM dbo.UserSession;

UPDATE dbo.TaxpayerRecipient
SET VisitCount     = 0,
    FirstVisitAt   = NULL,
    LastVisitAt    = NULL;

DBCC CHECKIDENT ('dbo.EventLog', RESEED, 0);
GO

------------------------------------------------------------------
-- Level 2 — also delete recipients (dummy ones auto-reseed on next run)
-- Uncomment the block below if you want a full recipient reset.
------------------------------------------------------------------
-- DELETE FROM dbo.EventLog;
-- DELETE FROM dbo.UserSession;
-- DELETE FROM dbo.TaxpayerRecipient;
-- DBCC CHECKIDENT ('dbo.TaxpayerRecipient', RESEED, 0);
-- DBCC CHECKIDENT ('dbo.EventLog',          RESEED, 0);
-- GO

------------------------------------------------------------------
-- Level 3 — wipe everything except CampaignMaster
-- Use this only if you really want a clean slate, e.g. before
-- changing the schema. Run the app once after to reseed dummies.
------------------------------------------------------------------
-- DELETE FROM dbo.EventLog;
-- DELETE FROM dbo.UserSession;
-- DELETE FROM dbo.TaxpayerRecipient;
-- DBCC CHECKIDENT ('dbo.TaxpayerRecipient', RESEED, 0);
-- DBCC CHECKIDENT ('dbo.EventLog',          RESEED, 0);
-- GO

------------------------------------------------------------------
-- Inspection — quick sanity checks
------------------------------------------------------------------
SELECT 'CampaignMaster'     AS TableName, COUNT(*) AS Rows FROM dbo.CampaignMaster
UNION ALL SELECT 'TaxpayerRecipient', COUNT(*) FROM dbo.TaxpayerRecipient
UNION ALL SELECT 'UserSession',       COUNT(*) FROM dbo.UserSession
UNION ALL SELECT 'EventLog',          COUNT(*) FROM dbo.EventLog;

-- Most recent 10 sessions (everything you'd want at a glance)
SELECT TOP 10
    s.StartedAt, s.RecipientId, s.CampaignId,
    s.Browser, s.OperatingSystem, s.DeviceType, s.Country, s.City,
    s.DurationSeconds, s.MaxScrollDepth, s.VideoWatchPercent,
    s.RegisterClicked, s.FileClicked, s.IsBot, s.IsBounce, s.IsEngaged,
    s.TimeToFirstInteractionMs, s.EndedAt, s.SessionId
FROM dbo.UserSession s
ORDER BY s.StartedAt DESC;

-- Most recent 50 events
SELECT TOP 50
    e.EventTime, e.EventType, e.EventTypeName,
    e.EventValue, e.DurationSeconds, e.ScrollDepth, e.PageUrl, e.SessionId
FROM dbo.EventLog e
ORDER BY e.EventTime DESC;

-- Per-campaign roll-up (excluding bots)
SELECT
    c.CampaignCode,
    c.PageTemplate,
    COUNT(s.SessionId)                                AS Sessions,
    SUM(CASE WHEN s.RegisterClicked = 1 THEN 1 ELSE 0 END) AS RegisterClicks,
    SUM(CASE WHEN s.FileClicked     = 1 THEN 1 ELSE 0 END) AS FileClicks,
    AVG(CAST(s.DurationSeconds   AS FLOAT))           AS AvgDurationSec,
    AVG(CAST(s.MaxScrollDepth    AS FLOAT))           AS AvgScrollPct,
    AVG(CAST(s.VideoWatchPercent AS FLOAT))           AS AvgVideoPct
FROM dbo.CampaignMaster c
LEFT JOIN dbo.UserSession s
       ON s.CampaignId = c.CampaignId AND s.IsBot = 0
GROUP BY c.CampaignCode, c.PageTemplate
ORDER BY c.CampaignCode;
