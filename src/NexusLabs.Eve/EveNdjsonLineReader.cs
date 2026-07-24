using System.Buffers;
using System.Text;

namespace NexusLabs.Eve;

internal sealed class EveNdjsonLineReader : IDisposable
{
    private const int ReadBufferSize = 4096;
    private const int InitialEventBufferSize = 256;
    private readonly int? _maximumEventBytes;
    private readonly byte[] _readBuffer;
    private readonly Stream _stream;
    private bool _disposed;
    private int _readBufferCount;
    private int _readBufferOffset;

    internal EveNdjsonLineReader(Stream stream, int? maximumEventBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _maximumEventBytes = maximumEventBytes;
        _readBuffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
    }

    internal async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int initialCapacity = Math.Min(
            _maximumEventBytes ?? InitialEventBufferSize,
            InitialEventBufferSize);
        ArrayBufferWriter<byte> eventBuffer = new(initialCapacity);
        bool observedByte = false;
        bool pendingCarriageReturn = false;

        while (true)
        {
            if (_readBufferOffset == _readBufferCount)
            {
                _readBufferCount = await _stream.ReadAsync(
                    _readBuffer.AsMemory(),
                    cancellationToken);
                _readBufferOffset = 0;
                if (_readBufferCount == 0)
                {
                    return observedByte
                        ? Encoding.UTF8.GetString(eventBuffer.WrittenSpan)
                        : null;
                }
            }

            byte value = _readBuffer[_readBufferOffset++];
            observedByte = true;

            if (pendingCarriageReturn)
            {
                if (value == (byte)'\n')
                {
                    return Encoding.UTF8.GetString(eventBuffer.WrittenSpan);
                }

                _readBufferOffset--;
                return Encoding.UTF8.GetString(eventBuffer.WrittenSpan);
            }

            if (value == (byte)'\r')
            {
                pendingCarriageReturn = true;
                continue;
            }

            if (value == (byte)'\n')
            {
                return Encoding.UTF8.GetString(eventBuffer.WrittenSpan);
            }

            Append(eventBuffer, value);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ArrayPool<byte>.Shared.Return(_readBuffer, clearArray: true);
        _disposed = true;
    }

    private void Append(ArrayBufferWriter<byte> eventBuffer, byte value)
    {
        int observedBytes = eventBuffer.WrittenCount + 1;
        if (_maximumEventBytes is int maximumEventBytes
            && observedBytes > maximumEventBytes)
        {
            throw new EveProtocolException(
                $"An eve stream event exceeded the configured maximum of "
                + $"{maximumEventBytes} UTF-8 bytes; observed at least {observedBytes} bytes.");
        }

        Span<byte> destination = eventBuffer.GetSpan(1);
        destination[0] = value;
        eventBuffer.Advance(1);
    }
}
