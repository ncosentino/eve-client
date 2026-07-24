namespace NexusLabs.Eve.Tests;

internal sealed record RecordedHttpCall(
    HttpMethod Method,
    string Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? Body);
