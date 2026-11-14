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
    TraceId      varchar(64)     NOT NULL,
    Method       varchar(10)     NOT NULL,
    Path         nvarchar(2048)  NOT NULL,
    QueryString  nvarchar(max)   NULL,
    StatusCode   int             NOT NULL,
    DurationMs   bigint          NOT NULL,
    RequestBody  nvarchar(max)   NULL,
    ResponseBody nvarchar(max)   NULL,
    Exception    nvarchar(max)   NULL
);
GO
