-- Optional: monthly partitioning for dbo.EventLog. Run AFTER schema.sql, and BEFORE
-- the table receives heavy traffic (recreating the clustered index is cheap on an
-- empty table, expensive otherwise). Adjust the right-edge dates for your range.

IF NOT EXISTS (SELECT 1 FROM sys.partition_functions WHERE name = 'PF_EventLog_Monthly')
BEGIN
    CREATE PARTITION FUNCTION PF_EventLog_Monthly(DATETIME2(3))
    AS RANGE RIGHT FOR VALUES (
        '2026-01-01', '2026-02-01', '2026-03-01', '2026-04-01',
        '2026-05-01', '2026-06-01', '2026-07-01', '2026-08-01',
        '2026-09-01', '2026-10-01', '2026-11-01', '2026-12-01',
        '2027-01-01'
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.partition_schemes WHERE name = 'PS_EventLog_Monthly')
BEGIN
    CREATE PARTITION SCHEME PS_EventLog_Monthly AS PARTITION PF_EventLog_Monthly ALL TO ([PRIMARY]);
END
GO

-- Move EventLog onto the partition scheme by recreating its clustered index.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'PK_EventLog' AND object_id = OBJECT_ID('dbo.EventLog'))
BEGIN
    ALTER TABLE dbo.EventLog DROP CONSTRAINT PK_EventLog;
    CREATE CLUSTERED INDEX CIX_EventLog_Partitioned
        ON dbo.EventLog(EventTime, EventId)
        ON PS_EventLog_Monthly(EventTime);
    ALTER TABLE dbo.EventLog ADD CONSTRAINT PK_EventLog
        PRIMARY KEY NONCLUSTERED (EventTime, EventId);
END
GO

-- Sliding window helper: run monthly to add the next boundary.
-- ALTER PARTITION SCHEME PS_EventLog_Monthly NEXT USED [PRIMARY];
-- ALTER PARTITION FUNCTION PF_EventLog_Monthly() SPLIT RANGE ('2027-02-01');
