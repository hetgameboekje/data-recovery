using RecoveryBLL.Exceptions;

namespace RecoveryDAL.Storage;

/// <summary>
/// Read-only wrapper that gives every read a deadline. A failing disk can retry one bad sector for minutes;
/// without this a copy or hash would hang on it. The file is opened for overlapped I/O, so a late read is
/// really cancelled (CancelIoEx), not just abandoned.
/// </summary>
public sealed class TimeoutStream(Stream inner, TimeSpan timeout, string path) : Stream
{
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var rent = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            int n;
            try { n = inner.ReadAsync(rent.AsMemory(0, buffer.Length), cts.Token).AsTask().GetAwaiter().GetResult(); }
            catch (OperationCanceledException)
            {
                throw new ReadTimeoutException($"reading did not finish within {timeout.TotalSeconds:0} s (bad sector?): {path}");
            }
            rent.AsSpan(0, n).CopyTo(buffer);
            return n;
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(rent); }
    }

    public override bool CanRead => true;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
