using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using NAudio.Wave;

namespace Buddy.Audio.Windows;

public sealed class AudioWaveformService : IAudioWaveformService
{
    public const string SchemaVersion = "buddy.waveform.v1";

    public Task<AudioWaveform> CreateAsync(
        Guid artifactId,
        string path,
        int sampleCount = AudioWaveform.DefaultSampleCount,
        CancellationToken cancellationToken = default)
    {
        if (artifactId == Guid.Empty)
        {
            throw new ArgumentException(
                "An audio artifact identifier is required.",
                nameof(artifactId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (sampleCount is < 16 or > 512)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleCount),
                "A waveform must contain between 16 and 512 samples.");
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The audio file for waveform analysis was not found.",
                fullPath);
        }

        return Task.Run(
            () => CreateCore(
                artifactId,
                fullPath,
                sampleCount,
                cancellationToken),
            cancellationToken);
    }

    private static AudioWaveform CreateCore(
        Guid artifactId,
        string path,
        int sampleCount,
        CancellationToken cancellationToken)
    {
        using ISeekablePlaybackSource source = string.Equals(
                Path.GetExtension(path),
                ".wav",
                StringComparison.OrdinalIgnoreCase)
            ? new WavePlaybackSource(path)
            : new OpusOggPlaybackSource(path);
        ISampleProvider samples = source.ToSampleProvider();
        int channels = samples.WaveFormat.Channels;
        int sampleRate = samples.WaveFormat.SampleRate;
        long expectedFrames = Math.Max(
            1,
            (long)Math.Ceiling(source.Duration.TotalSeconds * sampleRate));
        double[] squareSums = new double[sampleCount];
        long[] frameCounts = new long[sampleCount];
        float[] buffer = new float[4096 * channels];
        long frameIndex = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = samples.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            int framesRead = read / channels;
            for (int frame = 0; frame < framesRead; frame++)
            {
                float peak = 0;
                int sampleOffset = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    float value = Math.Abs(buffer[sampleOffset + channel]);
                    if (float.IsFinite(value))
                    {
                        peak = Math.Max(peak, Math.Min(1, value));
                    }
                }

                int bin = (int)Math.Min(
                    sampleCount - 1L,
                    frameIndex * sampleCount / expectedFrames);
                squareSums[bin] += peak * peak;
                frameCounts[bin]++;
                frameIndex++;
            }
        }

        double[] levels = new double[sampleCount];
        for (int index = 0; index < sampleCount; index++)
        {
            levels[index] = frameCounts[index] == 0
                ? 0
                : Math.Sqrt(squareSums[index] / frameCounts[index]);
        }

        double reference = levels.Max();
        byte[] peaks = new byte[sampleCount];
        if (reference > 0.000_001)
        {
            for (int index = 0; index < levels.Length; index++)
            {
                double normalized = Math.Clamp(levels[index] / reference, 0, 1);
                double visuallyCompressed = Math.Sqrt(normalized);
                peaks[index] = (byte)Math.Round(
                    visuallyCompressed * byte.MaxValue,
                    MidpointRounding.AwayFromZero);
            }
        }

        return new AudioWaveform(
            artifactId,
            source.Duration,
            peaks,
            DateTimeOffset.Now,
            SchemaVersion);
    }
}
