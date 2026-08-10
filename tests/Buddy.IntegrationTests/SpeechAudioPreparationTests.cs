using System.Buffers.Binary;
using Buddy.Audio.Windows;
using Buddy.Core.Abstractions;

namespace Buddy.IntegrationTests;

public sealed class SpeechAudioPreparationTests
{
    [Fact]
    public async Task DurableStereoCaptureChunksBecomeMonoSixteenKilohertzWave()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "buddy-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const int sampleRate = 48_000;
            const int channels = 2;
            const int frameCount = sampleRate / 2;
            byte[] raw = CreateStereoTone(sampleRate, frameCount);
            int split = raw.Length / 2;
            string firstChunk = Path.Combine(root, "chunk-000000.pcm");
            string secondChunk = Path.Combine(root, "chunk-000001.pcm");
            await File.WriteAllBytesAsync(firstChunk, raw.AsMemory(0, split).ToArray());
            await File.WriteAllBytesAsync(secondChunk, raw.AsMemory(split).ToArray());
            DateTimeOffset now = DateTimeOffset.UtcNow;
            AudioCaptureResult capture = new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                now,
                now.AddSeconds(frameCount / (double)sampleRate),
                "synthetic-device",
                "Synthetic stereo microphone",
                sampleRate,
                16,
                channels,
                AudioSampleEncoding.Pcm,
                [firstChunk, secondChunk],
                raw.Length);
            string destination = Path.Combine(root, "dialog-live.wav");
            SpeechAudioPreparationService preparation = new();

            PreparedSpeechAudio result = await preparation.CreateSpeechWaveAsync(
                capture,
                destination);

            Assert.Equal(destination, result.Path);
            Assert.Equal(16_000, result.SampleRate);
            Assert.Equal(1, result.Channels);
            Assert.InRange(
                result.Duration,
                TimeSpan.FromMilliseconds(490),
                TimeSpan.FromMilliseconds(510));
            Assert.InRange(result.ByteLength, 15_500, 16_500);
            Assert.True(File.Exists(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] CreateStereoTone(int sampleRate, int frameCount)
    {
        const int channels = 2;
        byte[] bytes = new byte[frameCount * channels * sizeof(short)];
        for (int frame = 0; frame < frameCount; frame++)
        {
            double phase = 2 * Math.PI * 220 * frame / sampleRate;
            short left = (short)(Math.Sin(phase) * short.MaxValue * 0.2);
            short right = (short)(Math.Sin(phase + 0.3) * short.MaxValue * 0.2);
            int offset = frame * channels * sizeof(short);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), left);
            BinaryPrimitives.WriteInt16LittleEndian(
                bytes.AsSpan(offset + sizeof(short)),
                right);
        }

        return bytes;
    }
}
