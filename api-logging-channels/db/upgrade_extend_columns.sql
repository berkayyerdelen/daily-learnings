-- One-off upgrade for tables created with the earlier script (Method varchar(10), Path nvarchar(2048)).
-- A longer client-supplied method or path failed the whole bulk insert batch; unbounded columns can't.
-- Safe to run more than once. On a large table, run it at a quiet time: SQL Server may need to touch
-- every row. If you added indexes that use these columns, drop them first and recreate them after.
USE api_logging;
GO

IF COL_LENGTH(N'dbo.ApiLogs', N'Path') <> -1      -- -1 means already nvarchar(max)
    ALTER TABLE dbo.ApiLogs ALTER COLUMN Path nvarchar(max) NOT NULL;
GO

IF COL_LENGTH(N'dbo.ApiLogs', N'Method') <> -1
    ALTER TABLE dbo.ApiLogs ALTER COLUMN Method varchar(max) NOT NULL;
GO
