using System.Collections.Concurrent;
using Buddy.Audio.Portable;
using Buddy.Core.Abstractions;
using Buddy.Core.Domain;

namespace Buddy.Core.Tests;

public sealed class FloatPcmCaptureSinkTests
{
    [Fact]
    public async Task StopAsync_WritesAlignedRecoverableChunksAndFinalJournal()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "Buddy.Tests",
            Guid.NewGuid().ToString("N"));
        RecordingKind kind = RecordingKind.Meeting;
        AudioCaptureOptions options = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            kind,
            directory,
            "test-device",
            TimeSpan.FromSeconds(1),
            PreferredSampleRate: 8_000,
            PreferredChannels: 1);
        InMemoryJournalStore journals = new();
        List<AudioCaptureChunk> completed = [];
        AudioCaptureProgress? latestProgress = null;

        try
        {
            await using FloatPcmCaptureSink sink = new(
                options,
                "test-device",
                "Test microphone",
                8_000,
                1,
                journals);
            sink.ChunkCompleted += (_, eventArgs) =>
                completed.Add(eventArgs.Chunk);
            sink.ProgressChanged += (_, progress) => latestProgress = progress;

            await sink.StartAsync();
            float[] samples = Enumerable.Range(0, 12_000)
                .Select(index => (float)Math.Sin(index / 20d) * 0.4f)
                .ToArray();
            Assert.True(sink.TryWrite(samples.AsSpan(0, 7_500)));
            Assert.True(sink.TryWrite(samples.AsSpan(7_500)));

            AudioCaptureResult result = await sink.StopAsync();

            Assert.Equal(48_000, result.TotalPcmBytes);
            Assert.Equal(2, result.ChunkPaths.Count);
            Assert.Equal([32_000L, 16_000L], completed.Select(chunk => chunk.ByteLength));
            Assert.Equal(TimeSpan.FromSeconds(1), completed[0].Duration);
            Assert.Equal(TimeSpan.FromMilliseconds(500), completed[1].Duration);
            Assert.Equal(48_000, latestProgress?.PcmBytes);
            Assert.Equal(TimeSpan.FromSeconds(1.5), latestProgress?.Duration);
            Assert.All(result.ChunkPaths, path => Assert.True(File.Exists(path)));
            Assert.Equal(
                CaptureJournalState.Finalized,
                journals.Saved.Last().State);
            Assert.Contains(
                journals.Saved,
                journal => journal.State == CaptureJournalState.Stopping);
            Assert.False(sink.TryWrite([0.1f]));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class InMemoryJournalStore : ICaptureJournalStore
    {
        private readonly ConcurrentDictionary<Guid, CaptureJournal> _current = [];

        public ConcurrentQueue<CaptureJournal> Saved { get; } = [];

        public Task SaveAsync(
            CaptureJournal journal,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _current[journal.SessionId] = journal;
            Saved.Enqueue(journal);
            return Task.CompletedTask;
        }

        public Task<CaptureJournal?> GetAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _current.TryGetValue(sessionId, out CaptureJournal? journal);
            return Task.FromResult(journal);
        }

        public Task<IReadOnlyList<CaptureJournal>> ListRecoverableAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<CaptureJournal> result = _current.Values
                .Where(journal => journal.State != CaptureJournalState.Finalized)
                .ToArray();
            return Task.FromResult(result);
        }

        public Task DeleteAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _current.TryRemove(sessionId, out _);
            return Task.CompletedTask;
        }
    }
}
