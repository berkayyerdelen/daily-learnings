using System.Collections;
using System.Data.Common;

namespace ApiLogging.Api.Logging;

/// <summary>
/// Forward-only reader over one batch, so SqlBulkCopy streams values straight from the logs.
/// Compared with a DataTable there is no second copy of the batch (no DataRow, no object[] per
/// row) and no shared state, so parallel writers can each use their own reader safely.
/// Only the members SqlBulkCopy uses are implemented.
/// </summary>
internal sealed class ApiLogDataReader(IReadOnlyList<ApiLog> logs) : DbDataReader
{
    internal static readonly string[] Columns =
    [
        nameof(ApiLog.CreatedAt), nameof(ApiLog.TraceId), nameof(ApiLog.Method), nameof(ApiLog.Path),
        nameof(ApiLog.QueryString), nameof(ApiLog.StatusCode), nameof(ApiLog.DurationMs),
        nameof(ApiLog.RequestBody), nameof(ApiLog.ResponseBody), nameof(ApiLog.Exception)
    ];

    private static readonly Type[] Types =
    [
        typeof(DateTime), typeof(string), typeof(string), typeof(string), typeof(string),
        typeof(int), typeof(long), typeof(string), typeof(string), typeof(string)
    ];

    private int _row = -1;

    public override bool Read() => ++_row < logs.Count;

    public override object GetValue(int ordinal)
    {
        var l = logs[_row];
        return ordinal switch
        {
            0 => l.CreatedAt,
            1 => l.TraceId,
            2 => l.Method,
            3 => l.Path,
            4 => (object?)l.QueryString ?? DBNull.Value,
            5 => l.StatusCode,
            6 => l.DurationMs,
            7 => (object?)l.RequestBody ?? DBNull.Value,
            8 => (object?)l.ResponseBody ?? DBNull.Value,
            9 => (object?)l.Exception ?? DBNull.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(ordinal))
        };
    }

    public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;
    public override int FieldCount => Columns.Length;
    public override string GetName(int ordinal) => Columns[ordinal];
    public override int GetOrdinal(string name) => Array.IndexOf(Columns, name);
    public override Type GetFieldType(int ordinal) => Types[ordinal];
    public override string GetDataTypeName(int ordinal) => Types[ordinal].Name;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));
    public override bool HasRows => logs.Count > 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => -1;
    public override int Depth => 0;
    public override bool NextResult() => false;

    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++) values[i] = GetValue(i);
        return count;
    }

    public override string GetString(int ordinal) => (string)GetValue(ordinal);
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);

    public override IEnumerator GetEnumerator() => throw new NotSupportedException();
    public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
    public override byte GetByte(int ordinal) => throw new NotSupportedException();
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override char GetChar(int ordinal) => throw new NotSupportedException();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
    public override double GetDouble(int ordinal) => throw new NotSupportedException();
    public override float GetFloat(int ordinal) => throw new NotSupportedException();
    public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
    public override short GetInt16(int ordinal) => throw new NotSupportedException();
}
