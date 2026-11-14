-- Troubleshooting queries for dbo.ApiLogs. Safe to run while the API is inserting: with
-- READ_COMMITTED_SNAPSHOT they read the last committed rows and neither wait for nor block inserts.
-- Each query is shaped to use one of the indexes in create_api_logs.sql. Keep a time range on
-- list queries: without one, they scan the whole table.
USE api_logging;
GO

-- 1. Recent errors (last hour), newest first. Uses IX_ApiLogs_Errors.
SELECT TOP (100) Id, CreatedAt, StatusCode, Method, DurationMs, TraceId
FROM dbo.ApiLogs
WHERE StatusCode >= 400 AND CreatedAt >= DATEADD(HOUR, -1, SYSUTCDATETIME())
ORDER BY CreatedAt DESC;

-- 2. Everything in a time window. Uses IX_ApiLogs_CreatedAt.
DECLARE @from datetime2 = DATEADD(MINUTE, -15, SYSUTCDATETIME()), @to datetime2 = SYSUTCDATETIME();
SELECT TOP (500) Id, CreatedAt, StatusCode, Method, DurationMs, TraceId
FROM dbo.ApiLogs
WHERE CreatedAt BETWEEN @from AND @to
ORDER BY CreatedAt DESC;

-- 3. One request by trace id (from an error message, a user report or your tracing tool),
--    with bodies. Uses IX_ApiLogs_TraceId.
DECLARE @traceId varchar(64) = '<trace id>';
SELECT Id, CreatedAt, Method, Path, QueryString, StatusCode, DurationMs, RequestBody, ResponseBody, Exception
FROM dbo.ApiLogs
WHERE TraceId = @traceId;

-- 4. Full row (bodies, exception) once you have the Id from query 1 or 2. Uses the primary key.
DECLARE @id bigint = 0;
SELECT * FROM dbo.ApiLogs WHERE Id = @id;

-- 5. Slowest requests in the last hour. Uses IX_ApiLogs_CreatedAt for the range.
SELECT TOP (50) Id, CreatedAt, Method, StatusCode, DurationMs, TraceId
FROM dbo.ApiLogs
WHERE CreatedAt >= DATEADD(HOUR, -1, SYSUTCDATETIME())
ORDER BY DurationMs DESC;

-- 6. Error rate per minute (last hour). Uses IX_ApiLogs_CreatedAt.
SELECT DATEADD(MINUTE, DATEDIFF(MINUTE, 0, CreatedAt), 0) AS Minute,
       COUNT(*) AS Requests,
       SUM(CASE WHEN StatusCode >= 500 THEN 1 ELSE 0 END) AS ServerErrors,
       SUM(CASE WHEN StatusCode BETWEEN 400 AND 499 THEN 1 ELSE 0 END) AS ClientErrors
FROM dbo.ApiLogs
WHERE CreatedAt >= DATEADD(HOUR, -1, SYSUTCDATETIME())
GROUP BY DATEADD(MINUTE, DATEDIFF(MINUTE, 0, CreatedAt), 0)
ORDER BY Minute DESC;
