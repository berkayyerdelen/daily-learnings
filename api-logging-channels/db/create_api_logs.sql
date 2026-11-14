IF DB_ID(N'api_logging') IS NULL CREATE DATABASE api_logging;
GO

-- Readers (reports, Rider, SELECT COUNT(*)) see the last committed rows instead of waiting for
-- in-progress bulk inserts, and never block them. Without this, one slow insert also blocks every
-- query on the table.
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
    -- Parallel writers all insert at the end of this ever-increasing key. SQL Server 2019+
    -- manages that hot last page better with OPTIMIZE_FOR_SEQUENTIAL_KEY.
    CONSTRAINT PK_ApiLogs PRIMARY KEY CLUSTERED (Id) WITH (OPTIMIZE_FOR_SEQUENTIAL_KEY = ON)
);
GO

-- A 5,000-row bulk insert takes >5,000 row locks, and SQL Server would try to swap them for one
-- table lock. If that succeeds, the other writers wait until it commits: not a deadlock, but the
-- writers take turns and parallelism is lost. This table is append-only, so keep row locks.
-- Consequence: delete old rows in chunks (DELETE TOP (5000) ... in a loop), never all at once.
ALTER TABLE dbo.ApiLogs SET (LOCK_ESCALATION = DISABLE);
GO
