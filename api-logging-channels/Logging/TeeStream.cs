using System.Buffers;
using System.Text;

namespace ApiLogging.Api.Logging;

/// <summary>
/// Write-only stream that forwards everything to the real response body and keeps a copy in a
/// pooled buffer. Copying bytes is far cheaper than serializing the response object again.
/// It never disposes the inner stream: the response owns it.
/// </summary>
internal sealed class TeeStream(Stream inner) : Stream
{
    private byte[] _buffer = [];
    private int _length;

    public Stream Inner => inner;

    public string GetText() => Encoding.UTF8.GetString(_buffer, 0, _length);

    /// <summary>Returns the buffer to the pool. Call once the copy is no longer needed.</summary>
    public void ReleaseBuffer()
    {
        if (_buffer.Length > 0) ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
        _length = 0;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Copy(buffer.AsSpan(offset, count));
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Copy(buffer);
        inner.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Copy(buffer.AsSpan(offset, count));
        return inner.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Copy(buffer.Span);
        return inner.WriteAsync(buffer, cancellationToken);
    }

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    private void Copy(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return;

        var required = _length + data.Length;
        if (required > _buffer.Length)
        {
            var grown = ArrayPool<byte>.Shared.Rent(Math.Max(required, Math.Max(4096, _buffer.Length * 2)));
            _buffer.AsSpan(0, _length).CopyTo(grown);
            if (_buffer.Length > 0) ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = grown;
        }

        data.CopyTo(_buffer.AsSpan(_length));
        _length = required;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
