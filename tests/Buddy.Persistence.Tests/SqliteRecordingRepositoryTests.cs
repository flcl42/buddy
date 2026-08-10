using System.Security.Cryptography;
using System.Text;
using Buddy.Core.Domain;

namespace Buddy.Persistence.Tests;

public sealed class SqliteRecordingRepositoryTests
{
    [Fact]
    public async Task RecordingRoundTripAndOptimisticUpdateWork()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        SqliteRecordingRepository repository = new(store.Connections);
        DateTimeOffset startedAt = new(2026, 7, 30, 11, 15, 0, TimeSpan.FromHours(3));
        Recording original = Recording.Start(
            RecordingKind.Meeting,
            startedAt,
            "headset",
            Guid.NewGuid());

        await repository.AddAsync(original);
        Recording? loaded = await repository.GetAsync(original.Id);

        Assert.Equal(original, loaded);

        Recording completed = original
            .CompleteCapture(startedAt.AddMinutes(3))
            .TransitionTo(RecordingStatus.ReadyForPlayback);
        Assert.True(await repository.TryUpdateAsync(completed, expectedVersion: 0));
        Assert.False(await repository.TryUpdateAsync(completed.Rename("stale"), expectedVersion: 0));

        Recording? updated = await repository.GetAsync(original.Id);
        Assert.Equal(completed, updated);
    }

    [Fact]
    public async Task ListSearchesTitlesAndTranscriptsAndExcludesDeleted()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        SqliteRecordingRepository repository = new(store.Connections);
        DateTimeOffset now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        Recording first = Recording.Start(RecordingKind.Meeting, now, id: Guid.NewGuid())
            .Rename("Protocol review");
        Recording second = Recording.Start(RecordingKind.Trainer, now.AddMinutes(1), id: Guid.NewGuid())
            .Rename("Practice take");
        await repository.AddAsync(first);
        await repository.AddAsync(second);
        await repository.AddTranscriptRevisionAsync(CreateRevision(
            first.Id,
            "We discussed the Glamsterdam devnet rollout.",
            now.AddMinutes(2)));

        IReadOnlyList<Recording> titleResults = await repository.ListAsync(new RecordingQuery(Search: "practice"));
        IReadOnlyList<Recording> transcriptResults =
            await repository.ListAsync(new RecordingQuery(Search: "glamsterdam"));

        Assert.Single(titleResults);
        Assert.Equal(second.Id, titleResults[0].Id);
        Assert.Single(transcriptResults);
        Assert.Equal(first.Id, transcriptResults[0].Id);

        Recording deleted = second.SoftDelete(now.AddMinutes(3));
        Assert.True(await repository.TryUpdateAsync(deleted, second.Version));
        Assert.DoesNotContain(
            (await repository.ListAsync(new RecordingQuery())).Select(item => item.Id),
            id => id == second.Id);
        Assert.Contains(
            (await repository.ListAsync(new RecordingQuery(IncludeDeleted: true))).Select(item => item.Id),
            id => id == second.Id);
    }

    [Fact]
    public async Task ArtifactsSegmentsAndTranscriptRevisionsRoundTrip()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        SqliteRecordingRepository repository = new(store.Connections);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Recording recording = Recording.Start(RecordingKind.Trainer, now, id: Guid.NewGuid());
        await repository.AddAsync(recording);

        AudioArtifact artifact = new(
            Guid.NewGuid(),
            recording.Id,
            AudioArtifactKind.Original,
            Path.Combine("2026", "07", recording.Id.ToString("D"), "original.opus"),
            AudioContainer.OggOpus,
            48_000,
            1,
            TimeSpan.FromSeconds(8),
            42_000,
            new string('a', 64),
            null,
            now);
        await repository.AddAudioArtifactAsync(artifact);
        AudioArtifact refreshedArtifact = artifact with
        {
            Duration = TimeSpan.FromSeconds(7.5),
            ByteLength = 41_000,
            Sha256 = new string('b', 64),
            Generator = "kokoro; text-normalization=buddy.markdown-speech.v1",
            CreatedAt = now.AddSeconds(1),
        };
        Assert.True(await repository.UpdateAudioArtifactAsync(refreshedArtifact));
        Assert.False(await repository.UpdateAudioArtifactAsync(
            refreshedArtifact with { Id = Guid.NewGuid() }));
        artifact = refreshedArtifact;
        AudioWaveform waveform = new(
            artifact.Id,
            artifact.Duration,
            Enumerable.Range(0, AudioWaveform.DefaultSampleCount)
                .Select(index => (byte)(index * 2))
                .ToArray(),
            now.AddMilliseconds(50),
            "buddy.waveform.v1");
        await repository.ReplaceAudioWaveformAsync(waveform);

        SpeechSegment[] segments =
        [
            new(
                recording.Id,
                0,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                0.9f),
            new(
                recording.Id,
                1,
                TimeSpan.FromSeconds(7),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(2.2),
                TimeSpan.FromSeconds(3.2),
                0.8f),
        ];
        await repository.ReplaceSpeechSegmentsAsync(recording.Id, segments);

        TranscriptRevision recognized = CreateRevision(recording.Id, "I has a question.", now);
        await repository.AddTranscriptRevisionAsync(recognized);
        TranscriptRevision edited = CreateRevision(
            recording.Id,
            "I have a question.",
            now.AddSeconds(1),
            recognized.Id,
            TranscriptRevisionKind.UserEdited);
        await repository.AddTranscriptRevisionAsync(edited);
        PronunciationAssessment assessment = new(
            recording.Id,
            recognized.Text,
            "aɪ hæz ə kwˈɛstʃən",
            now.AddSeconds(2),
            "large-v3-turbo",
            "buddy.pronunciation.v1",
            [
                new(
                    recording.Id,
                    0,
                    "I",
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(100),
                    0.96f),
                new(
                    recording.Id,
                    1,
                    "has",
                    TimeSpan.FromMilliseconds(120),
                    TimeSpan.FromMilliseconds(400),
                    0.51f),
                new(
                    recording.Id,
                    2,
                    "a",
                    TimeSpan.FromMilliseconds(420),
                    TimeSpan.FromMilliseconds(480),
                    0.93f),
                new(
                    recording.Id,
                    3,
                    "question.",
                    TimeSpan.FromMilliseconds(500),
                    TimeSpan.FromMilliseconds(950),
                    0.87f),
            ]);
        await repository.ReplacePronunciationAssessmentAsync(recording.Id, assessment);

        Assert.Equal([artifact], await repository.GetAudioArtifactsAsync(recording.Id));
        AudioWaveform? loadedWaveform =
            await repository.GetAudioWaveformAsync(artifact.Id);
        Assert.NotNull(loadedWaveform);
        Assert.Equal(waveform.ArtifactId, loadedWaveform.ArtifactId);
        Assert.Equal(waveform.Duration, loadedWaveform.Duration);
        Assert.Equal(waveform.Peaks, loadedWaveform.Peaks);
        Assert.Equal(waveform.CreatedAt, loadedWaveform.CreatedAt);
        Assert.Equal(waveform.SchemaVersion, loadedWaveform.SchemaVersion);
        Assert.Equal(segments, await repository.GetSpeechSegmentsAsync(recording.Id));

        IReadOnlyList<TranscriptRevision> revisions =
            await repository.GetTranscriptRevisionsAsync(recording.Id);
        Assert.Equal(2, revisions.Count);
        Assert.False(revisions[0].IsCurrent);
        Assert.True(revisions[1].IsCurrent);
        Assert.Equal(recognized.Id, revisions[1].ParentRevisionId);
        PronunciationAssessment? loadedAssessment =
            await repository.GetPronunciationAssessmentAsync(recording.Id);
        Assert.NotNull(loadedAssessment);
        Assert.Equal(assessment.RecordingId, loadedAssessment.RecordingId);
        Assert.Equal(assessment.Transcript, loadedAssessment.Transcript);
        Assert.Equal(
            assessment.PhoneticTranscript,
            loadedAssessment.PhoneticTranscript);
        Assert.Equal(assessment.CreatedAt, loadedAssessment.CreatedAt);
        Assert.Equal(assessment.Model, loadedAssessment.Model);
        Assert.Equal(assessment.SchemaVersion, loadedAssessment.SchemaVersion);
        Assert.Equal(assessment.Words, loadedAssessment.Words);

        await repository.ReplacePronunciationAssessmentAsync(recording.Id, null);
        Assert.Null(await repository.GetPronunciationAssessmentAsync(recording.Id));
    }

    private static TranscriptRevision CreateRevision(
        Guid recordingId,
        string text,
        DateTimeOffset createdAt,
        Guid? parentId = null,
        TranscriptRevisionKind kind = TranscriptRevisionKind.Recognized)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        return new TranscriptRevision(
            Guid.NewGuid(),
            recordingId,
            parentId,
            kind,
            text,
            hash,
            createdAt,
            kind == TranscriptRevisionKind.Recognized ? "whisper.net" : null,
            kind == TranscriptRevisionKind.Recognized ? "large-v3-turbo" : null,
            "1",
            true);
    }
}
