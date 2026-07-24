using System.Text;

namespace NexusLabs.Eve.Tests;

public sealed class EveNdjsonLineReaderTests
{
    [Test]
    public async Task ReadLineAsync_AllowsExactFragmentedUtf8ByteLimit(
        CancellationToken cancellationToken)
    {
        const string line =
            "{\"type\":\"message.appended\",\"data\":{\"messageDelta\":\"\ud83c\udf0d\"}}";
        int maximumEventBytes = Encoding.UTF8.GetByteCount(line);
        byte[] content = Encoding.UTF8.GetBytes($"{line}\r\n");
        using FragmentedReadStream stream = new(content, 1);
        using EveNdjsonLineReader reader = new(stream, maximumEventBytes);

        string? actual = await reader.ReadLineAsync(cancellationToken);
        string? trailing = await reader.ReadLineAsync(cancellationToken);

        await Assert.That(actual).IsEqualTo(line);
        await Assert.That(trailing).IsNull();
    }

    [Test]
    public async Task ReadLineAsync_RejectsOversizedEventWithoutEchoingPayload(
        CancellationToken cancellationToken)
    {
        const string payloadMarker = "payload-must-not-be-echoed";
        const string line = "{\"type\":\"message.appended\",\"data\":{\"messageDelta\":\""
            + payloadMarker
            + "\"}}";
        int maximumEventBytes = Encoding.UTF8.GetByteCount(line) - 1;
        byte[] content = Encoding.UTF8.GetBytes($"{line}\n");
        using FragmentedReadStream stream = new(content, 2);
        using EveNdjsonLineReader reader = new(stream, maximumEventBytes);
        EveProtocolException? exception = null;

        try
        {
            await reader.ReadLineAsync(cancellationToken);
        }
        catch (EveProtocolException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        string message = exception?.Message ?? string.Empty;
        await Assert.That(message.Contains(
            maximumEventBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains(payloadMarker, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ReadLineAsync_PreservesBlankLinesAndTrailingEvent(
        CancellationToken cancellationToken)
    {
        const string line = """{"type":"session.completed"}""";
        byte[] content = Encoding.UTF8.GetBytes($"\r\n{line}");
        using FragmentedReadStream stream = new(content, 3);
        using EveNdjsonLineReader reader = new(stream, null);

        string? blank = await reader.ReadLineAsync(cancellationToken);
        string? trailing = await reader.ReadLineAsync(cancellationToken);
        string? end = await reader.ReadLineAsync(cancellationToken);

        await Assert.That(blank).IsEqualTo(string.Empty);
        await Assert.That(trailing).IsEqualTo(line);
        await Assert.That(end).IsNull();
    }
}
