using System.Net;
using System.Text;

namespace NexusLabs.Eve.CompatibilityProbe;

/// <summary>
/// Maps the compact named-agent public route onto the standalone fixture's root-agent route.
/// </summary>
internal sealed class CompactNamedAgentRouteHandler : DelegatingHandler
{
    private const string CompactRoutePrefix = "/eve/support/v1";
    private const string RootRoutePrefix = "/eve/v1";
    private readonly Lock _gate = new();
    private readonly List<string> _requestPaths = [];
    private int _readinessRefusalCount;

    internal CompactNamedAgentRouteHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    internal int ReadinessRefusalCount => Volatile.Read(ref _readinessRefusalCount);

    internal IReadOnlyList<string> RequestPaths
    {
        get
        {
            lock (_gate)
            {
                return _requestPaths.ToArray();
            }
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Uri requestUri = request.RequestUri
            ?? throw new InvalidOperationException("The compact-route request had no URI.");
        string path = requestUri.AbsolutePath;
        lock (_gate)
        {
            _requestPaths.Add(path);
        }

        if (!string.Equals(path, CompactRoutePrefix, StringComparison.Ordinal)
            && !path.StartsWith($"{CompactRoutePrefix}/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The client did not target the compact named-agent route: {path}.");
        }

        if (request.Method == HttpMethod.Post
            && path.StartsWith($"{CompactRoutePrefix}/session/", StringComparison.Ordinal)
            && Interlocked.CompareExchange(ref _readinessRefusalCount, 1, 0) == 0)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    """
                    {
                      "code": "session_not_ready",
                      "error": "The prewarmed session is not ready yet.",
                      "ok": false
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            });
        }

        UriBuilder rewritten = new(requestUri)
        {
            Path = $"{RootRoutePrefix}{path[CompactRoutePrefix.Length..]}",
        };
        request.RequestUri = rewritten.Uri;
        return base.SendAsync(request, cancellationToken);
    }
}
