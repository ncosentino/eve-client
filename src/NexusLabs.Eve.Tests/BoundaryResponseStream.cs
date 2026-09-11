namespace NexusLabs.Eve.Tests;

internal sealed class BoundaryResponseStream : Stream
{
    private readonly TaskCompletionSource _disposeCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> _endOfStream =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly byte[] _payload;
    private readonly TaskCompletionSource _readBlocked =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _requestCancellation =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _traceCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationToken _testCancellationToken;
    private CancellationTokenRegistration _requestCancellationRegistration;
    private bool _disposed;
    private int _disposeStartedState;
    private int _offset;
    private int _requestAttached;

    internal BoundaryResponseStream(
        byte[] payload,
        CancellationToken testCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _payload = payload;
        _testCancellationToken = testCancellationToken;
    }

    internal Task DisposeCompleted => _disposeCompleted.Task;

    internal Task DisposeStarted => _disposeStarted.Task;

    internal bool IsDisposed => _disposed;

    internal Task ReadBlocked => _readBlocked.Task;

    internal Task RequestCancellation => _requestCancellation.Task;

    internal bool TraceIsOpen => !_traceCompleted.Task.IsCompleted;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    internal void AttachRequestCancellation(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _requestAttached, 1) != 0)
        {
            throw new InvalidOperationException(
                "The boundary stream request cancellation was already attached.");
        }

        _requestCancellationRegistration = cancellationToken.Register(
            static state => ((BoundaryResponseStream)state!).SignalRequestCancellation(),
            this);
    }

    internal void CompleteTrace()
    {
        _traceCompleted.TrySetResult();
        _endOfStream.TrySetResult(0);
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
        if (_offset < _payload.Length)
        {
            int count = Math.Min(buffer.Length, _payload.Length - _offset);
            _payload.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        _readBlocked.TrySetResult();
        return await _endOfStream.Task.WaitAsync(cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStartedState, 1) != 0)
        {
            await _disposeCompleted.Task.WaitAsync(_testCancellationToken);
            return;
        }

        _disposeStarted.TrySetResult();
        await Task.WhenAny(_requestCancellation.Task, _traceCompleted.Task)
            .WaitAsync(_testCancellationToken);
        Dispose(true);
        GC.SuppressFinalize(this);
        _disposeCompleted.TrySetResult();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _requestCancellationRegistration.Dispose();
            _endOfStream.TrySetResult(0);
            _disposed = true;
        }

        base.Dispose(disposing);
    }

    private void SignalRequestCancellation()
    {
        _requestCancellation.TrySetResult();
    }
}
