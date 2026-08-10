using System.Buffers.Binary;
using System.Security.Cryptography;
using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Buddy.Audio.Windows;

public sealed class OggOpusAudioArchiveService : IAudioArchiveService
{
    private const int ArchiveSampleRate = 48_000;
    private const int ArchiveChannels = 1;
    private const int SamplesPerRead = 4_800;

    public async Task<AudioArtifact> CreateOriginalArchiveAsync(
        AudioCaptureResult capture,
        string destinationPath,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (capture.RecordingId == Guid.Empty)
        {
            throw new ArgumentException("The capture must belong to a recording.", nameof(capture));
        }

        ValidateCaptureChunks(capture);

        string fullDestination = Path.GetFullPath(destinationPath);
        string? destinationDirectory = Path.GetDirectoryName(fullDestination);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("The archive needs a destination directory.", nameof(destinationPath));
        }

        Directory.CreateDirectory(destinationDirectory);
        string temporaryPath = fullDestination + $".{Guid.NewGuid():N}.tmp";
        long outputSamples;

        try
        {
            outputSamples = await EncodeAsync(capture, temporaryPath, cancellationToken)
                .ConfigureAwait(false);
            ValidateArchive(temporaryPath);
            File.Move(temporaryPath, fullDestination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        FileInfo file = new(fullDestination);
        string sha256 = await ComputeSha256Async(fullDestination, cancellationToken)
            .ConfigureAwait(false);
        return new AudioArtifact(
            Guid.NewGuid(),
            capture.RecordingId,
            AudioArtifactKind.Original,
            Path.GetFileName(fullDestination),
            AudioContainer.OggOpus,
            ArchiveSampleRate,
            ArchiveChannels,
            TimeSpan.FromSeconds(outputSamples / (double)ArchiveSampleRate),
            file.Length,
            sha256,
            $"Concentus {typeof(OpusCodecFactory).Assembly.GetName().Version}",
            createdAt);
    }

    public async Task<AudioArtifact> CreateCompactArchiveAsync(
        Guid recordingId,
        string sourcePath,
        string destinationPath,
        IReadOnlyList<SpeechSegment> segments,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        if (recordingId == Guid.Empty)
        {
            throw new ArgumentException(
                "The compact archive must belong to a recording.",
                nameof(recordingId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0)
        {
            throw new ArgumentException(
                "At least one speech segment is needed for a compact archive.",
                nameof(segments));
        }

        string fullSource = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSource))
        {
            throw new FileNotFoundException("The source audio archive is missing.", fullSource);
        }

        string fullDestination = Path.GetFullPath(destinationPath);
        if (string.Equals(fullSource, fullDestination, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The compact archive destination must differ from its source.",
                nameof(destinationPath));
        }

        string? destinationDirectory = Path.GetDirectoryName(fullDestination);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException(
                "The compact archive needs a destination directory.",
                nameof(destinationPath));
        }

        Directory.CreateDirectory(destinationDirectory);
        string temporaryPath = fullDestination + $".{Guid.NewGuid():N}.tmp";
        long outputSamples;

        try
        {
            outputSamples = await Task.Run(
                    () => EncodeCompact(
                        recordingId,
                        fullSource,
                        temporaryPath,
                        segments,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateArchive(temporaryPath);
            File.Move(temporaryPath, fullDestination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        FileInfo file = new(fullDestination);
        string sha256 = await ComputeSha256Async(fullDestination, cancellationToken)
            .ConfigureAwait(false);
        return new AudioArtifact(
            Guid.NewGuid(),
            recordingId,
            AudioArtifactKind.Compact,
            Path.GetFileName(fullDestination),
            AudioContainer.OggOpus,
            ArchiveSampleRate,
            ArchiveChannels,
            TimeSpan.FromSeconds(outputSamples / (double)ArchiveSampleRate),
            file.Length,
            sha256,
            $"Buddy pause compactor · Concentus "
                + $"{typeof(OpusCodecFactory).Assembly.GetName().Version}",
            createdAt);
    }

    private static async Task<long> EncodeAsync(
        AudioCaptureResult capture,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        await using FileStream output = new(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        using RawCaptureSampleProvider raw = new(capture);
        ISampleProvider samples = new MonoMixingSampleProvider(raw);
        if (samples.WaveFormat.SampleRate != ArchiveSampleRate)
        {
            samples = new WdlResamplingSampleProvider(samples, ArchiveSampleRate);
        }

        OpusOggWriteStream writer = CreateWriter(
            output,
            "Buddy local microphone archive");
        float[] buffer = new float[SamplesPerRead];
        long totalSamples = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = samples.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            writer.WriteSamples(buffer, 0, read);
            totalSamples += read;
        }

        if (totalSamples == 0)
        {
            throw new InvalidDataException("The microphone capture did not contain audio samples.");
        }

        writer.Finish();
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
        return totalSamples;
    }

    private static long EncodeCompact(
        Guid recordingId,
        string sourcePath,
        string temporaryPath,
        IReadOnlyList<SpeechSegment> segments,
        CancellationToken cancellationToken)
    {
        using OpusOggPlaybackSource source = new(sourcePath);
        SpeechSegment[] ordered = ValidateCompactSegments(
            recordingId,
            source.Duration,
            segments);
        using FileStream output = new(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.WriteThrough);
        OpusOggWriteStream writer = CreateWriter(
            output,
            "Buddy pause-compacted microphone archive");

        byte[] pcmBuffer = new byte[SamplesPerRead * sizeof(short)];
        float[] sampleBuffer = new float[SamplesPerRead];
        long sourceCursor = 0;
        long outputCursor = 0;
        const int fadeSamples = ArchiveSampleRate / 100;

        foreach (SpeechSegment segment in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long segmentStart = TimeToSamples(segment.OriginalStart);
            long segmentEnd = TimeToSamples(segment.OriginalEnd);
            SkipSamples(
                source,
                pcmBuffer,
                segmentStart - sourceCursor,
                cancellationToken);
            sourceCursor = segmentStart;

            long targetOutputStart = TimeToSamples(segment.CompactStart);
            WriteSilence(
                writer,
                sampleBuffer,
                targetOutputStart - outputCursor,
                cancellationToken);
            outputCursor = targetOutputStart;

            long segmentSamples = segmentEnd - segmentStart;
            long copied = 0;
            while (copied < segmentSamples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int wantedSamples = (int)Math.Min(
                    pcmBuffer.Length / sizeof(short),
                    segmentSamples - copied);
                int readBytes = source.Read(
                    pcmBuffer,
                    0,
                    wantedSamples * sizeof(short));
                if (readBytes == 0)
                {
                    throw new InvalidDataException(
                        "The source archive ended inside a detected speech segment.");
                }

                int readSamples = readBytes / sizeof(short);
                for (int index = 0; index < readSamples; index++)
                {
                    short pcm = BinaryPrimitives.ReadInt16LittleEndian(
                        pcmBuffer.AsSpan(index * sizeof(short), sizeof(short)));
                    long segmentOffset = copied + index;
                    float fadeIn = Math.Min(1, (segmentOffset + 1) / (float)fadeSamples);
                    float fadeOut = Math.Min(
                        1,
                        (segmentSamples - segmentOffset) / (float)fadeSamples);
                    float gain = Math.Min(fadeIn, fadeOut);
                    sampleBuffer[index] = pcm / 32768f * gain;
                }

                writer.WriteSamples(sampleBuffer, 0, readSamples);
                copied += readSamples;
                sourceCursor += readSamples;
                outputCursor += readSamples;
            }
        }

        if (outputCursor == 0)
        {
            throw new InvalidDataException("The speech segments did not contain audio.");
        }

        writer.Finish();
        output.Flush(flushToDisk: true);
        return outputCursor;
    }

    private static SpeechSegment[] ValidateCompactSegments(
        Guid recordingId,
        TimeSpan sourceDuration,
        IReadOnlyList<SpeechSegment> segments)
    {
        SpeechSegment[] ordered = segments
            .OrderBy(segment => segment.Sequence)
            .ToArray();
        long sourceSamples = TimeToSamples(sourceDuration);
        long previousOriginalEnd = 0;
        long previousCompactEnd = 0;

        for (int index = 0; index < ordered.Length; index++)
        {
            SpeechSegment segment = ordered[index];
            if (segment.RecordingId != recordingId)
            {
                throw new ArgumentException(
                    "Every compact segment must belong to the requested recording.",
                    nameof(segments));
            }

            long originalStart = TimeToSamples(segment.OriginalStart);
            long originalEnd = TimeToSamples(segment.OriginalEnd);
            long compactStart = TimeToSamples(segment.CompactStart);
            long compactEnd = TimeToSamples(segment.CompactEnd);
            if (segment.Sequence != index
                || originalStart < previousOriginalEnd
                || originalEnd <= originalStart
                || originalEnd > sourceSamples + 1
                || compactStart < previousCompactEnd
                || compactEnd <= compactStart
                || Math.Abs(
                    (originalEnd - originalStart)
                    - (compactEnd - compactStart)) > 1)
            {
                throw new ArgumentException(
                    "Compact speech segments are not a valid ordered timeline.",
                    nameof(segments));
            }

            previousOriginalEnd = originalEnd;
            previousCompactEnd = compactEnd;
        }

        return ordered;
    }

    private static void SkipSamples(
        OpusOggPlaybackSource source,
        byte[] buffer,
        long sampleCount,
        CancellationToken cancellationToken)
    {
        if (sampleCount < 0)
        {
            throw new InvalidDataException("Speech segments overlap in source time.");
        }

        long skipped = 0;
        while (skipped < sampleCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int wantedSamples = (int)Math.Min(
                buffer.Length / sizeof(short),
                sampleCount - skipped);
            int readBytes = source.Read(buffer, 0, wantedSamples * sizeof(short));
            if (readBytes == 0)
            {
                throw new InvalidDataException(
                    "The source archive ended before a detected speech segment.");
            }

            skipped += readBytes / sizeof(short);
        }
    }

    private static void WriteSilence(
        OpusOggWriteStream writer,
        float[] buffer,
        long sampleCount,
        CancellationToken cancellationToken)
    {
        if (sampleCount < 0)
        {
            throw new InvalidDataException(
                "A compact segment begins before the prior output segment ends.");
        }

        Array.Clear(buffer);
        long written = 0;
        while (written < sampleCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = (int)Math.Min(buffer.Length, sampleCount - written);
            writer.WriteSamples(buffer, 0, count);
            written += count;
        }
    }

    private static OpusOggWriteStream CreateWriter(Stream output, string comment)
    {
        IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(
            ArchiveSampleRate,
            ArchiveChannels,
            OpusApplication.OPUS_APPLICATION_AUDIO,
            TextWriter.Null);
        encoder.Bitrate = 32_000;
        encoder.Complexity = 10;
        encoder.UseVBR = true;
        encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;

        OpusTags tags = new()
        {
            Comment = comment,
        };
        tags.Fields["ENCODER"] = "Buddy Concentus";
        return new OpusOggWriteStream(
            encoder,
            output,
            tags,
            ArchiveSampleRate,
            resamplerQuality: 10,
            leaveOpen: true);
    }

    private static long TimeToSamples(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Audio time cannot be negative.");
        }

        return checked((long)Math.Round(
            value.TotalSeconds * ArchiveSampleRate,
            MidpointRounding.AwayFromZero));
    }

    private static void ValidateArchive(string path)
    {
        using FileStream input = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(
            ArchiveSampleRate,
            ArchiveChannels,
            TextWriter.Null);
        OpusOggReadStream reader = new(decoder, input);
        try
        {
            if (!reader.HasNextPacket)
            {
                throw new InvalidDataException("The generated Opus archive has no audio packets.");
            }

            short[] firstPacket = reader.DecodeNextPacket() ?? [];
            if (firstPacket.Length == 0)
            {
                throw new InvalidDataException("The generated Opus archive has an empty first packet.");
            }
        }
        finally
        {
            reader.Close();
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static void ValidateCaptureChunks(AudioCaptureResult capture)
    {
        int bytesPerSample = capture.BitsPerSample / 8;
        int blockAlign = checked(bytesPerSample * capture.Channels);
        if (bytesPerSample <= 0 || blockAlign <= 0)
        {
            throw new InvalidDataException("The capture audio format is invalid.");
        }

        long availableBytes = 0;
        foreach (string path in capture.ChunkPaths)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("A capture chunk is missing.", path);
            }

            long length = new FileInfo(path).Length;
            if (length % blockAlign != 0)
            {
                throw new InvalidDataException(
                    "A capture chunk ends in the middle of an audio frame.");
            }

            availableBytes = checked(availableBytes + length);
        }

        if (availableBytes != capture.TotalPcmBytes)
        {
            throw new InvalidDataException(
                "Capture chunk sizes do not match the journaled byte count.");
        }
    }
}
