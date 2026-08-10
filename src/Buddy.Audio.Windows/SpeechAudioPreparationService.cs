using Buddy.Core.Abstractions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Buddy.Audio.Windows;

public sealed class SpeechAudioPreparationService : IAudioPreparationService
{
    private const int SpeechSampleRate = 16_000;
    private const int CopyBufferSize = 64 * 1024;

    public async Task<PreparedSpeechAudio> CreateSpeechWaveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string fullSource = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSource))
        {
            throw new FileNotFoundException("The source audio archive is missing.", fullSource);
        }

        string fullDestination = Path.GetFullPath(destinationPath);
        if (string.Equals(fullSource, fullDestination, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The speech WAV destination must differ from its source.",
                nameof(destinationPath));
        }

        string? destinationDirectory = Path.GetDirectoryName(fullDestination);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException(
                "The speech WAV needs a destination directory.",
                nameof(destinationPath));
        }

        Directory.CreateDirectory(destinationDirectory);
        string temporaryPath = fullDestination + $".{Guid.NewGuid():N}.tmp";

        try
        {
            await Task.Run(
                    () => ConvertToWave(
                        fullSource,
                        temporaryPath,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateWave(temporaryPath);
            File.Move(temporaryPath, fullDestination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        using WaveFileReader reader = new(fullDestination);
        return new PreparedSpeechAudio(
            fullDestination,
            reader.WaveFormat.SampleRate,
            reader.WaveFormat.Channels,
            reader.TotalTime,
            reader.Length);
    }

    public async Task<PreparedSpeechAudio> CreateSpeechWaveAsync(
        AudioCaptureResult capture,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (capture.ChunkPaths.Count == 0)
        {
            throw new ArgumentException(
                "At least one durable capture chunk is required.",
                nameof(capture));
        }

        string fullDestination = Path.GetFullPath(destinationPath);
        string? destinationDirectory = Path.GetDirectoryName(fullDestination);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException(
                "The speech WAV needs a destination directory.",
                nameof(destinationPath));
        }

        Directory.CreateDirectory(destinationDirectory);
        string temporaryPath = fullDestination + $".{Guid.NewGuid():N}.tmp";

        try
        {
            await Task.Run(
                    () => ConvertCaptureToWave(
                        capture,
                        temporaryPath,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateWave(temporaryPath);
            File.Move(temporaryPath, fullDestination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        using WaveFileReader reader = new(fullDestination);
        return new PreparedSpeechAudio(
            fullDestination,
            reader.WaveFormat.SampleRate,
            reader.WaveFormat.Channels,
            reader.TotalTime,
            reader.Length);
    }

    private static void ConvertToWave(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using OpusOggPlaybackSource source = new(sourcePath);
        ISampleProvider samples = new Pcm16BitToSampleProvider(source);
        if (samples.WaveFormat.SampleRate != SpeechSampleRate)
        {
            samples = new WdlResamplingSampleProvider(samples, SpeechSampleRate);
        }

        SampleToWaveProvider16 pcm16 = new(samples);
        using WaveFileWriter writer = new(destinationPath, pcm16.WaveFormat);
        byte[] buffer = new byte[CopyBufferSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = pcm16.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            writer.Write(buffer, 0, read);
        }

        writer.Flush();
    }

    private static void ConvertCaptureToWave(
        AudioCaptureResult capture,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using RawCaptureSampleProvider raw = new(capture);
        ISampleProvider samples = new MonoMixingSampleProvider(raw);
        if (samples.WaveFormat.SampleRate != SpeechSampleRate)
        {
            samples = new WdlResamplingSampleProvider(samples, SpeechSampleRate);
        }

        SampleToWaveProvider16 pcm16 = new(samples);
        using WaveFileWriter writer = new(destinationPath, pcm16.WaveFormat);
        byte[] buffer = new byte[CopyBufferSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = pcm16.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            writer.Write(buffer, 0, read);
        }

        writer.Flush();
    }

    private static void ValidateWave(string path)
    {
        using WaveFileReader reader = new(path);
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm
            || reader.WaveFormat.SampleRate != SpeechSampleRate
            || reader.WaveFormat.Channels != 1
            || reader.WaveFormat.BitsPerSample != 16
            || reader.Length == 0)
        {
            throw new InvalidDataException(
                "The prepared speech WAV is not mono 16 kHz PCM audio.");
        }
    }
}
