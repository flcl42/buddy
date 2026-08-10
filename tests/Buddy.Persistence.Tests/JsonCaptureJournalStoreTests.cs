using Buddy.Core.Abstractions;
using Buddy.Core.Domain;

namespace Buddy.Persistence.Tests;

public sealed class JsonCaptureJournalStoreTests
{
    [Fact]
    public async Task SaveRoundTripsAndDeleteRemovesJournal()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        JsonCaptureJournalStore journals = new(store.Paths);
        CaptureJournal journal = CreateJournal(CaptureJournalState.Capturing);

        await journals.SaveAsync(journal);
        CaptureJournal? loaded = await journals.GetAsync(journal.SessionId);

        Assert.Equal(journal, loaded);

        await journals.DeleteAsync(journal.SessionId);
        Assert.Null(await journals.GetAsync(journal.SessionId));
    }

    [Fact]
    public async Task ListRecoverableExcludesFinalizedAndIgnoresCorruptFiles()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        JsonCaptureJournalStore journals = new(store.Paths);
        CaptureJournal capturing = CreateJournal(CaptureJournalState.Capturing);
        CaptureJournal finalized = CreateJournal(CaptureJournalState.Finalized);
        await journals.SaveAsync(capturing);
        await journals.SaveAsync(finalized);

        string corruptPath = Path.Combine(store.Paths.CaptureJournals, $"{Guid.NewGuid():D}.json");
        await File.WriteAllTextAsync(corruptPath, "{not-json");

        IReadOnlyList<CaptureJournal> recoverable = await journals.ListRecoverableAsync();

        CaptureJournal only = Assert.Single(recoverable);
        Assert.Equal(capturing.SessionId, only.SessionId);
        Assert.True(File.Exists(corruptPath));
    }

    private static CaptureJournal CreateJournal(CaptureJournalState state)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new CaptureJournal(
            Guid.NewGuid(),
            Guid.NewGuid(),
            RecordingKind.Meeting,
            state,
            now,
            now,
            "headset",
            48_000,
            16,
            1,
            AudioSampleEncoding.Pcm,
            3,
            288_000);
    }
}
