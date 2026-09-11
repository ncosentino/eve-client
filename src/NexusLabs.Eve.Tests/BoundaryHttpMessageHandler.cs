using System.Net;
using System.Text;

namespace NexusLabs.Eve.Tests;

internal sealed class BoundaryHttpMessageHandler : HttpMessageHandler
{
    private BoundaryStreamContent? _streamContent;
    private bool _disposed;
    private int _requestCount;

    internal BoundaryHttpMessageHandler(
        CancellationToken testCancellationToken,
        params string[] events)
    {
        ArgumentNullException.ThrowIfNull(events);
        Stream = new BoundaryResponseStream(Encoding.UTF8.GetBytes(
            $"{string.Join('\n', events)}\n"),
            testCancellationToken);
    }

    internal bool IsDisposed => _disposed;

    internal int RequestCount => _requestCount;

    internal BoundaryResponseStream Stream { get; }

    internal bool StreamContentIsDisposed => _streamContent?.IsDisposed == true;

    internal void CompleteTrace()
    {
        Stream.CompleteTrace();
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        int requestNumber = Interlocked.Increment(ref _requestCount);
        if (requestNumber == 1 && request.Method == HttpMethod.Post)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(
                    """{"ok":true,"sessionId":"session_1"}""",
                    Encoding.UTF8,
                    "application/json"),
            });
        }

        if (requestNumber == 2 && request.Method == HttpMethod.Get)
        {
            Stream.AttachRequestCancellation(cancellationToken);
            _streamContent = new BoundaryStreamContent(Stream);
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = _streamContent,
            };
            response.Headers.TryAddWithoutValidation(
                EveProtocol.StreamVersionHeaderName,
                EveProtocol.MessageStreamVersion);
            return Task.FromResult(response);
        }

        throw new InvalidOperationException(
            $"Unexpected boundary HTTP request {requestNumber}: {request.Method}.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            Stream.CompleteTrace();
            _disposed = true;
        }

        base.Dispose(disposing);
    }

    private sealed class BoundaryStreamContent(Stream stream) : StreamContent(stream)
    {
        internal bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
            }

            base.Dispose(disposing);
        }
    }
}
