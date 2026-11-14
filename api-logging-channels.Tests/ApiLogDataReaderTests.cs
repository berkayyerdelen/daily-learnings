using ApiLogging.Api.Logging;

namespace ApiLogging.Tests;

public sealed class ApiLogDataReaderTests
{
    [Fact]
    public void Streams_rows_and_maps_nulls_to_DBNull()
    {
        var at = DateTimeOffset.UtcNow;
        var rows = new[]
        {
            new ApiLogEntry
            {
                OccurredAt = at, TraceId = "t1", HttpMethod = "POST", Path = "/a", StatusCode = 201,
                DurationMs = 1.5, RequestBody = "{}", Exception = null
            },
            new ApiLogEntry { OccurredAt = at, TraceId = "t2", HttpMethod = "GET", Path = "/b", StatusCode = 404, DurationMs = 2 }
        };

        using var reader = new ApiLogDataReader(rows);

        Assert.Equal(14, reader.FieldCount);
        Assert.True(reader.Read());
        Assert.Equal(at, reader.GetValue(reader.GetOrdinal("OccurredAt")));
        Assert.Equal("t1", reader.GetValue(reader.GetOrdinal("TraceId")));
        Assert.Equal(201, reader.GetValue(reader.GetOrdinal("StatusCode")));
        Assert.Equal("{}", reader.GetValue(reader.GetOrdinal("RequestBody")));
        Assert.True(reader.IsDBNull(reader.GetOrdinal("Exception")));

        Assert.True(reader.Read());
        Assert.Equal("/b", reader.GetValue(reader.GetOrdinal("Path")));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Every_column_has_a_value_and_a_type()
    {
        var entry = new ApiLogEntry { OccurredAt = default, TraceId = "", HttpMethod = "", Path = "", StatusCode = 0, DurationMs = 0 };
        using var reader = new ApiLogDataReader([entry]);
        reader.Read();

        for (var i = 0; i < reader.FieldCount; i++)
        {
            Assert.NotNull(reader.GetFieldType(i));
            Assert.NotNull(reader.GetValue(i));
        }
    }
}
