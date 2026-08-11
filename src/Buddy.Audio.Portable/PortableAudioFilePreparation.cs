using Buddy.Audio.Windows;
using NAudio.Wave;

namespace Buddy.Audio.Portable;

public sealed record PreparedPlaybackFile(
    string Path,
    TimeSpan Duration,
    bool IsTemporary);

public static class PortableAudioFilePreparation
{
    public static Task<PreparedPlaybackFile> PrepareWaveAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The recording audio file was not found.",
                fullPath);
        }

        return Task.Run(
            () => PrepareWave(fullPath, cancellationToken),
            cancellationToken);
    }

    public static Task<string> CreateTestToneAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.Combine(
                    CreateTemporaryDirectory(),
                    $"speaker-test-{Guid.NewGuid():N}.wav");
                CreateTestTone(path);
                return path;
            },
            cancellationToken);
    }

    public static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static PreparedPlaybackFile PrepareWave(
        string fullPath,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                Path.GetExtension(fullPath),
                ".wav",
                StringComparison.OrdinalIgnoreCase))
        {
            using WaveFileReader reader = new(fullPath);
            if (reader.TotalTime <= TimeSpan.Zero)
            {
                throw new InvalidDataException(
                    "The WAV file does not contain playable audio.");
            }

            return new PreparedPlaybackFile(fullPath, reader.TotalTime, false);
        }

        string destination = Path.Combine(
            CreateTemporaryDirectory(),
            $"playback-{Guid.NewGuid():N}.wav");
        try
        {
            using OpusOggPlaybackSource source = new(fullPath);
            using WaveFileWriter writer = new(destination, source.WaveFormat);
            byte[] buffer = new byte[64 * 1024];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = source.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                writer.Write(buffer, 0, read);
            }

            return new PreparedPlaybackFile(
                destination,
                source.Duration,
                true);
        }
        catch
        {
            TryDelete(destination);
            throw;
        }
    }

    private static void CreateTestTone(string path)
    {
        const int sampleRate = 48_000;
        WaveFormat format = new(sampleRate, 16, 1);
        using WaveFileWriter writer = new(path, format);
        int sampleCount = (int)(sampleRate * 0.7);
        for (int index = 0; index < sampleCount; index++)
        {
            double fade = Math.Min(1, index / (sampleRate * 0.04));
            fade = Math.Min(
                fade,
                (sampleCount - index) / (sampleRate * 0.04));
            short sample = (short)Math.Round(
                Math.Sin(2 * Math.PI * 660 * index / sampleRate)
                * short.MaxValue
                * 0.16
                * fade);
            writer.WriteByte((byte)(sample & 0xFF));
            writer.WriteByte((byte)((sample >> 8) & 0xFF));
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "Buddy", "playback");
        Directory.CreateDirectory(path);
        return path;
    }
}
