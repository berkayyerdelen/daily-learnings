using ApiLogging.Api.Logging;

namespace ApiLogging.Tests;

public sealed class ApiLogDataReaderTests
{
    [Fact]
    public void Streams_each_log_as_a_row_and_maps_nulls_to_DBNull()
    {
        var at = DateTime.UtcNow;
        var logs = new[]
        {
            new ApiLog(at, "t1", "POST", "/a", "?x=1", 201, 5, "{}", "{\"id\":1}", null),
            new ApiLog(at, "t2", "GET", "/b", null, 404, 1, null, null, "boom")
        };
        using var reader = new ApiLogDataReader(logs);

        Assert.Equal(10, reader.FieldCount);
        Assert.True(reader.Read());
        Assert.Equal(at, reader.GetValue(reader.GetOrdinal("CreatedAt")));
        Assert.Equal(201, reader.GetValue(reader.GetOrdinal("StatusCode")));
        Assert.Equal("{\"id\":1}", reader.GetValue(reader.GetOrdinal("ResponseBody")));
        Assert.True(reader.IsDBNull(reader.GetOrdinal("Exception")));

        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(reader.GetOrdinal("QueryString")));
        Assert.Equal("boom", reader.GetValue(reader.GetOrdinal("Exception")));
        Assert.False(reader.Read());
    }
}
