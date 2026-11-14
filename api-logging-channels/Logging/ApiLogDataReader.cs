using System.Collections;
using System.Data.Common;

namespace ApiLogging.Api.Logging;

/// <summary>
/// Forward-only reader over a batch, so <c>SqlBulkCopy</c> can stream rows straight from
/// the entries without building a DataTable. Only the members bulk copy uses are implemented.
/// </summary>
internal sealed class ApiLogDataReader(IReadOnlyList<ApiLogEntry> rows) : DbDataReader
{
    internal static readonly string[] Columns =
    [
        "OccurredAt", "TraceId", "HttpMethod", "Path", "QueryString", "Endpoint", "StatusCode",
        "DurationMs", "RequestBody", "ResponseBody", "ClientIp", "UserAgent", "UserId", "Exception"
    ];

    private static readonly Type[] ColumnTypes =
    [
        typeof(DateTimeOffset), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
        typeof(int), typeof(double), typeof(string), typeof(string), typeof(string), typeof(string),
        typeof(string), typeof(string)
    ];

    private int _index = -1;

    private ApiLogEntry Current => rows[_index];

    public override int FieldCount => Columns.Length;
    public override bool HasRows => rows.Count > 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => -1;
    public override int Depth => 0;

    public override bool Read() => ++_index < rows.Count;
    public override bool NextResult() => false;

    public override string GetName(int ordinal) => Columns[ordinal];
    public override int GetOrdinal(string name) => Array.IndexOf(Columns, name);
    public override Type GetFieldType(int ordinal) => ColumnTypes[ordinal];
    public override string GetDataTypeName(int ordinal) => ColumnTypes[ordinal].Name;

    public override object GetValue(int ordinal) => ordinal switch
    {
        0 => Current.OccurredAt,
        1 => Current.TraceId,
        2 => Current.HttpMethod,
        3 => Current.Path,
        4 => (object?)Current.QueryString ?? DBNull.Value,
        5 => (object?)Current.Endpoint ?? DBNull.Value,
        6 => Current.StatusCode,
        7 => Current.DurationMs,
        8 => (object?)Current.RequestBody ?? DBNull.Value,
        9 => (object?)Current.ResponseBody ?? DBNull.Value,
        10 => (object?)Current.ClientIp ?? DBNull.Value,
        11 => (object?)Current.UserAgent ?? DBNull.Value,
        12 => (object?)Current.UserId ?? DBNull.Value,
        13 => (object?)Current.Exception ?? DBNull.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(ordinal))
    };

    public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;

    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++) values[i] = GetValue(i);
        return count;
    }

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override string GetString(int ordinal) => (string)GetValue(ordinal);
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);

    public override IEnumerator GetEnumerator() => throw new NotSupportedException();
    public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
    public override byte GetByte(int ordinal) => throw new NotSupportedException();
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override char GetChar(int ordinal) => throw new NotSupportedException();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
    public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
    public override float GetFloat(int ordinal) => throw new NotSupportedException();
    public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
    public override short GetInt16(int ordinal) => throw new NotSupportedException();
    public override long GetInt64(int ordinal) => throw new NotSupportedException();
}
