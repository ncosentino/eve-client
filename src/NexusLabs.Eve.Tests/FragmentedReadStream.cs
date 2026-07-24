namespace NexusLabs.Eve.Tests;

internal sealed class FragmentedReadStream : Stream
{
    private readonly MemoryStream _inner;
    private readonly int _maximumBytesPerRead;

    internal FragmentedReadStream(byte[] content, int maximumBytesPerRead)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytesPerRead);
        _inner = new MemoryStream(content, writable: false);
        _maximumBytesPerRead = maximumBytesPerRead;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        _inner.Read(buffer, offset, Math.Min(count, _maximumBytesPerRead));

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken) =>
        _inner.ReadAsync(
            buffer[..Math.Min(buffer.Length, _maximumBytesPerRead)],
            cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
