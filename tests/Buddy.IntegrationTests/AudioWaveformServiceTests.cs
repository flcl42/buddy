using Buddy.Audio.Windows;
using Buddy.Core.Domain;
using NAudio.Wave;

namespace Buddy.IntegrationTests;

public sealed class AudioWaveformServiceTests
{
    [Fact]
    public async Task WaveformPreservesQuietAndLoudSections()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "buddy-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const int sampleRate = 16_000;
            string path = Path.Combine(root, "levels.wav");
            using (WaveFileWriter writer = new(
                path,
                new WaveFormat(sampleRate, 16, 1)))
            {
                for (int sample = 0; sample < sampleRate * 2; sample++)
                {
                    float amplitude = sample < sampleRate ? 0.04f : 0.7f;
                    writer.WriteSample(
                        amplitude
                        * (float)Math.Sin(
                            2 * Math.PI * 220 * sample / sampleRate));
                }
            }

            AudioWaveformService service = new();
            AudioWaveform waveform = await service.CreateAsync(
                Guid.NewGuid(),
                path,
                sampleCount: 32);

            Assert.Equal(32, waveform.Peaks.Count);
            Assert.Equal(AudioWaveformService.SchemaVersion, waveform.SchemaVersion);
            Assert.InRange(
                waveform.Duration,
                TimeSpan.FromMilliseconds(1_990),
                TimeSpan.FromMilliseconds(2_010));
            Assert.True(
                waveform.Peaks.Take(16).Average(value => value)
                * 2
                < waveform.Peaks.Skip(16).Average(value => value));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
