-- Kept in sync with SqlServerApiLogStore.CreateTableSql.
-- For very high volume see README ("At 100k requests per second"): clustered columnstore,
-- daily partitions and partition switching for retention.

IF DB_ID(N'api_logging') IS NULL CREATE DATABASE api_logging;
GO
USE api_logging;
GO

IF OBJECT_ID(N'dbo.ApiLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApiLogs (
        Id           bigint IDENTITY(1,1) NOT NULL,
        OccurredAt   datetimeoffset(7) NOT NULL,
        TraceId      varchar(64)       NOT NULL,
        HttpMethod   varchar(16)       NOT NULL,
        Path         nvarchar(2048)    NOT NULL,
        QueryString  nvarchar(max)     NULL,
        Endpoint     nvarchar(256)     NULL,
        StatusCode   int               NOT NULL,
        DurationMs   float             NOT NULL,
        RequestBody  nvarchar(max)     NULL,
        ResponseBody nvarchar(max)     NULL,
        ClientIp     varchar(45)       NULL,
        UserAgent    nvarchar(512)     NULL,
        UserId       nvarchar(256)     NULL,
        Exception    nvarchar(max)     NULL,
        -- Ever-increasing key + many concurrent writers = last-page latch contention.
        -- OPTIMIZE_FOR_SEQUENTIAL_KEY (SQL Server 2019+) mitigates it.
        CONSTRAINT PK_ApiLogs PRIMARY KEY CLUSTERED (Id) WITH (OPTIMIZE_FOR_SEQUENTIAL_KEY = ON)
    );
    CREATE INDEX IX_ApiLogs_OccurredAt ON dbo.ApiLogs (OccurredAt);
    CREATE INDEX IX_ApiLogs_TraceId ON dbo.ApiLogs (TraceId);
END
GO
