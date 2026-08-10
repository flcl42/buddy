using System.Buffers.Binary;
using System.Security.Cryptography;
using Buddy.Audio.Windows;
using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using Concentus;
using Concentus.Oggfile;

namespace Buddy.IntegrationTests;

public sealed class OggOpusArchiveTests
{
    [Fact]
    public async Task OriginalArchiveRoundTripsSyntheticSpeechFormatAudio()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "buddy-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const int sampleRate = 48_000;
            const int sampleCount = 60_000;
            string chunkPath = Path.Combine(root, "000000.pcm");
            await WriteToneAsync(chunkPath, sampleRate, sampleCount);

            Guid recordingId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            AudioCaptureResult capture = new(
                Guid.NewGuid(),
                recordingId,
                now,
                now.AddSeconds(sampleCount / (double)sampleRate),
                "synthetic-device",
                "Synthetic microphone",
                sampleRate,
                16,
                1,
                AudioSampleEncoding.Pcm,
                [chunkPath],
                sampleCount * sizeof(short));
            string destination = Path.Combine(root, "original.opus");
            OggOpusAudioArchiveService archives = new();

            AudioArtifact artifact = await archives.CreateOriginalArchiveAsync(
                capture,
                destination,
                now);

            Assert.Equal(recordingId, artifact.RecordingId);
            Assert.Equal(AudioArtifactKind.Original, artifact.Kind);
            Assert.Equal(AudioContainer.OggOpus, artifact.Container);
            Assert.Equal(sampleRate, artifact.SampleRate);
            Assert.Equal(1, artifact.Channels);
            Assert.Equal(TimeSpan.FromSeconds(1.25), artifact.Duration);
            Assert.True(artifact.ByteLength > 0);
            Assert.Equal(await HashAsync(destination), artifact.Sha256);
            Assert.True(DecodeSampleCount(destination) >= sampleCount);

            using OpusOggPlaybackSource playback = new(destination);
            Assert.InRange(
                playback.Duration,
                TimeSpan.FromSeconds(1.2),
                TimeSpan.FromSeconds(1.35));
            byte[] playbackBuffer = new byte[8_192];
            long decodedBytes = 0;
            int read;
            while ((read = playback.Read(playbackBuffer, 0, playbackBuffer.Length)) > 0)
            {
                decodedBytes += read;
            }

            Assert.True(decodedBytes >= sampleCount * sizeof(short));
            playback.Seek(TimeSpan.Zero);
            Assert.True(playback.Read(playbackBuffer, 0, playbackBuffer.Length) > 0);
            playback.Seek(TimeSpan.FromMilliseconds(500));
            Assert.InRange(
                playback.Position,
                TimeSpan.FromMilliseconds(495),
                TimeSpan.FromMilliseconds(505));
            Assert.True(playback.Read(playbackBuffer, 0, playbackBuffer.Length) > 0);
            Assert.InRange(
                playback.Position,
                TimeSpan.FromMilliseconds(550),
                TimeSpan.FromMilliseconds(650));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CompactArchiveCollapsesGapsAndPreparesWhisperWave()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "buddy-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const int sampleRate = 48_000;
            const int sampleCount = sampleRate * 3;
            string chunkPath = Path.Combine(root, "000000.pcm");
            await WriteToneAsync(chunkPath, sampleRate, sampleCount);

            Guid recordingId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            AudioCaptureResult capture = new(
                Guid.NewGuid(),
                recordingId,
                now,
                now.AddSeconds(3),
                "synthetic-device",
                "Synthetic microphone",
                sampleRate,
                16,
                1,
                AudioSampleEncoding.Pcm,
                [chunkPath],
                sampleCount * sizeof(short));
            OggOpusAudioArchiveService archives = new();
            string originalPath = Path.Combine(root, "original.opus");
            await archives.CreateOriginalArchiveAsync(capture, originalPath, now);

            SpeechSegment[] segments =
            [
                new(
                    recordingId,
                    0,
                    TimeSpan.FromSeconds(0.5),
                    TimeSpan.FromSeconds(1),
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(0.5),
                    0.9f),
                new(
                    recordingId,
                    1,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(2.5),
                    TimeSpan.FromSeconds(0.7),
                    TimeSpan.FromSeconds(1.2),
                    0.8f),
            ];
            string compactPath = Path.Combine(root, "compact.opus");

            AudioArtifact compact = await archives.CreateCompactArchiveAsync(
                recordingId,
                originalPath,
                compactPath,
                segments,
                now);

            Assert.Equal(AudioArtifactKind.Compact, compact.Kind);
            Assert.InRange(
                compact.Duration,
                TimeSpan.FromSeconds(1.19),
                TimeSpan.FromSeconds(1.21));
            Assert.InRange(
                DecodeSampleCount(compactPath),
                (long)(sampleRate * 1.19),
                (long)(sampleRate * 1.22));

            SpeechAudioPreparationService preparation = new();
            string wavePath = Path.Combine(root, "speech.wav");
            PreparedSpeechAudio prepared = await preparation.CreateSpeechWaveAsync(
                compactPath,
                wavePath);

            Assert.Equal(16_000, prepared.SampleRate);
            Assert.Equal(1, prepared.Channels);
            Assert.True(prepared.ByteLength > 0);
            Assert.InRange(
                prepared.Duration,
                TimeSpan.FromSeconds(1.19),
                TimeSpan.FromSeconds(1.22));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WriteToneAsync(
        string path,
        int sampleRate,
        int sampleCount)
    {
        byte[] bytes = new byte[sampleCount * sizeof(short)];
        for (int index = 0; index < sampleCount; index++)
        {
            double phase = 2 * Math.PI * 220 * index / sampleRate;
            short sample = (short)(Math.Sin(phase) * short.MaxValue * 0.2);
            BinaryPrimitives.WriteInt16LittleEndian(
                bytes.AsSpan(index * sizeof(short)),
                sample);
        }

        await File.WriteAllBytesAsync(path, bytes);
    }

    private static long DecodeSampleCount(string path)
    {
        using FileStream stream = File.OpenRead(path);
        IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(48_000, 1, Console.Error);
        OpusOggReadStream reader = new(decoder, stream);
        long sampleCount = 0;
        try
        {
            while (reader.HasNextPacket)
            {
                short[]? packet = reader.DecodeNextPacket();
                if (packet is null)
                {
                    break;
                }

                sampleCount += packet.Length;
            }
        }
        finally
        {
            reader.Close();
        }

        return sampleCount;
    }

    private static async Task<string> HashAsync(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }
}
