using System.Text;

using Microsoft.Extensions.Time.Testing;

namespace NexusLabs.Eve.Tests;

public sealed class EveNdjsonLineReaderTests
{
    private static readonly TimeSpan ReadIdleTimeout = TimeSpan.FromSeconds(15);

    [Test]
    public async Task ReadLineAsync_AllowsExactFragmentedUtf8ByteLimit(
        CancellationToken cancellationToken)
    {
        const string line =
            "{\"type\":\"message.appended\",\"data\":{\"messageDelta\":\"\ud83c\udf0d\"}}";
        int maximumEventBytes = Encoding.UTF8.GetByteCount(line);
        byte[] content = Encoding.UTF8.GetBytes($"{line}\r\n");
        using FragmentedReadStream stream = new(content, 1);
        using EveNdjsonLineReader reader = new(
            stream,
            maximumEventBytes,
            TimeProvider.System,
            ReadIdleTimeout);

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
        using EveNdjsonLineReader reader = new(
            stream,
            maximumEventBytes,
            TimeProvider.System,
            ReadIdleTimeout);
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
        using EveNdjsonLineReader reader = new(
            stream,
            null,
            TimeProvider.System,
            ReadIdleTimeout);

        string? blank = await reader.ReadLineAsync(cancellationToken);
        string? trailing = await reader.ReadLineAsync(cancellationToken);
        string? end = await reader.ReadLineAsync(cancellationToken);

        await Assert.That(blank).IsEqualTo(string.Empty);
        await Assert.That(trailing).IsEqualTo(line);
        await Assert.That(end).IsNull();
    }

    [Test]
    public async Task ReadLineAsync_CancelsAnIdleUnderlyingRead(
        CancellationToken cancellationToken)
    {
        FakeTimeProvider timeProvider = new(
            new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero));
        using IdleAfterPrefixStream stream = new(Encoding.UTF8.GetBytes("partial"));
        using EveNdjsonLineReader reader = new(
            stream,
            null,
            timeProvider,
            ReadIdleTimeout);

        Task<string?> read = reader.ReadLineAsync(cancellationToken).AsTask();
        await stream.IdleReadStarted.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken);

        timeProvider.Advance(ReadIdleTimeout);

        await Assert.That(async () => await read.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken))
            .Throws<OperationCanceledException>();
        await Assert.That(cancellationToken.IsCancellationRequested)
            .IsFalse()
            .Because("The read-idle deadline must not cancel the caller's token.");
    }
}
