using Buddy.App.State;
using Buddy.App.WinUI;
using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using Buddy.Persistence;

namespace Buddy.App.Services;

public sealed class RecordingCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan CaptureChunkDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DialogCaptureChunkDuration = TimeSpan.FromSeconds(1);

    private readonly IAudioCaptureService _captureService;
    private readonly IAudioArchiveService _archiveService;
    private readonly IAppSettingsStore _settings;
    private readonly IRecordingRepository _recordings;
    private readonly ICaptureJournalStore _journals;
    private readonly BuddyDataPaths _paths;
    private readonly BuddyRuntimeState _runtime;
    private readonly SpeechProcessingCoordinator _speechProcessing;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveCapture? _active;
    private bool _disposed;

    public RecordingCoordinator(
        IAudioCaptureService captureService,
        IAudioArchiveService archiveService,
        IAppSettingsStore settings,
        IRecordingRepository recordings,
        ICaptureJournalStore journals,
        BuddyDataPaths paths,
        BuddyRuntimeState runtime,
        SpeechProcessingCoordinator speechProcessing)
    {
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _recordings = recordings ?? throw new ArgumentNullException(nameof(recordings));
        _journals = journals ?? throw new ArgumentNullException(nameof(journals));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _speechProcessing = speechProcessing
            ?? throw new ArgumentNullException(nameof(speechProcessing));
    }

    public bool IsRecording => _active is not null;

    public RecordingKind? ActiveKind => _active?.Recording.Kind;

    public Guid? ActiveRecordingId => _active?.Recording.Id;

    public event EventHandler? LibraryChanged;

    public event EventHandler<AudioCaptureChunkCompletedEventArgs>? CaptureChunkCompleted;

    public async Task ToggleAsync(
        RecordingKind kind,
        CancellationToken cancellationToken = default)
    {
        if (IsRecording)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await StartAsync(kind, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StartAsync(
        RecordingKind kind,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_active is not null)
            {
                throw new InvalidOperationException("A microphone capture is already active.");
            }

            IReadOnlyList<AudioInputDevice> devices = await _captureService
                .GetInputDevicesAsync(cancellationToken)
                .ConfigureAwait(false);
            if (devices.Count == 0)
            {
                throw new InvalidOperationException(
                    "Windows did not report an active microphone.");
            }

            string? selectedDeviceId = await _settings
                .GetAsync(BuddySettings.InputDeviceId, cancellationToken)
                .ConfigureAwait(false);
            AudioInputDevice? device = string.IsNullOrWhiteSpace(selectedDeviceId)
                ? null
                : devices.FirstOrDefault(
                    item => string.Equals(
                        item.Id,
                        selectedDeviceId,
                        StringComparison.Ordinal));
            device ??= devices.FirstOrDefault(item => item.IsDefault) ?? devices[0];

            DateTimeOffset startedAt = DateTimeOffset.Now;
            Recording recording = Recording.Start(kind, startedAt, device.Id);
            await _recordings.AddAsync(recording, cancellationToken).ConfigureAwait(false);
            RaiseLibraryChanged();

            Guid sessionId = Guid.NewGuid();
            string sessionDirectory = _paths.GetCaptureSessionDirectory(sessionId);
            AudioCaptureOptions options = new(
                sessionId,
                recording.Id,
                kind,
                sessionDirectory,
                device.Id,
                kind == RecordingKind.Dialog
                    ? DialogCaptureChunkDuration
                    : CaptureChunkDuration);

            try
            {
                IAudioCaptureSession session = await _captureService
                    .StartAsync(options, cancellationToken)
                    .ConfigureAwait(false);
                ActiveCapture active = new(recording, session, sessionDirectory, device.DisplayName);
                session.ProgressChanged += OnCaptureProgressChanged;
                session.ChunkCompleted += OnCaptureChunkCompleted;
                session.CaptureFaulted += OnCaptureFaulted;
                _active = active;
                UpdateRuntimeForCapture(active, TimeSpan.Zero, 0);
            }
            catch (Exception error) when (
                error is IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException
                    or NotSupportedException
                    or System.Runtime.InteropServices.COMException)
            {
                Recording failed = recording.TransitionTo(
                    RecordingStatus.NeedsAttention,
                    "capture-start",
                    CreateSafeMessage(error));
                await TryUpdateOrThrowAsync(failed, recording.Version, cancellationToken)
                    .ConfigureAwait(false);
                SetAttention("Microphone capture could not start.");
                RaiseLibraryChanged();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ActiveCapture? active = _active;
            if (active is null)
            {
                return;
            }

            active.Session.ProgressChanged -= OnCaptureProgressChanged;
            active.Session.CaptureFaulted -= OnCaptureFaulted;
            _active = null;
            SetProcessing();

            try
            {
                AudioCaptureResult result = await active.Session
                    .StopAsync(cancellationToken)
                    .ConfigureAwait(false);
                await FinalizeCaptureAsync(
                        active.Recording,
                        result,
                        active.SessionDirectory,
                        cancellationToken)
                    .ConfigureAwait(false);
                SetIdle();
            }
            catch (Exception error) when (
                error is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or InvalidOperationException
                    or NotSupportedException)
            {
                await MarkNeedsAttentionAsync(
                        active.Recording.Id,
                        "capture-finalize",
                        error,
                        cancellationToken)
                    .ConfigureAwait(false);
                SetAttention("The recording is safe but needs recovery.");
            }
            finally
            {
                active.Session.ChunkCompleted -= OnCaptureChunkCompleted;
                await active.Session.DisposeAsync().ConfigureAwait(false);
                RaiseLibraryChanged();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecoverInterruptedCapturesAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            IReadOnlyList<CaptureJournal> recoverable = await _journals
                .ListRecoverableAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (CaptureJournal journal in recoverable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Recording? recording = await _recordings
                    .GetAsync(journal.RecordingId, cancellationToken)
                    .ConfigureAwait(false);
                if (recording is null)
                {
                    continue;
                }

                string sessionDirectory = _paths.GetCaptureSessionDirectory(journal.SessionId);
                string[] chunks = Directory.Exists(sessionDirectory)
                    ? Directory.GetFiles(sessionDirectory, "*.pcm")
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .ToArray()
                    : [];
                long totalBytes = chunks.Sum(path => new FileInfo(path).Length);
                if (chunks.Length == 0 || totalBytes == 0)
                {
                    await MarkNeedsAttentionAsync(
                            recording.Id,
                            "capture-recovery-empty",
                            new InvalidDataException("No recoverable microphone chunks were found."),
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                int bytesPerSecond = checked(
                    journal.SampleRate * journal.Channels * (journal.BitsPerSample / 8));
                DateTimeOffset endedAt = journal.StartedAt
                    .AddSeconds(totalBytes / (double)bytesPerSecond);
                AudioCaptureResult result = new(
                    journal.SessionId,
                    journal.RecordingId,
                    journal.StartedAt,
                    endedAt,
                    journal.InputDeviceId ?? "recovered-device",
                    "Recovered microphone",
                    journal.SampleRate,
                    journal.BitsPerSample,
                    journal.Channels,
                    journal.Encoding,
                    chunks,
                    totalBytes);

                SetProcessing();
                try
                {
                    recording = await MoveToRecoveringAsync(recording, cancellationToken)
                        .ConfigureAwait(false);
                    await FinalizeCaptureAsync(
                            recording,
                            result,
                            sessionDirectory,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception error) when (
                    error is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or InvalidOperationException
                        or NotSupportedException)
                {
                    await MarkNeedsAttentionAsync(
                            recording.Id,
                            "capture-recovery",
                            error,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (_active is null)
            {
                SetIdle();
            }

            if (recoverable.Count > 0)
            {
                RaiseLibraryChanged();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_active is not null)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _disposed = true;
        _gate.Dispose();
    }

    private async Task FinalizeCaptureAsync(
        Recording recording,
        AudioCaptureResult capture,
        string sessionDirectory,
        CancellationToken cancellationToken)
    {
        Recording finalizing = recording.Status switch
        {
            RecordingStatus.Capturing => recording.CompleteCapture(capture.EndedAt),
            RecordingStatus.Recovering or RecordingStatus.NeedsAttention =>
                recording.TransitionTo(RecordingStatus.FinalizingSource) with
                {
                    CaptureEndedAt = capture.EndedAt,
                    WallDuration = capture.EndedAt - recording.CaptureStartedAt,
                },
            RecordingStatus.FinalizingSource => recording,
            _ => throw new InvalidOperationException(
                $"Recording {recording.Id:D} cannot be finalized from {recording.Status}."),
        };

        finalizing = finalizing.WithDurations(finalizing.WallDuration, TimeSpan.Zero);
        await TryUpdateOrThrowAsync(finalizing, recording.Version, cancellationToken)
            .ConfigureAwait(false);
        RaiseLibraryChanged();

        IReadOnlyList<AudioArtifact> existing = await _recordings
            .GetAudioArtifactsAsync(recording.Id, cancellationToken)
            .ConfigureAwait(false);
        AudioArtifact? original = existing.FirstOrDefault(
            artifact => artifact.Kind == AudioArtifactKind.Original);

        if (original is null)
        {
            string recordingDirectory = _paths.GetRecordingDirectory(
                recording.Id,
                recording.CreatedAt);
            Directory.CreateDirectory(recordingDirectory);
            string destination = Path.Combine(recordingDirectory, "original.opus");
            original = await _archiveService
                .CreateOriginalArchiveAsync(
                    capture,
                    destination,
                    DateTimeOffset.Now,
                    cancellationToken)
                .ConfigureAwait(false);
            original = original with
            {
                RelativePath = _paths.ToRecordingRelativePath(destination),
            };
            await _recordings.AddAudioArtifactAsync(original, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            string archivePath = _paths.ResolveRecordingArtifact(original.RelativePath);
            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException(
                    "The cataloged source archive is missing.",
                    archivePath);
            }
        }

        Recording ready = finalizing.TransitionTo(RecordingStatus.ReadyForPlayback);
        await TryUpdateOrThrowAsync(ready, finalizing.Version, cancellationToken)
            .ConfigureAwait(false);
        await _journals.DeleteAsync(capture.SessionId, cancellationToken).ConfigureAwait(false);
        DeleteCaptureDirectory(sessionDirectory);
        try
        {
            await _speechProcessing.QueueInitialProcessingAsync(
                    recording.Id,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (
            error is IOException
                or InvalidOperationException
                or System.Data.Common.DbException)
        {
            StartupDiagnostics.Write(
                $"Speech processing will be recovered at startup: "
                + $"{error.GetType().Name}: {error.Message}");
        }

        RaiseLibraryChanged();
    }

    private async Task<Recording> MoveToRecoveringAsync(
        Recording recording,
        CancellationToken cancellationToken)
    {
        if (recording.Status == RecordingStatus.Capturing)
        {
            Recording interrupted = recording.TransitionTo(RecordingStatus.Interrupted);
            await TryUpdateOrThrowAsync(interrupted, recording.Version, cancellationToken)
                .ConfigureAwait(false);
            recording = interrupted;
        }

        if (recording.Status == RecordingStatus.Interrupted)
        {
            Recording recovering = recording.TransitionTo(RecordingStatus.Recovering);
            await TryUpdateOrThrowAsync(recovering, recording.Version, cancellationToken)
                .ConfigureAwait(false);
            recording = recovering;
        }

        return recording;
    }

    private async Task MarkNeedsAttentionAsync(
        Guid recordingId,
        string errorCode,
        Exception error,
        CancellationToken cancellationToken)
    {
        Recording? current = await _recordings
            .GetAsync(recordingId, cancellationToken)
            .ConfigureAwait(false);
        if (current is null || current.Status == RecordingStatus.NeedsAttention)
        {
            return;
        }

        if (!RecordingStateMachine.CanTransition(
                current.Status,
                RecordingStatus.NeedsAttention))
        {
            return;
        }

        Recording failed = current.TransitionTo(
            RecordingStatus.NeedsAttention,
            errorCode,
            CreateSafeMessage(error));
        await TryUpdateOrThrowAsync(failed, current.Version, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task TryUpdateOrThrowAsync(
        Recording recording,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        bool updated = await _recordings
            .TryUpdateAsync(recording, expectedVersion, cancellationToken)
            .ConfigureAwait(false);
        if (!updated)
        {
            throw new InvalidOperationException(
                $"Recording {recording.Id:D} changed during an audio operation.");
        }
    }

    private void OnCaptureProgressChanged(
        object? sender,
        AudioCaptureProgress progress)
    {
        ActiveCapture? active = _active;
        if (active is null)
        {
            return;
        }

        UpdateRuntimeForCapture(active, progress.Duration, progress.Peak);
    }

    private void OnCaptureFaulted(
        object? sender,
        AudioCaptureFaultedEventArgs eventArgs)
    {
        MainThread.BeginInvokeOnMainThread(
            () => _runtime.AttentionMessage =
                "The microphone stopped unexpectedly; saving captured audio.");
        _ = StopAfterFaultAsync();
    }

    private void OnCaptureChunkCompleted(
        object? sender,
        AudioCaptureChunkCompletedEventArgs eventArgs)
    {
        CaptureChunkCompleted?.Invoke(this, eventArgs);
    }

    private async Task StopAfterFaultAsync()
    {
        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error) when (
            error is IOException
                or InvalidOperationException
                or InvalidCastException
                or System.Runtime.InteropServices.COMException)
        {
            SetAttention("The microphone stopped and the recording needs recovery.");
        }
    }

    private void UpdateRuntimeForCapture(
        ActiveCapture active,
        TimeSpan elapsed,
        float peak)
    {
        MainThread.BeginInvokeOnMainThread(
            () =>
            {
                if (!ReferenceEquals(_active, active))
                {
                    return;
                }

                _runtime.ActiveRecordingKind = active.Recording.Kind;
                _runtime.RecordingDeviceName = active.DeviceName;
                _runtime.RecordingElapsed = elapsed;
                _runtime.RecordingPeak = peak;
                _runtime.Mode = BuddyRuntimeMode.Recording;
            });
    }

    private void SetProcessing()
    {
        MainThread.BeginInvokeOnMainThread(
            () =>
            {
                _runtime.RecordingPeak = 0;
                _runtime.Mode = BuddyRuntimeMode.Processing;
            });
    }

    private void SetIdle()
    {
        MainThread.BeginInvokeOnMainThread(
            () =>
            {
                _runtime.Mode = BuddyRuntimeMode.Idle;
                _runtime.ActiveRecordingKind = null;
                _runtime.RecordingDeviceName = null;
                _runtime.RecordingElapsed = TimeSpan.Zero;
                _runtime.RecordingPeak = 0;
                _runtime.AttentionMessage = null;
            });
    }

    private void SetAttention(string message)
    {
        MainThread.BeginInvokeOnMainThread(
            () =>
            {
                _runtime.AttentionMessage = message;
                _runtime.Mode = BuddyRuntimeMode.Attention;
                _runtime.ActiveRecordingKind = null;
                _runtime.RecordingPeak = 0;
            });
    }

    private void RaiseLibraryChanged()
    {
        MainThread.BeginInvokeOnMainThread(
            () => LibraryChanged?.Invoke(this, EventArgs.Empty));
    }

    private void DeleteCaptureDirectory(string sessionDirectory)
    {
        string expected = _paths.GetCaptureSessionDirectory(
            Guid.Parse(Path.GetFileName(Path.TrimEndingDirectorySeparator(sessionDirectory))));
        if (!string.Equals(
                Path.GetFullPath(sessionDirectory),
                expected,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Capture cleanup path did not match its session.");
        }

        if (Directory.Exists(expected))
        {
            Directory.Delete(expected, recursive: true);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string CreateSafeMessage(Exception error)
    {
        return error switch
        {
            UnauthorizedAccessException => "Windows denied microphone or file access.",
            InvalidDataException => "Captured audio data could not be validated.",
            NotSupportedException => error.Message,
            InvalidOperationException => error.Message,
            IOException => error.Message,
            _ => "The audio operation failed.",
        };
    }

    private sealed record ActiveCapture(
        Recording Recording,
        IAudioCaptureSession Session,
        string SessionDirectory,
        string DeviceName);
}
