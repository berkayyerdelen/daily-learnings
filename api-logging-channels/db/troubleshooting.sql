-- Troubleshooting queries for dbo.ApiLogs, safe to run while the API is inserting.
--
-- READ UNCOMMITTED: these queries take no row locks, so they never wait for in-progress bulk inserts
-- and never block them, without any database setting. Trade-off: a query can briefly show rows from a
-- batch that is still being inserted (and, rarely, one that then rolls back). That's fine for logs.
--
-- No extra indexes needed: Id only ever increases, so it doubles as a time order. "Latest" queries
-- read the primary key backwards from the end (fast); the others are limited to a recent Id range
-- so they never scan the whole table.
USE api_logging;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
GO

-- How far back to look: the last N rows. Raise it to search further back (slower).
DECLARE @recentRows bigint = 200000;
DECLARE @fromId bigint = (SELECT ISNULL(MAX(Id), 0) FROM dbo.ApiLogs) - @recentRows;

-- 1. Latest errors, newest first (stops as soon as it has 100).
SELECT TOP (100) Id, CreatedAt, StatusCode, Method, Path, DurationMs, TraceId
FROM dbo.ApiLogs
WHERE StatusCode >= 400
ORDER BY Id DESC;

-- 2. Latest requests to one endpoint.
SELECT TOP (100) Id, CreatedAt, StatusCode, Method, Path, DurationMs, TraceId
FROM dbo.ApiLogs
WHERE Path = N'/api/orders'
ORDER BY Id DESC;

-- 3. One request by trace id (from an error message, a user report or your tracing tool), with
--    bodies. Searches the recent range; widen @recentRows if it's older.
DECLARE @traceId varchar(64) = '<trace id>';
SELECT Id, CreatedAt, Method, Path, QueryString, StatusCode, DurationMs, RequestBody, ResponseBody, Exception
FROM dbo.ApiLogs
WHERE Id > @fromId AND TraceId = @traceId;

-- 4. Full row (bodies, exception) once you have the Id from another query.
DECLARE @id bigint = 0;
SELECT * FROM dbo.ApiLogs WHERE Id = @id;

-- 5. Slowest recent requests.
SELECT TOP (50) Id, CreatedAt, Method, Path, StatusCode, DurationMs, TraceId
FROM dbo.ApiLogs
WHERE Id > @fromId
ORDER BY DurationMs DESC;

-- 6. Error rate per minute over the recent range.
SELECT DATEADD(MINUTE, DATEDIFF(MINUTE, 0, CreatedAt), 0) AS Minute,
       COUNT(*) AS Requests,
       SUM(CASE WHEN StatusCode >= 500 THEN 1 ELSE 0 END) AS ServerErrors,
       SUM(CASE WHEN StatusCode BETWEEN 400 AND 499 THEN 1 ELSE 0 END) AS ClientErrors
FROM dbo.ApiLogs
WHERE Id > @fromId
GROUP BY DATEADD(MINUTE, DATEDIFF(MINUTE, 0, CreatedAt), 0)
ORDER BY Minute DESC;
