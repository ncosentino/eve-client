namespace NexusLabs.Eve.Tests;

internal sealed class IdleAfterPrefixStream : Stream
{
    private readonly TaskCompletionSource<int> _idleRead =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _idleReadStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly byte[] _prefix;
    private bool _disposed;
    private int _offset;

    internal IdleAfterPrefixStream(byte[] prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        _prefix = prefix;
    }

    internal bool IsDisposed => _disposed;

    internal Task IdleReadStarted => _idleReadStarted.Task;

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
        throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_offset < _prefix.Length)
        {
            int count = Math.Min(buffer.Length, _prefix.Length - _offset);
            _prefix.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        _idleReadStarted.TrySetResult();
        return await _idleRead.Task.WaitAsync(cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _idleRead.TrySetResult(0);
            _disposed = true;
        }

        base.Dispose(disposing);
    }
}
