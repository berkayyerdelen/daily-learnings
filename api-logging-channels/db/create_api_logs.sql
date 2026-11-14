IF DB_ID(N'api_logging') IS NULL CREATE DATABASE api_logging;
GO

-- Readers (troubleshooting queries, Rider, SELECT COUNT(*)) see the last committed rows instead of
-- waiting for in-progress bulk inserts, and never block them.
ALTER DATABASE api_logging SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO

USE api_logging;
GO

DROP TABLE IF EXISTS dbo.ApiLogs;
GO

CREATE TABLE dbo.ApiLogs (
    Id           bigint IDENTITY(1,1) NOT NULL,
    CreatedAt    datetime2       NOT NULL,
    TraceId      varchar(64)     NOT NULL,
    Method       varchar(10)     NOT NULL,
    Path         nvarchar(2048)  NOT NULL,
    QueryString  nvarchar(max)   NULL,
    StatusCode   int             NOT NULL,
    DurationMs   bigint          NOT NULL,
    RequestBody  nvarchar(max)   NULL,
    ResponseBody nvarchar(max)   NULL,
    Exception    nvarchar(max)   NULL,
    -- All writers insert at the end of this ever-increasing key (SQL Server 2019+ handles that hot
    -- page better with OPTIMIZE_FOR_SEQUENTIAL_KEY).
    CONSTRAINT PK_ApiLogs PRIMARY KEY CLUSTERED (Id)
        WITH (OPTIMIZE_FOR_SEQUENTIAL_KEY = ON, ALLOW_PAGE_LOCKS = OFF)
);
GO

-- Troubleshooting indexes. Each one makes every insert a bit slower, so only the ones the common
-- questions need. Bodies (nvarchar(max)) are never indexed: find the row, then fetch it by Id.
--
-- ALLOW_PAGE_LOCKS = OFF everywhere: parallel writers each insert their own new rows, so with row
-- locks only they never wait on each other's locks, and can't deadlock across two indexes (writer A
-- holding a page in one index while waiting for B's page in another). Cost: more lock memory per
-- batch, and ALTER INDEX ... REORGANIZE is not possible (REBUILD is).

-- "What happened between 14:00 and 14:05?" Appends at the end, like the primary key: cheap.
CREATE INDEX IX_ApiLogs_CreatedAt ON dbo.ApiLogs (CreatedAt)
    INCLUDE (StatusCode, Method, DurationMs, TraceId)
    WITH (OPTIMIZE_FOR_SEQUENTIAL_KEY = ON, ALLOW_PAGE_LOCKS = OFF);

-- "Show me this request": the trace id from an error message, a user report or your tracing tool.
-- Random values, so this is the most expensive index for inserts; drop it if you never look up by trace.
CREATE INDEX IX_ApiLogs_TraceId ON dbo.ApiLogs (TraceId)
    WITH (ALLOW_PAGE_LOCKS = OFF);

-- "Recent errors": only 4xx/5xx rows are written to it, so it stays small and nearly free.
CREATE INDEX IX_ApiLogs_Errors ON dbo.ApiLogs (CreatedAt)
    INCLUDE (StatusCode, Method, DurationMs, TraceId)
    WHERE StatusCode >= 400
    WITH (ALLOW_PAGE_LOCKS = OFF);
GO

-- A 5,000-row bulk insert takes >5,000 row locks, and SQL Server would try to swap them for one table
-- lock, making the writers take turns. This table is append-only, so keep row locks.
-- Consequence: delete old rows in chunks (DELETE TOP (5000) ... in a loop), never all at once.
ALTER TABLE dbo.ApiLogs SET (LOCK_ESCALATION = DISABLE);
GO
