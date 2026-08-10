using Concentus;
using Concentus.Oggfile;
using NAudio.Wave;

namespace Buddy.Audio.Windows;

internal sealed class OpusOggPlaybackSource : ISeekablePlaybackSource
{
    private const int SampleRate = 48_000;
    private const int Channels = 1;

    private readonly string _path;
    private FileStream _stream;
    private OpusOggReadStream _reader;
    private short[] _packet = [];
    private int _packetOffset;
    private long _samplePosition;
    private bool _atEnd;
    private bool _disposed;

    internal OpusOggPlaybackSource(string path)
    {
        _path = Path.GetFullPath(path);
        (_stream, _reader) = OpenReader(_path);
        WaveFormat = new WaveFormat(SampleRate, 16, Channels);
        Duration = _reader.TotalTime;
    }

    public WaveFormat WaveFormat { get; }

    public TimeSpan Duration { get; }

    public TimeSpan Position
    {
        get
        {
            if (_atEnd)
            {
                return Duration;
            }

            TimeSpan decoded = TimeSpan.FromSeconds(
                _samplePosition / (double)SampleRate);
            return decoded > Duration ? Duration : decoded;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
        {
            throw new ArgumentException("The requested range is outside the output buffer.");
        }

        int writableBytes = count - (count % sizeof(short));
        if (_atEnd)
        {
            return 0;
        }

        int written = 0;
        while (written < writableBytes)
        {
            if (_packetOffset >= _packet.Length)
            {
                if (!_reader.HasNextPacket)
                {
                    _atEnd = true;
                    break;
                }

                _packet = _reader.DecodeNextPacket() ?? [];
                _packetOffset = 0;
                if (_packet.Length == 0)
                {
                    continue;
                }
            }

            int samplesToCopy = Math.Min(
                _packet.Length - _packetOffset,
                (writableBytes - written) / sizeof(short));
            Buffer.BlockCopy(
                _packet,
                _packetOffset * sizeof(short),
                buffer,
                offset + written,
                samplesToCopy * sizeof(short));
            _packetOffset += samplesToCopy;
            _samplePosition += samplesToCopy;
            written += samplesToCopy * sizeof(short);
        }

        return written;
    }

    public void Seek(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TimeSpan target = position < TimeSpan.Zero
            ? TimeSpan.Zero
            : position > Duration
                ? Duration
                : position;
        _reader.Close();
        _stream.Dispose();
        (_stream, _reader) = OpenReader(_path);
        _atEnd = target == Duration;
        _packet = [];
        _packetOffset = 0;
        _samplePosition = 0;
        if (target > TimeSpan.Zero && !_atEnd)
        {
            SeekByDecoding(target);
        }
        else if (_atEnd)
        {
            _samplePosition = (long)Math.Round(
                Duration.TotalSeconds * SampleRate);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reader.Close();
        _stream.Dispose();
    }

    private void SeekByDecoding(TimeSpan target)
    {
        long targetSample = Math.Max(
            0,
            (long)Math.Round(target.TotalSeconds * SampleRate));
        while (_samplePosition < targetSample && _reader.HasNextPacket)
        {
            short[] decoded = _reader.DecodeNextPacket() ?? [];
            if (decoded.Length == 0)
            {
                continue;
            }

            long remaining = targetSample - _samplePosition;
            if (decoded.Length <= remaining)
            {
                _samplePosition += decoded.Length;
                continue;
            }

            _packet = decoded;
            _packetOffset = checked((int)remaining);
            _samplePosition = targetSample;
            return;
        }

        if (_samplePosition < targetSample)
        {
            _atEnd = true;
            _samplePosition = (long)Math.Round(
                Duration.TotalSeconds * SampleRate);
        }
    }

    private static (FileStream Stream, OpusOggReadStream Reader) OpenReader(string path)
    {
        FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.RandomAccess);
        try
        {
            IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(
                SampleRate,
                Channels,
                TextWriter.Null);
            return (stream, new OpusOggReadStream(decoder, stream));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
