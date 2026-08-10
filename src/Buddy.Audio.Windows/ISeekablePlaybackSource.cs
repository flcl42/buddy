using NAudio.Wave;

namespace Buddy.Audio.Windows;

internal interface ISeekablePlaybackSource : IWaveProvider, IDisposable
{
    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    void Seek(TimeSpan position);
}
