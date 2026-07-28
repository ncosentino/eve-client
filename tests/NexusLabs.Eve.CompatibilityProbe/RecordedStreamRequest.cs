namespace NexusLabs.Eve.CompatibilityProbe;

/// <summary>
/// One observed stream request and the durable tail index the server reported for it.
/// </summary>
internal sealed record RecordedStreamRequest(string Uri, string? TailIndex);
