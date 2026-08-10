using Buddy.Audio.Windows;
using NAudio.Wave;

namespace Buddy.IntegrationTests;

public sealed class OutputTestToneTests
{
    [Fact]
    public void SpeakerTestToneIsAudiblePcmWithShortBoundedDuration()
    {
        using WaveStream tone = NAudioPlaybackService.CreateOutputTestTone();

        Assert.Equal(48_000, tone.WaveFormat.SampleRate);
        Assert.Equal(16, tone.WaveFormat.BitsPerSample);
        Assert.Equal(1, tone.WaveFormat.Channels);
        Assert.InRange(tone.TotalTime, TimeSpan.FromSeconds(1.19), TimeSpan.FromSeconds(1.21));

        byte[] audio = new byte[tone.Length];
        int read = tone.Read(audio, 0, audio.Length);

        Assert.Equal(audio.Length, read);
        Assert.Contains(audio, value => value != 0);
        Assert.Equal(0, audio[0]);
        Assert.Equal(0, audio[1]);
    }
}
