using System.Buffers;
using System.Buffers.Binary;
using Buddy.Core.Abstractions;
using NAudio.Wave;

namespace Buddy.Audio.Windows;

internal sealed class RawCaptureSampleProvider : ISampleProvider, IDisposable
{
    private readonly IReadOnlyList<string> _paths;
    private readonly AudioSampleEncoding _encoding;
    private readonly int _bytesPerSample;
    private int _pathIndex;
    private FileStream? _current;
    private bool _disposed;

    internal RawCaptureSampleProvider(AudioCaptureResult capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.SampleRate <= 0 || capture.Channels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capture),
                "Capture sample rate and channel count must be positive.");
        }

        if (capture.BitsPerSample is not (16 or 24 or 32))
        {
            throw new NotSupportedException(
                $"Raw {capture.BitsPerSample}-bit samples are not supported.");
        }

        _paths = capture.ChunkPaths;
        _encoding = capture.Encoding;
        _bytesPerSample = capture.BitsPerSample / 8;
        SourceBitsPerSample = capture.BitsPerSample;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
            capture.SampleRate,
            capture.Channels);
    }

    public WaveFormat WaveFormat { get; }

    private int SourceBitsPerSample { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (offset > buffer.Length - count)
        {
            throw new ArgumentException("The requested range is outside the output buffer.");
        }

        int requestedBytes = checked(count * _bytesPerSample);
        byte[] rented = ArrayPool<byte>.Shared.Rent(requestedBytes);
        try
        {
            int bytesRead = ReadBytes(rented.AsSpan(0, requestedBytes));
            int sampleCount = bytesRead / _bytesPerSample;
            Decode(rented.AsSpan(0, sampleCount * _bytesPerSample), buffer.AsSpan(offset, sampleCount));
            return sampleCount;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _current?.Dispose();
        _current = null;
    }

    private int ReadBytes(Span<byte> destination)
    {
        int total = 0;
        while (total < destination.Length)
        {
            if (_current is null && !OpenNextFile())
            {
                break;
            }

            int read = _current!.Read(destination[total..]);
            if (read > 0)
            {
                total += read;
                continue;
            }

            _current.Dispose();
            _current = null;
        }

        return total - (total % _bytesPerSample);
    }

    private bool OpenNextFile()
    {
        while (_pathIndex < _paths.Count)
        {
            string path = _paths[_pathIndex++];
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("A capture chunk is missing.", path);
            }

            _current = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            return true;
        }

        return false;
    }

    private void Decode(ReadOnlySpan<byte> source, Span<float> destination)
    {
        if (_encoding == AudioSampleEncoding.IeeeFloat)
        {
            if (SourceBitsPerSample != 32)
            {
                throw new NotSupportedException("IEEE float capture must use 32-bit samples.");
            }

            for (int index = 0; index < destination.Length; index++)
            {
                int bits = BinaryPrimitives.ReadInt32LittleEndian(source[(index * 4)..]);
                float sample = BitConverter.Int32BitsToSingle(bits);
                destination[index] = float.IsFinite(sample)
                    ? Math.Clamp(sample, -1, 1)
                    : 0;
            }

            return;
        }

        switch (SourceBitsPerSample)
        {
            case 16:
                DecodePcm16(source, destination);
                break;
            case 24:
                DecodePcm24(source, destination);
                break;
            case 32:
                DecodePcm32(source, destination);
                break;
            default:
                throw new NotSupportedException(
                    $"PCM {SourceBitsPerSample}-bit samples are not supported.");
        }
    }

    private static void DecodePcm16(ReadOnlySpan<byte> source, Span<float> destination)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            short sample = BinaryPrimitives.ReadInt16LittleEndian(source[(index * 2)..]);
            destination[index] = sample / 32_768f;
        }
    }

    private static void DecodePcm24(ReadOnlySpan<byte> source, Span<float> destination)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            int byteOffset = index * 3;
            int sample = source[byteOffset]
                | (source[byteOffset + 1] << 8)
                | (source[byteOffset + 2] << 16);
            if ((sample & 0x0080_0000) != 0)
            {
                sample |= unchecked((int)0xFF00_0000);
            }

            destination[index] = sample / 8_388_608f;
        }
    }

    private static void DecodePcm32(ReadOnlySpan<byte> source, Span<float> destination)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            int sample = BinaryPrimitives.ReadInt32LittleEndian(source[(index * 4)..]);
            destination[index] = sample / 2_147_483_648f;
        }
    }
}
