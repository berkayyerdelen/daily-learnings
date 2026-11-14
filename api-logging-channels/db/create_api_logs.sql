IF DB_ID(N'api_logging') IS NULL CREATE DATABASE api_logging;
GO
USE api_logging;
GO

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
