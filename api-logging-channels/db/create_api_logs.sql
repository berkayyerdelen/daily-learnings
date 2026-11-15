-- The only thing the application needs: one plain table. No database settings, no extra indexes,
-- no lock settings. The application keeps transactions short and below SQL Server's lock-escalation
-- threshold, and troubleshooting queries read without locks (see troubleshooting.sql).
IF DB_ID(N'api_logging') IS NULL CREATE DATABASE api_logging;
GO
USE api_logging;
GO

IF OBJECT_ID(N'dbo.ApiLogs', N'U') IS NULL
CREATE TABLE dbo.ApiLogs (
    Id           bigint IDENTITY(1,1) PRIMARY KEY,
    CreatedAt    datetime2       NOT NULL,
    TraceId      varchar(64)     NOT NULL,  -- server-generated: always <= 32 chars
    -- Method and Path come from the client and can be longer than any fixed size (Kestrel allows
    -- request lines up to 8 KB). A value that doesn't fit would fail the whole bulk insert batch
    -- permanently, so they are unbounded.
    Method       varchar(max)    NOT NULL,
    Path         nvarchar(max)   NOT NULL,
    QueryString  nvarchar(max)   NULL,
    StatusCode   int             NOT NULL,
    DurationMs   bigint          NOT NULL,
    RequestBody  nvarchar(max)   NULL,
    ResponseBody nvarchar(max)   NULL,
    Exception    nvarchar(max)   NULL
);
GO
