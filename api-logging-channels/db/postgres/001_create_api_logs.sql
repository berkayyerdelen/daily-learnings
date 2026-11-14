-- Kept in sync with PostgresApiLogStore.CreateTableSql.
-- Production: run this as a migration and consider monthly partitioning on occurred_at
-- so retention is a cheap DROP of an old partition instead of a large DELETE.

CREATE TABLE IF NOT EXISTS api_logs (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at   timestamptz      NOT NULL,
    trace_id      text             NOT NULL,
    http_method   text             NOT NULL,
    path          text             NOT NULL,
    query_string  text             NULL,
    endpoint      text             NULL,
    status_code   integer          NOT NULL,
    duration_ms   double precision NOT NULL,
    request_body  text             NULL,
    response_body text             NULL,
    client_ip     text             NULL,
    user_agent    text             NULL,
    user_id       text             NULL,
    exception     text             NULL
);

-- Append-only, time-ordered data: BRIN is tiny and almost free to maintain on insert.
CREATE INDEX IF NOT EXISTS ix_api_logs_occurred_at ON api_logs USING brin (occurred_at);
CREATE INDEX IF NOT EXISTS ix_api_logs_trace_id ON api_logs (trace_id);
