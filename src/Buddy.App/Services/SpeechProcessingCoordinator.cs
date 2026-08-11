using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Buddy.App.WinUI;
using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using Buddy.Core.Services;
using Buddy.Language;
using Buddy.Persistence;
using Buddy.Speech;

namespace Buddy.App.Services;

public sealed class SpeechProcessingCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromSeconds(30);
    private const int MaximumAttempts = 3;

    private readonly IBackgroundJobStore _jobs;
    private readonly IRecordingRepository _recordings;
    private readonly IVoiceActivityService _voiceActivity;
    private readonly ITranscriptionService _transcription;
    private readonly IPhoneticTranscriptionService _phonetics;
    private readonly IAudioArchiveService _archives;
    private readonly IAudioPreparationService _audioPreparation;
    private readonly IAudioWaveformService _waveforms;
    private readonly ILocalModelManager _models;
    private readonly ILanguageImprovementProvider _language;
    private readonly LanguagePreferences _languages;
    private readonly BuddyDataPaths _paths;
    private readonly string _workerId = $"{Environment.ProcessId}:{Guid.NewGuid():N}";
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);
    private CancellationTokenSource? _workerCancellation;
    private Task? _worker;
    private bool _disposed;

    public SpeechProcessingCoordinator(
        IBackgroundJobStore jobs,
        IRecordingRepository recordings,
        IVoiceActivityService voiceActivity,
        ITranscriptionService transcription,
        IPhoneticTranscriptionService phonetics,
        IAudioArchiveService archives,
        IAudioPreparationService audioPreparation,
        IAudioWaveformService waveforms,
        ILocalModelManager models,
        ILanguageImprovementProvider language,
        LanguagePreferences languages,
        BuddyDataPaths paths)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _recordings = recordings ?? throw new ArgumentNullException(nameof(recordings));
        _voiceActivity = voiceActivity ?? throw new ArgumentNullException(nameof(voiceActivity));
        _transcription = transcription ?? throw new ArgumentNullException(nameof(transcription));
        _phonetics = phonetics ?? throw new ArgumentNullException(nameof(phonetics));
        _archives = archives ?? throw new ArgumentNullException(nameof(archives));
        _audioPreparation = audioPreparation
            ?? throw new ArgumentNullException(nameof(audioPreparation));
        _waveforms = waveforms ?? throw new ArgumentNullException(nameof(waveforms));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _language = language ?? throw new ArgumentNullException(nameof(language));
        _languages = languages ?? throw new ArgumentNullException(nameof(languages));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public event EventHandler? LibraryChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_worker is not null)
            {
                return;
            }

            await ScheduleRecoverableWorkAsync(cancellationToken).ConfigureAwait(false);
            _workerCancellation = new CancellationTokenSource();
            _worker = Task.Run(
                () => RunWorkerAsync(_workerCancellation.Token),
                CancellationToken.None);
            SignalWorker();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? worker;
        CancellationTokenSource? workerCancellation;

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            worker = _worker;
            workerCancellation = _workerCancellation;
            _worker = null;
            _workerCancellation = null;
            workerCancellation?.Cancel();
            SignalWorker();
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (worker is not null)
        {
            try
            {
                await worker.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (workerCancellation?.IsCancellationRequested == true)
            {
            }
        }

        workerCancellation?.Dispose();
    }

    public async Task QueueInitialProcessingAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnqueueStageAsync(
                recordingId,
                BackgroundJobType.DetectSpeech,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task QueuePendingTranscriptionsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        bool whisperReady = await IsWhisperReadyAsync(cancellationToken)
            .ConfigureAwait(false);

        await ForEachRecordingAsync(
                async recording =>
                {
                    if (recording.Status != RecordingStatus.Ready)
                    {
                        return;
                    }

                    IReadOnlyList<TranscriptRevision> revisions = await _recordings
                        .GetTranscriptRevisionsAsync(recording.Id, cancellationToken)
                        .ConfigureAwait(false);
                    IReadOnlyList<SpeechSegment> segments = await _recordings
                        .GetSpeechSegmentsAsync(recording.Id, cancellationToken)
                        .ConfigureAwait(false);
                    bool hasCurrentTranscript = revisions.Any(
                        revision => revision.IsCurrent);
                    if (hasCurrentTranscript)
                    {
                        PronunciationAssessment? pronunciation =
                            recording.Kind == RecordingKind.Trainer
                                ? await _recordings
                                    .GetPronunciationAssessmentAsync(
                                        recording.Id,
                                        cancellationToken)
                                    .ConfigureAwait(false)
                                : null;
                        if (recording.Kind == RecordingKind.Trainer
                            && (pronunciation is not null
                                && string.IsNullOrWhiteSpace(
                                    pronunciation.PhoneticTranscript)
                                || whisperReady
                                    && segments.Count > 0
                                    && pronunciation is null))
                        {
                            await EnqueueStageAsync(
                                    recording.Id,
                                    BackgroundJobType.AnalyzePronunciation,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }

                        return;
                    }

                    if (whisperReady && segments.Count > 0)
                    {
                        await EnqueueStageAsync(
                                recording.Id,
                                BackgroundJobType.Transcribe,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task QueueTranscriptionAsync(
        Guid recordingId,
        bool replaceCurrent,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (recordingId == Guid.Empty)
        {
            throw new ArgumentException(
                "A recording identifier is required.",
                nameof(recordingId));
        }

        if (!await IsWhisperReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new LocalModelNotInstalledException(
                LocalSpeechModels.WhisperLargeV3Turbo,
                "Whisper large-v3-turbo");
        }

        Recording recording = await GetRecordingAsync(recordingId, cancellationToken)
            .ConfigureAwait(false);
        if (recording.Status is RecordingStatus.Capturing
            or RecordingStatus.FinalizingSource
            or RecordingStatus.DetectingSpeech
            or RecordingStatus.BuildingCompactAudio
            or RecordingStatus.Titling
            or RecordingStatus.Recovering)
        {
            throw new InvalidOperationException(
                "Wait for this recording to finish its current processing step.");
        }

        _ = await GetSourceArtifactAsync(
                recording.Id,
                preferCompact: true,
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<TranscriptRevision> revisions = await _recordings
            .GetTranscriptRevisionsAsync(recording.Id, cancellationToken)
            .ConfigureAwait(false);
        if (!replaceCurrent && revisions.Any(revision => revision.IsCurrent))
        {
            return;
        }

        if (recording.Status != RecordingStatus.Transcribing)
        {
            recording = await EnsureStatusAsync(
                    recording,
                    RecordingStatus.Transcribing,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await EnqueueStageAsync(
                recording.Id,
                BackgroundJobType.Transcribe,
                JsonSerializer.Serialize(
                    new TranscriptionJobRequest(replaceCurrent)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
        _lifecycleGate.Dispose();
        _wakeSignal.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                BackgroundJob? job = await _jobs.TryLeaseNextAsync(
                        _workerId,
                        LeaseDuration,
                        DateTimeOffset.UtcNow,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (job is null)
                {
                    await _wakeSignal
                        .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                await ExecuteLeasedJobAsync(job, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error) when (IsRecoverable(error))
            {
                StartupDiagnostics.Write(
                    $"Speech worker polling failed: {error.GetType().Name}: {error.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteLeasedJobAsync(
        BackgroundJob job,
        CancellationToken applicationCancellation)
    {
        using CancellationTokenSource renewalCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(applicationCancellation);
        Task renewal = RenewLeaseLoopAsync(job.Id, renewalCancellation.Token);

        try
        {
            await ExecuteStageAsync(job, applicationCancellation).ConfigureAwait(false);
            await _jobs.CompleteAsync(job.Id, _workerId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (applicationCancellation.IsCancellationRequested)
        {
            await _jobs.ReleaseAsync(
                    job.Id,
                    _workerId,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (IsRecoverable(error))
        {
            string safeMessage = CreateSafeMessage(error);
            TimeSpan delay = TimeSpan.FromSeconds(Math.Pow(4, job.AttemptCount));
            await _jobs.FailAsync(
                    job.Id,
                    _workerId,
                    GetErrorCode(error),
                    safeMessage,
                    DateTimeOffset.UtcNow + delay,
                    MaximumAttempts,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (job.AttemptCount >= MaximumAttempts
                && job.Type is not BackgroundJobType.GenerateTitle
                    and not BackgroundJobType.AnalyzePronunciation
                    and not BackgroundJobType.BuildWaveform)
            {
                await MarkNeedsAttentionAsync(
                        job.RecordingId,
                        GetErrorCode(error),
                        safeMessage)
                    .ConfigureAwait(false);
            }

            StartupDiagnostics.Write(
                $"Speech job {job.Type} failed on attempt {job.AttemptCount}: "
                + $"{error.GetType().Name}: {error.Message}");
        }
        finally
        {
            renewalCancellation.Cancel();
            try
            {
                await renewal.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (renewalCancellation.IsCancellationRequested)
            {
            }

            RaiseLibraryChanged();
        }
    }

    private async Task RenewLeaseLoopAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(LeaseRenewalInterval, cancellationToken).ConfigureAwait(false);
            bool renewed = await _jobs.RenewLeaseAsync(
                    jobId,
                    _workerId,
                    DateTimeOffset.UtcNow + LeaseDuration,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!renewed)
            {
                throw new InvalidOperationException(
                    "The local speech worker lost its processing lease.");
            }
        }
    }

    private Task ExecuteStageAsync(
        BackgroundJob job,
        CancellationToken cancellationToken)
    {
        if (!job.RecordingId.HasValue)
        {
            throw new InvalidDataException(
                $"Speech processing job {job.Id:D} has no recording.");
        }

        return job.Type switch
        {
            BackgroundJobType.DetectSpeech => DetectSpeechAsync(
                job.RecordingId.Value,
                cancellationToken),
            BackgroundJobType.BuildCompactAudio => BuildCompactAudioAsync(
                job.RecordingId.Value,
                cancellationToken),
            BackgroundJobType.Transcribe => TranscribeAsync(
                job.RecordingId.Value,
                ReadTranscriptionRequest(job.PayloadJson).ReplaceCurrent,
                cancellationToken),
            BackgroundJobType.GenerateTitle => GenerateTitleAsync(
                job.RecordingId.Value,
                cancellationToken),
            BackgroundJobType.AnalyzePronunciation => AnalyzePronunciationAsync(
                job.RecordingId.Value,
                cancellationToken),
            BackgroundJobType.BuildWaveform => BuildWaveformAsync(
                job.RecordingId.Value,
                cancellationToken),
            _ => throw new NotSupportedException(
                $"The local speech worker cannot execute {job.Type}."),
        };
    }

    private async Task DetectSpeechAsync(
        Guid recordingId,
        CancellationToken cancellationToken)
    {
        Recording recording = await GetRecordingAsync(recordingId, cancellationToken)
            .ConfigureAwait(false);
        if (recording.Status is RecordingStatus.BuildingCompactAudio
            or RecordingStatus.Transcribing
            or RecordingStatus.Titling
            or RecordingStatus.Ready)
        {
            await EnqueueStageAsync(
                    recording.Id,
                    BackgroundJobType.BuildCompactAudio,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        recording = await EnsureStatusAsync(
                recording,
                RecordingStatus.DetectingSpeech,
                cancellationToken)
            .ConfigureAwait(false);
        AudioArtifact source = await GetSourceArtifactAsync(
                recording.Id,
                preferCompact: false,
                cancellationToken)
            .ConfigureAwait(false);
        string sourcePath = _paths.ResolveRecordingArtifact(source.RelativePath);
        string recordingDirectory = _paths.GetRecordingDirectory(
            recording.Id,
            recording.CreatedAt);
        string analysisPath = Path.Combine(recordingDirectory, ".buddy-vad.wav");

        IReadOnlyList<DetectedSpeechRegion> detected;
        try
        {
            await _audioPreparation.CreateSpeechWaveAsync(
                    sourcePath,
                    analysisPath,
                    cancellationToken)
                .ConfigureAwait(false);
            detected = await _voiceActivity.DetectAsync(
                    analysisPath,
                    new SpeechDetectionOptions(
                        Threshold: 0.5f,
                        MinimumSpeech: TimeSpan.FromMilliseconds(250),
                        MinimumSilence: TimeSpan.FromMilliseconds(350),
                        Padding: TimeSpan.Zero),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            DeleteAnalysisFile(analysisPath, recordingDirectory);
        }

        IReadOnlyList<SpeechSegment> segments = CompactTimelineBuilder.Build(
            recording.Id,
            source.Duration,
            detected);
        await _recordings.ReplaceSpeechSegmentsAsync(
                recording.Id,
                segments,
                cancellationToken)
            .ConfigureAwait(false);

        TimeSpan speechDuration = TimeSpan.FromTicks(
            segments.Sum(segment => segment.OriginalDuration.Ticks));
        if (speechDuration > recording.WallDuration)
        {
            speechDuration = recording.WallDuration;
        }

        recording = await GetRecordingAsync(recording.Id, cancellationToken)
            .ConfigureAwait(false);
        Recording updated = recording
            .WithDurations(recording.WallDuration, speechDuration)
            .TransitionTo(RecordingStatus.BuildingCompactAudio);
        await UpdateOrThrowAsync(updated, recording.Version, cancellationToken)
            .ConfigureAwait(false);
        await EnqueueStageAsync(
                recording.Id,
                BackgroundJobType.BuildCompactAudio,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task BuildCompactAudioAsync(
        Guid recordingId,
        CancellationToken cancellationToken)
    {
        Recording recording = await GetRecordingAsync(recordingId, cancellationToken)
            .ConfigureAwait(false);
        if (recording.Status is RecordingStatus.Transcribing
            or RecordingStatus.Titling
            or RecordingStatus.Ready)
        {
            return;
        }

        recording = await EnsureStatusAsync(
                recording,
                RecordingStatus.BuildingCompactAudio,
                cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<SpeechSegment> segments = await _recordings
            .GetSpeechSegmentsAsync(recording.Id, cancellationToken)
            .ConfigureAwait(false);
        if (segments.Count == 0)
        {
            await EnsureStatusAsync(
                    recording,
                    RecordingStatus.Ready,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnqueueStageAsync(
                    recording.Id,
                    BackgroundJobType.BuildWaveform,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        IReadOnlyList<AudioArtifact> artifacts = await _recordings
            .GetAudioArtifactsAsync(recording.Id, cancellationToken)
            .ConfigureAwait(false);
        AudioArtifact original = artifacts.FirstOrDefault(
                artifact => artifact.Kind == AudioArtifactKind.Original)
            ?? throw new InvalidDataException("The original audio artifact is missing.");
        AudioArtifact? compact = artifacts.FirstOrDefault(
            artifact => artifact.Kind == AudioArtifactKind.Compact);
        if (compact is null)
        {
            string sourcePath = _paths.ResolveRecordingArtifact(original.RelativePath);
            string recordingDirectory = _paths.GetRecordingDirectory(
                recording.Id,
                recording.CreatedAt);
            string destinationPath = Path.Combine(recordingDirectory, "compact.opus");
            compact = await _archives.CreateCompactArchiveAsync(
                    recording.Id,
                    sourcePath,
                    destinationPath,
                    segments,
                    DateTimeOffset.Now,
                    cancellationToken)
                .ConfigureAwait(false);
            compact = compact with
            {
                RelativePath = _paths.ToRecordingRelativePath(destinationPath),
            };
            await _recordings.AddAudioArtifactAsync(compact, cancellationToken)
                .ConfigureAwait(false);
        }

        else
        {
            string compactPath = _paths.ResolveRecordingArtifact(compact.RelativePath);
            if (!File.Exists(compactPath))
            {
                throw new FileNotFoundException(
                    "The compact audio artifact is missing.",
                    compactPath);
            }
        }

        await EnqueueStageAsync(
                recording.Id,
                BackgroundJobType.BuildWaveform,
                cancellationToken)
            .ConfigureAwait(false);

        if (await IsWhisperReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            recording = await GetRecordingAsync(recording.Id, cancellationToken)
                .ConfigureAwait(false);
            await EnsureStatusAsync(
                    recording,
                    RecordingStatus.Transcribing,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnqueueStageAsync(
                    recording.Id,
                    BackgroundJobType.Transcribe,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            recording = await GetRecordingAsync(recording.Id, cancellationToken)
                .ConfigureAwait(false);
            await EnsureStatusAsync(
                    recording,
                    RecordingStatus.Ready,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task TranscribeAsync(
        Guid recordingId,
        bool replaceCurrent,
        CancellationToken cancellationToken)
    {
        Recording recording = await GetRecordingAsync(recordingId, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<TranscriptRevision> revisions = await _recordings
            .GetTranscriptRevisionsAsync(recording.Id, cancellationToken)
            .ConfigureAwait(false);
        TranscriptRevision? previousCurrent = revisions.LastOrDefault(
            revision => revision.IsCurrent);
        if (!replaceCurrent && previousCurrent is not null)
        {
            if (recording.Status == RecordingStatus.Transcribing)
            {
                await EnsureStatusAsync(
                        recording,
                        RecordingStatus.Ready,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await EnqueueStageAsync(
                    recording.Id,
                    BackgroundJobType.GenerateTitle,
                    cancellationToken)
                .ConfigureAwait(false);
            PronunciationAssessment? pronunciation =
                recording.Kind == RecordingKind.Trainer
                    ? await _recordings
                        .GetPronunciationAssessmentAsync(
                            recording.Id,
                            cancellationToken)
                        .ConfigureAwait(false)
                    : null;
            if (recording.Kind == RecordingKind.Trainer
                && (pronunciation is not null
                    && string.IsNullOrWhiteSpace(
                        pronunciation.PhoneticTranscript)
                    || (await _recordings.GetSpeechSegmentsAsync(
                            recording.Id,
                            cancellationToken)
                        .ConfigureAwait(false)).Count > 0
                        && pronunciation is null))
            {
                await EnqueueStageAsync(
                        recording.Id,
                        BackgroundJobType.AnalyzePronunciation,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        if (!await IsWhisperReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            if (recording.Status is RecordingStatus.Transcribing
                or RecordingStatus.BuildingCompactAudio)
            {
                await EnsureStatusAsync(
                        recording,
                        RecordingStatus.Ready,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        recording = await EnsureStatusAsync(
                recording,
                RecordingStatus.Transcribing,
                cancellationToken)
            .ConfigureAwait(false);
        AudioArtifact source = await GetSourceArtifactAsync(
                recording.Id,
                preferCompact: true,
                cancellationToken)
            .ConfigureAwait(false);
        string sourcePath = _paths.ResolveRecordingArtifact(source.RelativePath);
        string recordingDirectory = _paths.GetRecordingDirectory(
            recording.Id,
            recording.CreatedAt);
        string analysisPath = Path.Combine(recordingDirectory, ".buddy-transcribe.wav");

        TranscriptionResult result;
        try
        {
            await _audioPreparation.CreateSpeechWaveAsync(
                    sourcePath,
                    analysisPath,
                    cancellationToken)
                .ConfigureAwait(false);
            result = await _transcription.TranscribeAsync(
                    analysisPath,
                    new TranscriptionOptions(
                        Language: recording.Kind == RecordingKind.Trainer
                            ? _languages.DialogLanguage.WhisperLanguage
                            : "auto",
                        InitialPrompt: recording.Kind == RecordingKind.Trainer
                            ? _languages.DialogLanguage.InitialPrompt
                            : null,
                        IncludeWordTimestamps: recording.Kind == RecordingKind.Trainer),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            DeleteAnalysisFile(analysisPath, recordingDirectory);
        }

        bool promoteRecognition =
            EditableRecordingTranscriptSelector.ShouldPromoteRecognition(previousCurrent);
        TranscriptRevision revision = new(
            Guid.NewGuid(),
            recording.Id,
            previousCurrent?.Id,
            TranscriptRevisionKind.Recognized,
            result.Text,
            HashText(result.Text),
            DateTimeOffset.Now,
            "Whisper.net",
            result.Model,
            "buddy.transcript.v1",
            promoteRecognition);
        await _recordings.AddTranscriptRevisionAsync(revision, cancellationToken)
            .ConfigureAwait(false);
        if (recording.Kind == RecordingKind.Trainer)
        {
            string phoneticTranscript = await TryCreatePhoneticAsync(
                    result.Text,
                    _languages.DialogLanguage.Locale,
                    cancellationToken)
                .ConfigureAwait(false);
            PronunciationAssessment? assessment = PronunciationAssessmentBuilder.Build(
                recording.Id,
                result.Text,
                result.Model,
                revision.CreatedAt,
                result.Tokens,
                phoneticTranscript);
            if (assessment is not null)
            {
                await _recordings.ReplacePronunciationAssessmentAsync(
                        recording.Id,
                        assessment,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        recording = await GetRecordingAsync(recording.Id, cancellationToken)
            .ConfigureAwait(false);
        await EnsureStatusAsync(
                recording,
                RecordingStatus.Ready,
                cancellationToken)
            .ConfigureAwait(false);
        await EnqueueStageAsync(
                recording.Id,
                BackgroundJobType.GenerateTitle,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task AnalyzePronunciationAsync(
        Guid recordingId,
        CancellationToken cancellationToken)
    {
        Recording recording = await GetRecordingAsync(recordingId, cancellationToken)
            .ConfigureAwait(false);
        if (recording.Kind != RecordingKind.Trainer)
        {
            return;
        }

        PronunciationAssessment? existing = await _recordings
            .GetPronunciationAssessmentAsync(recording.Id, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(existing.PhoneticTranscript))
            {
                return;
            }

            string existingPhonetic = await _phonetics
                .TranscribeAsync(
                    existing.Transcript,
                    _languages.DialogLanguage.Locale,
                    cancellationToken)
                .ConfigureAwait(false);
            await _recordings.ReplacePronunciationAssessmentAsync(
                    recording.Id,
                    existing with
                    {
                        PhoneticTranscript = existingPhonetic,
                        CreatedAt = DateTimeOffset.Now,
                        SchemaVersion = PronunciationAssessmentBuilder.SchemaVersion,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        IReadOnlyList<SpeechSegment> segments = await _recordings
            .GetSpeechSegmentsAsync(recording.Id, cancellationToken)
            .ConfigureAwait(false);
        if (segments.Count == 0)
        {
            return;
        }

        IReadOnlyList<TranscriptRevision> revisions = await _recordings
            .GetTranscriptRevisionsAsync(recording.Id, cancellationToken)
            .ConfigureAwait(false);
        if (!revisions.Any(revision => revision.IsCurrent)
            || !await IsWhisperReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        AudioArtifact source = await GetSourceArtifactAsync(
                recording.Id,
                preferCompact: true,
                cancellationToken)
            .ConfigureAwait(false);
        string sourcePath = _paths.ResolveRecordingArtifact(source.RelativePath);
        string recordingDirectory = _paths.GetRecordingDirectory(
            recording.Id,
            recording.CreatedAt);
        string analysisPath = Path.Combine(recordingDirectory, ".buddy-pronunciation.wav");

        TranscriptionResult result;
        try
        {
            await _audioPreparation.CreateSpeechWaveAsync(
                    sourcePath,
                    analysisPath,
                    cancellationToken)
                .ConfigureAwait(false);
            result = await _transcription.TranscribeAsync(
                    analysisPath,
                    new TranscriptionOptions(
                        Language: _languages.DialogLanguage.WhisperLanguage,
                        InitialPrompt: _languages.DialogLanguage.InitialPrompt,
                        IncludeWordTimestamps: true),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            DeleteAnalysisFile(analysisPath, recordingDirectory);
        }

        PronunciationAssessment? assessment = PronunciationAssessmentBuilder.Build(
            recording.Id,
            result.Text,
            result.Model,
            DateTimeOffset.Now,
            result.Tokens,
            await _phonetics
                .TranscribeAsync(
                    result.Text,
                    _languages.DialogLanguage.Locale,
                    cancellationToken)
                .ConfigureAwait(false));
        if (assessment is null)
        {
            throw new InvalidDataException(
                "Whisper did not return word timing data for this Trainer take.");
        }

        await _recordings.ReplacePronunciationAssessmentAsync(
                recording.Id,
                assessment,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task BuildWaveformAsync(
        Guid recordingId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AudioArtifact> artifacts = await _recordings
            .GetAudioArtifactsAsync(recordingId, cancellationToken)
            .ConfigureAwait(false);
        AudioArtifact? selected = CanonicalAudioArtifactSelector.Select(artifacts);
        if (selected is null
            || await _recordings.GetAudioWaveformAsync(
                    selected.Id,
                    cancellationToken)
                .ConfigureAwait(false) is not null)
        {
            return;
        }

        string path = _paths.ResolveRecordingArtifact(selected.RelativePath);
        AudioWaveform waveform = await _waveforms
            .CreateAsync(
                selected.Id,
                path,
                AudioWaveform.DefaultSampleCount,
                cancellationToken)
            .ConfigureAwait(false);
        await _recordings
            .ReplaceAudioWaveformAsync(waveform, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task GenerateTitleAsync(
        Guid recordingId,
        CancellationToken cancellationToken)
    {
        Recording recording = await GetRecordingAsync(recordingId, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(recording.GeneratedTitle))
        {
            return;
        }

        IReadOnlyList<TranscriptRevision> revisions = await _recordings
            .GetTranscriptRevisionsAsync(recording.Id, cancellationToken)
            .ConfigureAwait(false);
        TranscriptRevision? current = revisions.LastOrDefault(
            revision => revision.IsCurrent);
        if (current is null || string.IsNullOrWhiteSpace(current.Text))
        {
            return;
        }

        ProviderHealth health = await _language.CheckHealthAsync(cancellationToken)
            .ConfigureAwait(false);
        if (health.Status == ProviderHealthStatus.NotConfigured)
        {
            return;
        }

        if (health.Status != ProviderHealthStatus.Available)
        {
            throw new LanguageProviderException(health.Message, health.Status);
        }

        TitleResult title = await _language.CreateTitleAsync(
                new TitleRequest(
                    current.Text,
                    recording.Kind,
                    _languages.DialogLanguage.Locale),
                cancellationToken)
            .ConfigureAwait(false);
        recording = await GetRecordingAsync(recording.Id, cancellationToken)
            .ConfigureAwait(false);
        Recording updated = recording.WithGeneratedTitle(title.Title);
        await UpdateOrThrowAsync(updated, recording.Version, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ScheduleRecoverableWorkAsync(
        CancellationToken cancellationToken)
    {
        bool whisperReady = await IsWhisperReadyAsync(cancellationToken)
            .ConfigureAwait(false);
        await ForEachRecordingAsync(
                async recording =>
                {
                    BackgroundJobType? stage = recording.Status switch
                    {
                        RecordingStatus.ReadyForPlayback => BackgroundJobType.DetectSpeech,
                        RecordingStatus.DetectingSpeech => BackgroundJobType.DetectSpeech,
                        RecordingStatus.BuildingCompactAudio =>
                            BackgroundJobType.BuildCompactAudio,
                        RecordingStatus.Transcribing => BackgroundJobType.Transcribe,
                        _ => null,
                    };
                    if (stage.HasValue)
                    {
                        await EnqueueStageAsync(
                                recording.Id,
                                stage.Value,
                                cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    }

                    if (recording.Status == RecordingStatus.Ready)
                    {
                        await QueueWaveformIfMissingAsync(
                                recording.Id,
                                cancellationToken)
                            .ConfigureAwait(false);
                        IReadOnlyList<TranscriptRevision> revisions = await _recordings
                            .GetTranscriptRevisionsAsync(recording.Id, cancellationToken)
                            .ConfigureAwait(false);
                        IReadOnlyList<SpeechSegment> segments = await _recordings
                            .GetSpeechSegmentsAsync(recording.Id, cancellationToken)
                            .ConfigureAwait(false);
                        bool hasCurrentTranscript = revisions.Any(
                            revision => revision.IsCurrent);
                        if (whisperReady
                            && !hasCurrentTranscript
                            && segments.Count > 0)
                        {
                            await EnqueueStageAsync(
                                    recording.Id,
                                    BackgroundJobType.Transcribe,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        else if (hasCurrentTranscript)
                        {
                            if (string.IsNullOrWhiteSpace(recording.GeneratedTitle))
                            {
                                await EnqueueStageAsync(
                                        recording.Id,
                                        BackgroundJobType.GenerateTitle,
                                        cancellationToken)
                                    .ConfigureAwait(false);
                            }

                            PronunciationAssessment? pronunciation =
                                recording.Kind == RecordingKind.Trainer
                                    ? await _recordings
                                        .GetPronunciationAssessmentAsync(
                                            recording.Id,
                                            cancellationToken)
                                        .ConfigureAwait(false)
                                    : null;
                            if (recording.Kind == RecordingKind.Trainer
                                && (pronunciation is not null
                                    && string.IsNullOrWhiteSpace(
                                        pronunciation.PhoneticTranscript)
                                    || whisperReady
                                        && segments.Count > 0
                                        && pronunciation is null))
                            {
                                await EnqueueStageAsync(
                                        recording.Id,
                                        BackgroundJobType.AnalyzePronunciation,
                                        cancellationToken)
                                    .ConfigureAwait(false);
                            }
                        }
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task QueueWaveformIfMissingAsync(
        Guid recordingId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AudioArtifact> artifacts = await _recordings
            .GetAudioArtifactsAsync(recordingId, cancellationToken)
            .ConfigureAwait(false);
        AudioArtifact? selected = CanonicalAudioArtifactSelector.Select(artifacts);
        if (selected is not null
            && await _recordings.GetAudioWaveformAsync(
                    selected.Id,
                    cancellationToken)
                .ConfigureAwait(false) is null)
        {
            await EnqueueStageAsync(
                    recordingId,
                    BackgroundJobType.BuildWaveform,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<string> TryCreatePhoneticAsync(
        string transcript,
        string locale,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _phonetics
                .TranscribeAsync(transcript, locale, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (
            error is IOException
                or InvalidOperationException
                or ArgumentException)
        {
            StartupDiagnostics.Write(
                $"Phonetic transcription will retry in the background: "
                + $"{error.GetType().Name}: {error.Message}");
            return string.Empty;
        }
    }

    private async Task ForEachRecordingAsync(
        Func<Recording, Task> action,
        CancellationToken cancellationToken)
    {
        const int pageSize = 200;
        int offset = 0;
        while (true)
        {
            IReadOnlyList<Recording> page = await _recordings.ListAsync(
                    new RecordingQuery(Limit: pageSize, Offset: offset),
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (Recording recording in page)
            {
                await action(recording).ConfigureAwait(false);
            }

            if (page.Count < pageSize)
            {
                break;
            }

            offset += page.Count;
        }
    }

    private async Task EnqueueStageAsync(
        Guid recordingId,
        BackgroundJobType type,
        CancellationToken cancellationToken)
    {
        await EnqueueStageAsync(
                recordingId,
                type,
                "{}",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnqueueStageAsync(
        Guid recordingId,
        BackgroundJobType type,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BackgroundJob job = new(
            Guid.NewGuid(),
            recordingId,
            type,
            payloadJson,
            BackgroundJobState.Pending,
            0,
            now,
            now,
            null,
            null,
            null,
            null);
        if (await _jobs.EnqueueIfMissingAsync(job, cancellationToken).ConfigureAwait(false))
        {
            SignalWorker();
        }
    }

    private static TranscriptionJobRequest ReadTranscriptionRequest(
        string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new TranscriptionJobRequest(false);
        }

        try
        {
            return JsonSerializer.Deserialize<TranscriptionJobRequest>(payloadJson)
                ?? new TranscriptionJobRequest(false);
        }
        catch (JsonException)
        {
            return new TranscriptionJobRequest(false);
        }
    }

    private sealed record TranscriptionJobRequest(bool ReplaceCurrent);

    private async Task<Recording> EnsureStatusAsync(
        Recording recording,
        RecordingStatus status,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (recording.Status == status)
            {
                return recording;
            }

            if (!RecordingStateMachine.CanTransition(recording.Status, status))
            {
                throw new InvalidOperationException(
                    $"Recording {recording.Id:D} cannot enter {status} "
                    + $"from {recording.Status}.");
            }

            Recording updated = recording.TransitionTo(status);
            if (await _recordings.TryUpdateAsync(
                    updated,
                    recording.Version,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                RaiseLibraryChanged();
                return updated;
            }

            recording = await GetRecordingAsync(recording.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Recording {recording.Id:D} changed repeatedly during speech processing.");
    }

    private async Task UpdateOrThrowAsync(
        Recording recording,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!await _recordings.TryUpdateAsync(
                recording,
                expectedVersion,
                cancellationToken)
            .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Recording {recording.Id:D} changed during speech processing.");
        }

        RaiseLibraryChanged();
    }

    private async Task<Recording> GetRecordingAsync(
        Guid recordingId,
        CancellationToken cancellationToken)
    {
        return await _recordings.GetAsync(recordingId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException(
                $"Speech processing recording {recordingId:D} no longer exists.");
    }

    private async Task<AudioArtifact> GetSourceArtifactAsync(
        Guid recordingId,
        bool preferCompact,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AudioArtifact> artifacts = await _recordings
            .GetAudioArtifactsAsync(recordingId, cancellationToken)
            .ConfigureAwait(false);
        AudioArtifact? source = preferCompact
            ? CanonicalAudioArtifactSelector.Select(artifacts)
            : CanonicalAudioArtifactSelector.SelectOriginal(artifacts);
        return source
            ?? throw new InvalidDataException("The recording has no source audio artifact.");
    }

    private async Task<bool> IsWhisperReadyAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalModelInfo> models = await _models
            .GetModelsAsync(cancellationToken)
            .ConfigureAwait(false);
        return models.Any(
            model => model.Id == LocalSpeechModels.WhisperLargeV3Turbo
                && model.Status == LocalModelStatus.Ready);
    }

    private async Task MarkNeedsAttentionAsync(
        Guid? recordingId,
        string errorCode,
        string errorMessage)
    {
        if (!recordingId.HasValue)
        {
            return;
        }

        Recording? recording = await _recordings
            .GetAsync(recordingId.Value, CancellationToken.None)
            .ConfigureAwait(false);
        if (recording is null
            || recording.Status == RecordingStatus.NeedsAttention
            || !RecordingStateMachine.CanTransition(
                recording.Status,
                RecordingStatus.NeedsAttention))
        {
            return;
        }

        Recording failed = recording.TransitionTo(
            RecordingStatus.NeedsAttention,
            errorCode,
            errorMessage);
        await _recordings.TryUpdateAsync(
                failed,
                recording.Version,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private void SignalWorker()
    {
        if (_wakeSignal.CurrentCount == 0)
        {
            _wakeSignal.Release();
        }
    }

    private void RaiseLibraryChanged()
    {
        MainThread.BeginInvokeOnMainThread(
            () => LibraryChanged?.Invoke(this, EventArgs.Empty));
    }

    private static void DeleteAnalysisFile(string path, string recordingDirectory)
    {
        string fullDirectory = Path.GetFullPath(recordingDirectory);
        string fullPath = Path.GetFullPath(path);
        if (!string.Equals(
                Path.GetDirectoryName(fullPath),
                fullDirectory,
                StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullPath).StartsWith(
                ".buddy-",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A speech analysis cleanup path was outside its recording directory.");
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    private static string HashText(string text)
    {
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string GetErrorCode(Exception error)
    {
        return error switch
        {
            HttpRequestException => "model-download-network",
            InvalidDataException => "speech-data-invalid",
            FileNotFoundException => "speech-file-missing",
            UnauthorizedAccessException => "speech-file-access",
            LocalModelNotInstalledException => "speech-model-missing",
            _ => "speech-processing-failed",
        };
    }

    private static string CreateSafeMessage(Exception error)
    {
        string message = string.IsNullOrWhiteSpace(error.Message)
            ? "Local speech processing failed."
            : error.Message.Trim();
        return message.Length <= 500 ? message : message[..500];
    }

    private static bool IsRecoverable(Exception error)
    {
        return error is not OutOfMemoryException
            and not StackOverflowException
            and not AccessViolationException;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
