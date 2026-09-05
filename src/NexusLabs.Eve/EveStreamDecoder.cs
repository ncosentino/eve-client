namespace NexusLabs.Eve;

internal sealed class EveStreamDecoder
{
    private readonly int _streamVersion;
    private readonly Dictionary<string, string> _messageSoFar = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _reasoningSoFar = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _toolInputOffsets = new(StringComparer.Ordinal);

    public EveStreamDecoder(int streamVersion)
    {
        if (streamVersion is < 21 or > 25)
        {
            throw new EveProtocolException(
                $"Unsupported eve stream protocol version '{streamVersion}'. Supported versions are 21 through 25.");
        }

        _streamVersion = streamVersion;
    }

    public int StreamVersion => _streamVersion;

    public EveStreamEvent Decode(string json) =>
        EveStreamEvent.Parse(json, _streamVersion, this);

    internal string? GetMessageSoFar(string turnId) =>
        _messageSoFar.TryGetValue(turnId, out string? value) ? value : null;

    internal void SetMessageSoFar(string turnId, string value) =>
        _messageSoFar[turnId] = value;

    internal string? GetReasoningSoFar(string turnId) =>
        _reasoningSoFar.TryGetValue(turnId, out string? value) ? value : null;

    internal void SetReasoningSoFar(string turnId, string value) =>
        _reasoningSoFar[turnId] = value;

    internal int? GetToolInputOffset(string callId) =>
        _toolInputOffsets.TryGetValue(callId, out int value) ? value : null;

    internal void SetToolInputOffset(string callId, int value) =>
        _toolInputOffsets[callId] = value;
}
