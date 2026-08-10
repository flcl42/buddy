using NAudio.Wave;

namespace Buddy.Audio.Windows;

internal sealed class WavePlaybackSource : ISeekablePlaybackSource
{
    private readonly WaveFileReader _reader;
    private bool _disposed;

    internal WavePlaybackSource(string path)
    {
        _reader = new WaveFileReader(Path.GetFullPath(path));
        if (_reader.TotalTime <= TimeSpan.Zero)
        {
            _reader.Dispose();
            throw new InvalidDataException("The WAV file does not contain playable audio.");
        }
    }

    public WaveFormat WaveFormat => _reader.WaveFormat;

    public TimeSpan Position => _reader.CurrentTime;

    public TimeSpan Duration => _reader.TotalTime;

    public int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _reader.Read(buffer, offset, count);
    }

    public void Seek(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _reader.CurrentTime = position < TimeSpan.Zero
            ? TimeSpan.Zero
            : position > Duration
                ? Duration
                : position;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reader.Dispose();
    }
}
