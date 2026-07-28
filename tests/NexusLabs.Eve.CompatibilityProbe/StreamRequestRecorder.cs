namespace NexusLabs.Eve.CompatibilityProbe;

/// <summary>
/// Records the stream requests the client issues and the durable tail index headers the real
/// Eve server returns for them.
/// </summary>
internal sealed class StreamRequestRecorder : DelegatingHandler
{
    private const string TailIndexHeader = "x-eve-stream-tail-index";
    private readonly List<RecordedStreamRequest> _streamRequests = [];

    internal StreamRequestRecorder(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    internal IReadOnlyList<RecordedStreamRequest> StreamRequests => _streamRequests;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        string uri = request.RequestUri?.ToString() ?? string.Empty;
        if (uri.Contains("/stream", StringComparison.Ordinal))
        {
            _streamRequests.Add(new RecordedStreamRequest(
                uri,
                response.Headers.TryGetValues(TailIndexHeader, out IEnumerable<string>? values)
                    ? values.FirstOrDefault()
                    : null));
        }

        return response;
    }
}
