using Buddy.Core.Domain;

namespace Buddy.Core.Abstractions;

public interface IBuddyDatabase
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IAppSettingsStore
{
    Task<string?> GetAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default);
}

public interface IRecordingRepository
{
    Task AddAsync(Recording recording, CancellationToken cancellationToken = default);

    Task<Recording?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Recording>> ListAsync(
        RecordingQuery query,
        CancellationToken cancellationToken = default);

    Task<bool> TryUpdateAsync(
        Recording recording,
        long expectedVersion,
        CancellationToken cancellationToken = default);

    Task AddAudioArtifactAsync(
        AudioArtifact artifact,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAudioArtifactAsync(
        AudioArtifact artifact,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AudioArtifact>> GetAudioArtifactsAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default);

    Task ReplaceSpeechSegmentsAsync(
        Guid recordingId,
        IReadOnlyList<SpeechSegment> segments,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpeechSegment>> GetSpeechSegmentsAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default);

    Task AddTranscriptRevisionAsync(
        TranscriptRevision revision,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TranscriptRevision>> GetTranscriptRevisionsAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default);

    Task ReplacePronunciationAssessmentAsync(
        Guid recordingId,
        PronunciationAssessment? assessment,
        CancellationToken cancellationToken = default);

    Task<PronunciationAssessment?> GetPronunciationAssessmentAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default);

    Task ReplaceAudioWaveformAsync(
        AudioWaveform waveform,
        CancellationToken cancellationToken = default);

    Task<AudioWaveform?> GetAudioWaveformAsync(
        Guid artifactId,
        CancellationToken cancellationToken = default);
}

public interface IDialogRepository
{
    Task AddSessionAsync(
        DialogSession session,
        CancellationToken cancellationToken = default);

    Task<DialogSession?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<DialogSession?> GetSessionByRecordingIdAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default);

    Task<DialogSession?> GetLatestSessionAsync(
        CancellationToken cancellationToken = default);

    Task<DialogSession?> GetActiveSessionAsync(
        CancellationToken cancellationToken = default);

    Task<bool> TryUpdateSessionAsync(
        DialogSession session,
        long expectedVersion,
        CancellationToken cancellationToken = default);

    Task AddMessageAsync(
        DialogMessage message,
        CancellationToken cancellationToken = default);

    Task AddUserMessageWithPronunciationAsync(
        DialogMessage message,
        DialogPronunciationAssessment assessment,
        CancellationToken cancellationToken = default);

    Task UpdateMessageAudioAsync(
        Guid messageId,
        Guid audioArtifactId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DialogMessage>> GetMessagesAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task ReplacePronunciationAssessmentAsync(
        DialogPronunciationAssessment assessment,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, DialogPronunciationAssessment>>
        GetPronunciationAssessmentsAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);
}

public interface IBackgroundJobStore
{
    Task EnqueueAsync(BackgroundJob job, CancellationToken cancellationToken = default);

    Task<bool> EnqueueIfMissingAsync(
        BackgroundJob job,
        CancellationToken cancellationToken = default);

    Task<BackgroundJob?> TryLeaseNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<bool> RenewLeaseAsync(
        Guid jobId,
        string workerId,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(
        Guid jobId,
        string workerId,
        DateTimeOffset availableAt,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        Guid jobId,
        string workerId,
        CancellationToken cancellationToken = default);

    Task FailAsync(
        Guid jobId,
        string workerId,
        string errorCode,
        string errorMessage,
        DateTimeOffset retryAt,
        int maximumAttempts,
        CancellationToken cancellationToken = default);
}

public interface ICaptureJournalStore
{
    Task SaveAsync(CaptureJournal journal, CancellationToken cancellationToken = default);

    Task<CaptureJournal?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CaptureJournal>> ListRecoverableAsync(
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
