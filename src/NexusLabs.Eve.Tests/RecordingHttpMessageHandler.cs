namespace NexusLabs.Eve.Tests;

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly List<RecordedHttpCall> _calls = [];
    private readonly Queue<Func<RecordedHttpCall, CancellationToken, Task<HttpResponseMessage>>>
        _responders = new();

    internal IReadOnlyList<RecordedHttpCall> Calls => _calls;

    internal void Enqueue(
        Func<RecordedHttpCall, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);
        _responders.Enqueue(responder);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Dictionary<string, string> requestHeaders = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, IReadOnlyList<string>> requestHeaderValues =
            new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, IEnumerable<string> values) in request.Headers)
        {
            string[] recordedValues = values.ToArray();
            requestHeaders[name] = string.Join(",", recordedValues);
            requestHeaderValues[name] = recordedValues;
        }

        Dictionary<string, string> contentHeaders = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, IReadOnlyList<string>> contentHeaderValues =
            new(StringComparer.OrdinalIgnoreCase);
        if (request.Content is not null)
        {
            foreach ((string name, IEnumerable<string> values) in request.Content.Headers)
            {
                string[] recordedValues = values.ToArray();
                contentHeaders[name] = string.Join(",", recordedValues);
                contentHeaderValues[name] = recordedValues;
            }
        }

        Dictionary<string, string> headers = new(requestHeaders, StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in contentHeaders)
        {
            headers[name] = value;
        }

        RecordedHttpCall call = new(
            request.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            headers,
            requestHeaders,
            contentHeaders,
            requestHeaderValues,
            contentHeaderValues,
            body);
        _calls.Add(call);

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException("No HTTP response was queued for the request.");
        }

        return await _responders.Dequeue()(call, cancellationToken);
    }
}
