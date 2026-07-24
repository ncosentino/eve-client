namespace NexusLabs.Eve.Tests;

internal sealed record RecordedHttpCall(
    HttpMethod Method,
    string Uri,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> RequestHeaders,
    IReadOnlyDictionary<string, string> ContentHeaders,
    IReadOnlyDictionary<string, IReadOnlyList<string>> RequestHeaderValues,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ContentHeaderValues,
    string? Body);
