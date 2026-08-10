using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Buddy.App.Services;
using Buddy.App.State;
using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using Buddy.Language;
using Buddy.Persistence;
using Buddy.Speech;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Buddy.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IBuddyDatabase _database;
    private readonly IRecordingRepository _recordings;
    private readonly IWindowController _window;
    private readonly RecordingCoordinator _recordingCoordinator;
    private readonly SpeechProcessingCoordinator _speechProcessing;
    private readonly IAudioPlaybackService _playback;
    private readonly ILanguageImprovementProvider _language;
    private readonly ISpeechSynthesisService _synthesis;
    private readonly IQwenModelRuntime _qwenRuntime;
    private readonly LocalSetupCoordinator _localSetup;
    private readonly BuddyDataPaths _paths;
    private readonly List<SpeechVoice> _trainerVoices = [];
    private Guid? _playingRecordingId;
    private Guid? _playingArtifactId;
    private Guid? _currentTrainerRecordingId;
    private Guid? _currentTrainerTakeArtifactId;
    private Guid? _currentTrainerGeneratedArtifactId;
    private string? _currentTrainerTakeRelativePath;
    private string? _currentTrainerGeneratedRelativePath;
    private Guid? _loadedTrainerSourceRevisionId;
    private Guid? _loadedTrainerImprovedRevisionId;
    private DateTimeOffset? _loadedTrainerPronunciationAt;
    private bool _loadingTrainerText;
    private bool _trainerSourceDirty;
    private bool _trainerImprovedDirty;
    private bool _initialized;

    public MainViewModel(
        IBuddyDatabase database,
        IRecordingRepository recordings,
        IWindowController window,
        RecordingCoordinator recordingCoordinator,
        SpeechProcessingCoordinator speechProcessing,
        IAudioPlaybackService playback,
        ILanguageImprovementProvider language,
        ISpeechSynthesisService synthesis,
        IQwenModelRuntime qwenRuntime,
        LocalSetupCoordinator localSetup,
        BuddyDataPaths paths,
        BuddyRuntimeState runtime,
        SettingsViewModel settings,
        DialogViewModel dialog)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _recordings = recordings ?? throw new ArgumentNullException(nameof(recordings));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _recordingCoordinator = recordingCoordinator
            ?? throw new ArgumentNullException(nameof(recordingCoordinator));
        _speechProcessing = speechProcessing
            ?? throw new ArgumentNullException(nameof(speechProcessing));
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _language = language ?? throw new ArgumentNullException(nameof(language));
        _synthesis = synthesis ?? throw new ArgumentNullException(nameof(synthesis));
        _qwenRuntime = qwenRuntime
            ?? throw new ArgumentNullException(nameof(qwenRuntime));
        _localSetup = localSetup
            ?? throw new ArgumentNullException(nameof(localSetup));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        Runtime.PropertyChanged += OnRuntimePropertyChanged;
        _recordingCoordinator.LibraryChanged += OnLibraryChanged;
        _speechProcessing.LibraryChanged += OnLibraryChanged;
        _playback.StateChanged += OnPlaybackStateChanged;
    }

    public BuddyRuntimeState Runtime { get; }

    public SettingsViewModel Settings { get; }

    public DialogViewModel Dialog { get; }

    public LocalSetupCoordinator LocalSetup => _localSetup;

    public ObservableCollection<RecordingCardViewModel> Recordings { get; } = [];

    public ObservableCollection<string> TrainerChangeNotes { get; } = [];

    public ObservableCollection<string> TrainerAmbiguityNotes { get; } = [];

    public ObservableCollection<string> TrainerProtectedWarnings { get; } = [];

    public ObservableCollection<string> TrainerVoiceNames { get; } = [];

    public ObservableCollection<PronunciationWordViewModel> TrainerPronunciationWords { get; } = [];

    public ObservableCollection<string> TrainerSpeedNames { get; } =
    [
        "0.9× · relaxed",
        "1.0× · natural",
        "1.1× · focused",
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecordingsSelected))]
    [NotifyPropertyChangedFor(nameof(IsSpeakSelected))]
    [NotifyPropertyChangedFor(nameof(IsMonologueMode))]
    [NotifyPropertyChangedFor(nameof(IsDialogMode))]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMonologueMode))]
    [NotifyPropertyChangedFor(nameof(IsDialogMode))]
    public partial SpeakMode SelectedSpeakMode { get; set; } = SpeakMode.Dialog;

    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RecordingsMessage { get; set; } =
        "Loading your recordings…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImproveTrainerText))]
    public partial string TrainerTranscript { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSynthesizeTrainerText))]
    public partial string TrainerImprovedText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImproveTrainerText))]
    [NotifyPropertyChangedFor(nameof(CanSynthesizeTrainerText))]
    public partial bool IsTrainerBusy { get; set; }

    [ObservableProperty]
    public partial string TrainerStatusMessage { get; set; } =
        "Record a practice take to begin.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrainerPlaybackButtonText))]
    public partial bool IsTrainerTakeAvailable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrainerGeneratedPlaybackButtonText))]
    public partial bool IsTrainerGeneratedAudioAvailable { get; set; }

    [ObservableProperty]
    public partial string TrainerGeneratedStatusText { get; set; } =
        "Create audio to hear the improved version.";

    [ObservableProperty]
    public partial bool HasTrainerFeedback { get; set; }

    [ObservableProperty]
    public partial bool HasTrainerAmbiguities { get; set; }

    [ObservableProperty]
    public partial bool HasTrainerPronunciation { get; set; }

    [ObservableProperty]
    public partial string TrainerPronunciationSummary { get; set; } =
        "No pronunciation data yet";

    [ObservableProperty]
    public partial string TrainerPronunciationStatus { get; set; } =
        "Record a practice take to collect local word-level pronunciation signals.";

    [ObservableProperty]
    public partial bool HasTrainerPhoneticTranscript { get; set; }

    [ObservableProperty]
    public partial string TrainerPhoneticTranscript { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAudioBusy { get; set; }

    [ObservableProperty]
    public partial string? AudioOperationMessage { get; set; }

    [ObservableProperty]
    public partial int ImprovementModeIndex { get; set; } = 1;

    [ObservableProperty]
    public partial int SelectedTrainerVoiceIndex { get; set; } = -1;

    [ObservableProperty]
    public partial int TrainerSpeedIndex { get; set; } = 1;

    public bool IsRecordingsSelected =>
        SpeakNavigationState.IsRecordings(SelectedTabIndex);

    public bool IsSpeakSelected =>
        SpeakNavigationState.IsSpeak(SelectedTabIndex);

    public bool IsMonologueMode =>
        SpeakNavigationState.IsMonologue(
            SelectedTabIndex,
            SelectedSpeakMode);

    public bool IsDialogMode =>
        SpeakNavigationState.IsDialog(
            SelectedTabIndex,
            SelectedSpeakMode);

    public bool CanImproveTrainerText =>
        !IsTrainerBusy && !string.IsNullOrWhiteSpace(TrainerTranscript);

    public bool CanSynthesizeTrainerText =>
        !IsTrainerBusy
        && _currentTrainerRecordingId.HasValue
        && !string.IsNullOrWhiteSpace(TrainerImprovedText);

    public string TrainerPlaybackButtonText =>
        _currentTrainerTakeArtifactId.HasValue
        && IsLoadedRelativePath(_currentTrainerTakeRelativePath)
        && _playback.IsPlaying
            ? "Pause take"
            : "Play take";

    public string TrainerGeneratedPlaybackButtonText =>
        _currentTrainerGeneratedArtifactId.HasValue
        && IsLoadedRelativePath(_currentTrainerGeneratedRelativePath)
        && _playback.IsPlaying
            ? "Pause generated"
            : "Play generated";

    public string MeetingButtonText => Runtime.Mode switch
    {
        BuddyRuntimeMode.Recording when Runtime.ActiveRecordingKind == RecordingKind.Meeting =>
            $"Stop & save · {FormatDuration(Runtime.RecordingElapsed)}",
        BuddyRuntimeMode.Recording when Runtime.ActiveRecordingKind == RecordingKind.Dialog =>
            "Finish AI dialog first",
        BuddyRuntimeMode.Recording => "Stop practice first",
        BuddyRuntimeMode.Processing => "Saving…",
        _ => "Start meeting",
    };

    public string TrayRecordingMenuText => Runtime.IsRecording
        ? "Stop and save recording"
        : "Start meeting recording";

    public string TrainerRecordingButtonText =>
        Runtime.Mode == BuddyRuntimeMode.Recording
        && Runtime.ActiveRecordingKind == RecordingKind.Trainer
            ? $"Stop & save · {FormatDuration(Runtime.RecordingElapsed)}"
            : Runtime.ActiveRecordingKind == RecordingKind.Dialog
                ? "Finish AI dialog first"
            : Runtime.IsRecording
                ? "Stop meeting first"
                : "●  Record";

    public string TrainerTakeStatusText =>
        Runtime.Mode == BuddyRuntimeMode.Recording
        && Runtime.ActiveRecordingKind == RecordingKind.Trainer
            ? $"{Runtime.RecordingDeviceName} · {FormatDuration(Runtime.RecordingElapsed)}"
            : "No take recording in progress";

    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        IsLoading = true;
        try
        {
            await _database.InitializeAsync().ConfigureAwait(true);
            await Settings.LoadAsync().ConfigureAwait(true);
            await LoadTrainerVoicesAsync().ConfigureAwait(true);
            await _recordingCoordinator
                .RecoverInterruptedCapturesAsync()
                .ConfigureAwait(true);
            await Dialog.InitializeAsync().ConfigureAwait(true);
            await _speechProcessing.StartAsync().ConfigureAwait(true);
            await RefreshRecordingsAsync().ConfigureAwait(true);
            await LoadLatestTrainerAsync().ConfigureAwait(true);
            _initialized = true;
        }
        catch (Exception error) when (
            error is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            Runtime.AttentionMessage = "Buddy could not open its local library.";
            Runtime.Mode = BuddyRuntimeMode.Attention;
            RecordingsMessage = error.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshRecordingsAsync()
    {
        IsLoading = true;
        try
        {
            IReadOnlyList<Recording> recordings = await _recordings.ListAsync(
                    new RecordingQuery(
                        Search: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim()))
                .ConfigureAwait(true);

            Recordings.Clear();
            foreach (Recording recording in recordings)
            {
                IReadOnlyList<AudioArtifact> artifacts = await _recordings
                    .GetAudioArtifactsAsync(recording.Id)
                    .ConfigureAwait(true);
                AudioArtifact? playbackArtifact = SelectPlaybackArtifact(artifacts);
                AudioWaveform? waveform = playbackArtifact is null
                    ? null
                    : await _recordings
                        .GetAudioWaveformAsync(playbackArtifact.Id)
                        .ConfigureAwait(true);
                RecordingCardViewModel card = new(
                    recording,
                    playbackArtifact,
                    waveform)
                {
                    IsPlaying = playbackArtifact is not null
                        && IsArtifactLoaded(playbackArtifact)
                        && _playback.IsPlaying,
                };
                Recordings.Add(card);
            }

            RecordingsMessage = recordings.Count == 0
                ? string.IsNullOrWhiteSpace(SearchText)
                    ? "No recordings yet. Start a meeting, practice take, or AI dialog."
                    : "No recordings match this search."
                : $"{recordings.Count} recording{(recordings.Count == 1 ? string.Empty : "s")}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void SelectRecordings()
    {
        SelectedTabIndex = SpeakNavigationState.RecordingsTabIndex;
    }

    [RelayCommand]
    public async Task SelectSpeakAsync()
    {
        SelectedTabIndex = SpeakNavigationState.SpeakTabIndex;
        if (SelectedSpeakMode == SpeakMode.Monologue)
        {
            await LoadLatestTrainerAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task SelectMonologueAsync()
    {
        SelectedSpeakMode = SpeakMode.Monologue;
        SelectedTabIndex = SpeakNavigationState.SpeakTabIndex;
        await LoadLatestTrainerAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public void SelectDialog()
    {
        SelectedSpeakMode = SpeakMode.Dialog;
        SelectedTabIndex = SpeakNavigationState.SpeakTabIndex;
    }

    [RelayCommand]
    public async Task OpenMonologueAsync()
    {
        await SelectMonologueAsync().ConfigureAwait(true);
        _window.Show();
    }

    [RelayCommand]
    public void OpenDialog()
    {
        SelectDialog();
        _window.Show();
    }

    [RelayCommand]
    public void OpenWindow()
    {
        _window.Show();
    }

    [RelayCommand]
    public void HideWindow()
    {
        _window.Hide();
    }

    [RelayCommand]
    public void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
    }

    [RelayCommand]
    public void CloseSettings()
    {
        IsSettingsOpen = false;
    }

    [RelayCommand]
    public async Task ExitApplicationAsync()
    {
        if (Dialog.IsActive)
        {
            await Dialog.FinishForShutdownAsync().ConfigureAwait(true);
        }
        else if (_recordingCoordinator.IsRecording)
        {
            await _recordingCoordinator.StopAsync().ConfigureAwait(true);
        }

        await _speechProcessing.StopAsync().ConfigureAwait(true);
        await _qwenRuntime.UnloadAsync().ConfigureAwait(true);
        _window.ExitApplication();
    }

    [RelayCommand]
    public async Task ToggleMeetingRecordingAsync()
    {
        SelectedTabIndex = SpeakNavigationState.RecordingsTabIndex;
        await ToggleRecordingAsync(RecordingKind.Meeting).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RecordTrainerTakeAsync()
    {
        SelectedSpeakMode = SpeakMode.Monologue;
        SelectedTabIndex = SpeakNavigationState.SpeakTabIndex;
        bool wasStoppingTrainer = _recordingCoordinator.IsRecording
            && _recordingCoordinator.ActiveKind == RecordingKind.Trainer;
        await ToggleRecordingAsync(RecordingKind.Trainer).ConfigureAwait(true);
        if (wasStoppingTrainer)
        {
            await LoadLatestTrainerAsync(force: true).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task ImproveTrainerTextAsync()
    {
        SelectedSpeakMode = SpeakMode.Monologue;
        SelectedTabIndex = SpeakNavigationState.SpeakTabIndex;
        if (!CanImproveTrainerText || !_currentTrainerRecordingId.HasValue)
        {
            TrainerStatusMessage =
                "Record and transcribe a practice take before improving it.";
            return;
        }

        IsTrainerBusy = true;
        TrainerStatusMessage = "Sending only the editable text to the selected AI provider…";
        TrainerChangeNotes.Clear();
        TrainerAmbiguityNotes.Clear();
        TrainerProtectedWarnings.Clear();
        HasTrainerFeedback = false;
        HasTrainerAmbiguities = false;

        try
        {
            TranscriptRevision sourceRevision = await SaveTrainerSourceEditAsync(
                    _currentTrainerRecordingId.Value)
                .ConfigureAwait(true);
            ImprovementMode mode = ImprovementModeIndex switch
            {
                0 => ImprovementMode.CorrectOnly,
                2 => ImprovementMode.ClearAndConcise,
                _ => ImprovementMode.Natural,
            };
            ImprovementResult result = await _language.ImproveAsync(
                    new ImprovementRequest(
                        TrainerTranscript.Trim(),
                        mode,
                        "en-US",
                        [],
                        null))
                .ConfigureAwait(true);
            bool usePolished = mode != ImprovementMode.CorrectOnly
                && !string.IsNullOrWhiteSpace(result.Polished);
            TranscriptRevision corrected = CreateTranscriptRevision(
                _currentTrainerRecordingId.Value,
                sourceRevision.Id,
                TranscriptRevisionKind.Corrected,
                result.Corrected,
                result.Provider,
                result.Model,
                isCurrent: !usePolished);
            await _recordings.AddTranscriptRevisionAsync(corrected).ConfigureAwait(true);

            TranscriptRevision selectedRevision = corrected;
            if (!string.IsNullOrWhiteSpace(result.Polished))
            {
                TranscriptRevision polished = CreateTranscriptRevision(
                    _currentTrainerRecordingId.Value,
                    corrected.Id,
                    TranscriptRevisionKind.Polished,
                    result.Polished,
                    result.Provider,
                    result.Model,
                    isCurrent: usePolished);
                await _recordings.AddTranscriptRevisionAsync(polished).ConfigureAwait(true);
                if (usePolished)
                {
                    selectedRevision = polished;
                }
            }

            _loadingTrainerText = true;
            TrainerImprovedText = selectedRevision.Text;
            _loadingTrainerText = false;
            _trainerImprovedDirty = false;
            _loadedTrainerImprovedRevisionId = selectedRevision.Id;

            foreach (TextChange change in result.Changes)
            {
                TrainerChangeNotes.Add(
                    $"“{change.Original}” → “{change.Replacement}” — {change.Reason}");
            }

            foreach (TextAmbiguity ambiguity in result.Ambiguities)
            {
                string alternatives = ambiguity.Alternatives.Count == 0
                    ? string.Empty
                    : $" Options: {string.Join(" / ", ambiguity.Alternatives)}";
                TrainerAmbiguityNotes.Add(
                    $"“{ambiguity.SourceText}” — {ambiguity.Explanation}{alternatives}");
            }

            foreach (string violation in result.ProtectedTermViolations)
            {
                TrainerProtectedWarnings.Add(
                    $"Review protected term: {violation}");
            }

            HasTrainerFeedback = TrainerChangeNotes.Count > 0
                || TrainerProtectedWarnings.Count > 0;
            HasTrainerAmbiguities = TrainerAmbiguityNotes.Count > 0;
            TrainerStatusMessage =
                $"Improved with {result.Model} in {result.Latency.TotalSeconds:F1}s. "
                + "Review and edit before creating audio.";
        }
        catch (Exception error) when (
            error is LanguageProviderException
                or InvalidDataException
                or IOException
                or UnauthorizedAccessException)
        {
            TrainerStatusMessage = error.Message;
        }
        finally
        {
            _loadingTrainerText = false;
            IsTrainerBusy = false;
            OnPropertyChanged(nameof(CanImproveTrainerText));
        }
    }

    [RelayCommand]
    public async Task SynthesizeTrainerTextAsync()
    {
        SelectedSpeakMode = SpeakMode.Monologue;
        SelectedTabIndex = SpeakNavigationState.SpeakTabIndex;
        if (!CanSynthesizeTrainerText || !_currentTrainerRecordingId.HasValue)
        {
            TrainerStatusMessage =
                "Create or edit a better version before generating speech.";
            return;
        }

        if (_trainerVoices.Count == 0
            || SelectedTrainerVoiceIndex < 0
            || SelectedTrainerVoiceIndex >= _trainerVoices.Count)
        {
            TrainerStatusMessage = "No local Kokoro voice is available.";
            return;
        }

        IsTrainerBusy = true;
        TrainerStatusMessage = "Generating the better version locally with Kokoro…";
        Guid artifactId = Guid.NewGuid();
        string? outputPath = null;
        bool artifactPersisted = false;
        try
        {
            Guid recordingId = _currentTrainerRecordingId.Value;
            Recording recording = await _recordings.GetAsync(recordingId)
                    .ConfigureAwait(true)
                ?? throw new InvalidOperationException(
                    "The current practice take no longer exists.");
            TranscriptRevision revision = await SaveTrainerImprovedEditAsync(recordingId)
                .ConfigureAwait(true);
            SpeechVoice voice = _trainerVoices[SelectedTrainerVoiceIndex];
            float speed = TrainerSpeedIndex switch
            {
                0 => 0.9f,
                2 => 1.1f,
                _ => 1.0f,
            };
            string directory = _paths.GetRecordingDirectory(
                recordingId,
                recording.CreatedAt);
            outputPath = Path.Combine(
                directory,
                $"trainer-generated-{artifactId:N}.wav");
            await _localSetup.EnsureSpeechSynthesisAsync().ConfigureAwait(true);
            SpeechSynthesisResult result = await _synthesis.SynthesizeAsync(
                    revision.Text,
                    outputPath,
                    new SpeechSynthesisOptions(voice.Id, speed, []))
                .ConfigureAwait(true);
            FileInfo file = new(result.OutputPath);
            AudioArtifact artifact = new(
                artifactId,
                recordingId,
                AudioArtifactKind.TrainerGenerated,
                _paths.ToRecordingRelativePath(result.OutputPath),
                AudioContainer.Wave,
                24_000,
                1,
                result.Duration,
                file.Length,
                await ComputeSha256Async(result.OutputPath).ConfigureAwait(true),
                $"{result.Model}; voice={result.VoiceId}; revision={revision.Id:D}",
                DateTimeOffset.Now);
            await _recordings.AddAudioArtifactAsync(artifact).ConfigureAwait(true);
            artifactPersisted = true;
            _currentTrainerGeneratedArtifactId = artifact.Id;
            _currentTrainerGeneratedRelativePath = artifact.RelativePath;
            IsTrainerGeneratedAudioAvailable = true;
            TrainerGeneratedStatusText =
                $"{voice.DisplayName} · {FormatDuration(result.Duration)} · saved locally";
            TrainerStatusMessage =
                "Better audio is ready. The text and generated WAV were saved locally.";
            await ToggleArtifactPlaybackAsync(recordingId, artifact)
                .ConfigureAwait(true);
        }
        catch (LocalModelNotInstalledException error)
        {
            TrainerStatusMessage =
                $"{error.Message} Open Settings and download the verified 310 MB model.";
            IsSettingsOpen = true;
        }
        catch (Exception error) when (
            error is IOException
                or InvalidDataException
                or InvalidOperationException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            TrainerStatusMessage = error.Message;
        }
        finally
        {
            if (!artifactPersisted
                && outputPath is not null
                && File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            IsTrainerBusy = false;
            OnPropertyChanged(nameof(CanSynthesizeTrainerText));
        }
    }

    [RelayCommand]
    public async Task PlayTrainerTakeAsync()
    {
        if (_currentTrainerRecordingId.HasValue
            && _currentTrainerTakeArtifactId.HasValue)
        {
            await ToggleKnownArtifactAsync(
                    _currentTrainerRecordingId.Value,
                    _currentTrainerTakeArtifactId.Value)
                .ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task PlayTrainerGeneratedAsync()
    {
        if (_currentTrainerRecordingId.HasValue
            && _currentTrainerGeneratedArtifactId.HasValue)
        {
            await ToggleKnownArtifactAsync(
                    _currentTrainerRecordingId.Value,
                    _currentTrainerGeneratedArtifactId.Value)
                .ConfigureAwait(true);
        }
    }

    private void OnRuntimePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(BuddyRuntimeState.Mode)
            or nameof(BuddyRuntimeState.RecordingElapsed)
            or nameof(BuddyRuntimeState.ActiveRecordingKind)
            or nameof(BuddyRuntimeState.RecordingDeviceName)
            or nameof(BuddyRuntimeState.RecordingPeak))
        {
            OnPropertyChanged(nameof(MeetingButtonText));
            OnPropertyChanged(nameof(TrayRecordingMenuText));
            OnPropertyChanged(nameof(TrainerRecordingButtonText));
            OnPropertyChanged(nameof(TrainerTakeStatusText));
        }
    }

    [RelayCommand]
    private async Task PlayRecordingAsync(RecordingCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        await TogglePlaybackAsync(card.Id).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SeekRecordingAsync(WaveformSeekRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        double fraction = Math.Clamp(request.Fraction, 0, 1);
        try
        {
            AudioOperationMessage = null;
            AudioArtifact? artifact = await GetPreferredPlaybackArtifactAsync(
                    request.Recording.Id)
                .ConfigureAwait(true);
            if (artifact is null)
            {
                AudioOperationMessage =
                    "This recording has not finished saving yet.";
                return;
            }

            if (!IsArtifactLoaded(artifact))
            {
                string path = _paths.ResolveRecordingArtifact(
                    artifact.RelativePath);
                await _playback.LoadAsync(path).ConfigureAwait(true);
                _playingRecordingId = request.Recording.Id;
                _playingArtifactId = artifact.Id;
            }

            TimeSpan duration = _playback.Duration > TimeSpan.Zero
                ? _playback.Duration
                : artifact.Duration;
            await _playback
                .SeekAsync(TimeSpan.FromTicks(
                    (long)Math.Round(duration.Ticks * fraction)))
                .ConfigureAwait(true);
        }
        catch (Exception error) when (
            error is IOException
                or InvalidOperationException
                or NotSupportedException)
        {
            AudioOperationMessage = error.Message;
        }
        finally
        {
            UpdatePlaybackCards();
        }
    }

    private async Task TogglePlaybackAsync(Guid recordingId)
    {
        try
        {
            AudioOperationMessage = null;
            AudioArtifact? selected = await GetPreferredPlaybackArtifactAsync(
                    recordingId)
                .ConfigureAwait(true);
            if (selected is null)
            {
                AudioOperationMessage = "This recording has not finished saving yet.";
                return;
            }

            await ToggleArtifactPlaybackAsync(recordingId, selected)
                .ConfigureAwait(true);
        }
        catch (Exception error) when (
            error is IOException
                or InvalidOperationException
                or NotSupportedException)
        {
            AudioOperationMessage = error.Message;
        }
        finally
        {
            UpdatePlaybackCards();
        }
    }

    private async Task ToggleKnownArtifactAsync(
        Guid recordingId,
        Guid artifactId)
    {
        try
        {
            AudioOperationMessage = null;
            IReadOnlyList<AudioArtifact> artifacts = await _recordings
                .GetAudioArtifactsAsync(recordingId)
                .ConfigureAwait(true);
            AudioArtifact? artifact = artifacts.FirstOrDefault(
                item => item.Id == artifactId);
            if (artifact is null)
            {
                AudioOperationMessage = "The saved audio artifact could not be found.";
                return;
            }

            await ToggleArtifactPlaybackAsync(recordingId, artifact).ConfigureAwait(true);
        }
        catch (Exception error) when (
            error is IOException
                or InvalidOperationException
                or NotSupportedException)
        {
            AudioOperationMessage = error.Message;
        }
        finally
        {
            UpdatePlaybackCards();
        }
    }

    private async Task ToggleArtifactPlaybackAsync(
        Guid recordingId,
        AudioArtifact artifact)
    {
        string path = _paths.ResolveRecordingArtifact(artifact.RelativePath);
        if (IsLoadedPath(path))
        {
            if (_playback.IsPlaying)
            {
                await _playback.PauseAsync().ConfigureAwait(true);
            }
            else
            {
                await _playback.PlayAsync().ConfigureAwait(true);
            }

            return;
        }

        await _playback.LoadAsync(path).ConfigureAwait(true);
        _playingRecordingId = recordingId;
        _playingArtifactId = artifact.Id;
        await _playback.PlayAsync().ConfigureAwait(true);
    }

    private async Task ToggleRecordingAsync(RecordingKind kind)
    {
        if (IsAudioBusy)
        {
            return;
        }

        IsAudioBusy = true;
        AudioOperationMessage = null;
        try
        {
            if (Dialog.IsActive)
            {
                AudioOperationMessage =
                    "Finish and save the active AI dialog before starting another recording.";
                return;
            }

            await _recordingCoordinator.ToggleAsync(kind).ConfigureAwait(true);
            if (_recordingCoordinator.IsRecording)
            {
                _ = PrepareRecognitionForRecordingAsync();
            }
            await RefreshRecordingsAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (
            error is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or NotSupportedException
                or System.Runtime.InteropServices.COMException)
        {
            AudioOperationMessage = error.Message;
        }
        finally
        {
            IsAudioBusy = false;
        }
    }

    private async Task PrepareRecognitionForRecordingAsync()
    {
        try
        {
            await _localSetup.EnsureSpeechRecognitionAsync().ConfigureAwait(true);
            await _speechProcessing
                .QueuePendingTranscriptionsAsync()
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            AudioOperationMessage =
                "Local recognition setup is paused; recording continues safely.";
        }
        catch (Exception error) when (
            error is HttpRequestException
                or IOException
                or InvalidDataException
                or UnauthorizedAccessException)
        {
            AudioOperationMessage =
                $"Recording continues, but local recognition setup needs attention: {error.Message}";
        }
    }

    private void OnLibraryChanged(object? sender, EventArgs eventArgs)
    {
        _ = RefreshLibraryFromEventAsync();
    }

    private async Task RefreshLibraryFromEventAsync()
    {
        try
        {
            await RefreshRecordingsAsync().ConfigureAwait(true);
            await LoadLatestTrainerAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (
            error is IOException
                or InvalidOperationException)
        {
            AudioOperationMessage = error.Message;
        }
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs eventArgs)
    {
        MainThread.BeginInvokeOnMainThread(
            () =>
            {
                if (_playback.LastError is not null)
                {
                    AudioOperationMessage =
                        $"Playback stopped: {_playback.LastError.Message}";
                }

                UpdatePlaybackCards();
            });
    }

    private void UpdatePlaybackCards()
    {
        foreach (RecordingCardViewModel card in Recordings)
        {
            bool isSelected = IsCardLoaded(card);
            card.IsPlaying = isSelected && _playback.IsPlaying;
            if (isSelected)
            {
                TimeSpan duration = _playback.Duration > TimeSpan.Zero
                    ? _playback.Duration
                    : card.PlaybackDuration;
                TimeSpan position = _playback.Position > duration
                    ? duration
                    : _playback.Position;
                card.PlaybackDuration = duration;
                card.PlaybackPosition = position;
                card.PlaybackProgress = duration > TimeSpan.Zero
                    ? Math.Clamp(
                        position.TotalMilliseconds / duration.TotalMilliseconds,
                        0,
                        1)
                    : 0;
            }
            else
            {
                card.PlaybackPosition = TimeSpan.Zero;
                card.PlaybackProgress = 0;
            }
        }

        OnPropertyChanged(nameof(TrainerPlaybackButtonText));
        OnPropertyChanged(nameof(TrainerGeneratedPlaybackButtonText));
    }

    private async Task<AudioArtifact?> GetPreferredPlaybackArtifactAsync(
        Guid recordingId)
    {
        IReadOnlyList<AudioArtifact> artifacts = await _recordings
            .GetAudioArtifactsAsync(recordingId)
            .ConfigureAwait(true);
        return SelectPlaybackArtifact(artifacts);
    }

    private static AudioArtifact? SelectPlaybackArtifact(
        IReadOnlyList<AudioArtifact> artifacts)
    {
        return artifacts.FirstOrDefault(
                artifact => artifact.Kind == AudioArtifactKind.Compact)
            ?? artifacts.FirstOrDefault(
                artifact => artifact.Kind == AudioArtifactKind.Original);
    }

    private bool IsArtifactLoaded(AudioArtifact artifact)
    {
        return IsLoadedRelativePath(artifact.RelativePath);
    }

    private bool IsCardLoaded(RecordingCardViewModel card)
    {
        return IsLoadedRelativePath(card.PlaybackArtifactRelativePath);
    }

    private bool IsLoadedRelativePath(string? relativePath)
    {
        return !string.IsNullOrWhiteSpace(relativePath)
            && IsLoadedPath(_paths.ResolveRecordingArtifact(relativePath));
    }

    private bool IsLoadedPath(string path)
    {
        return _playback.LoadedPath is not null
            && string.Equals(
                Path.GetFullPath(path),
                _playback.LoadedPath,
                StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadLatestTrainerAsync(bool force = false)
    {
        IReadOnlyList<Recording> trainers = await _recordings.ListAsync(
                new RecordingQuery(Kind: RecordingKind.Trainer, Limit: 1))
            .ConfigureAwait(true);
        Recording? trainer = trainers.Count == 0 ? null : trainers[0];
        if (trainer is null)
        {
            _currentTrainerRecordingId = null;
            _currentTrainerTakeArtifactId = null;
            _currentTrainerGeneratedArtifactId = null;
            _currentTrainerTakeRelativePath = null;
            _currentTrainerGeneratedRelativePath = null;
            _loadedTrainerPronunciationAt = null;
            TrainerPronunciationWords.Clear();
            HasTrainerPronunciation = false;
            HasTrainerPhoneticTranscript = false;
            TrainerPhoneticTranscript = string.Empty;
            TrainerPronunciationSummary = "No pronunciation data yet";
            TrainerPronunciationStatus =
                "Record a practice take to collect local word-level pronunciation signals.";
            IsTrainerTakeAvailable = false;
            IsTrainerGeneratedAudioAvailable = false;
            TrainerGeneratedStatusText =
                "Create audio to hear the improved version.";
            TrainerStatusMessage = "Record a practice take to begin.";
            OnPropertyChanged(nameof(CanSynthesizeTrainerText));
            return;
        }

        bool recordingChanged = _currentTrainerRecordingId != trainer.Id;
        _currentTrainerRecordingId = trainer.Id;
        IReadOnlyList<AudioArtifact> artifacts = await _recordings
            .GetAudioArtifactsAsync(trainer.Id)
            .ConfigureAwait(true);
        AudioArtifact? take = artifacts.FirstOrDefault(
                artifact => artifact.Kind == AudioArtifactKind.Compact)
            ?? artifacts.FirstOrDefault(
                artifact => artifact.Kind == AudioArtifactKind.Original);
        AudioArtifact? generated = artifacts
            .Where(artifact => artifact.Kind == AudioArtifactKind.TrainerGenerated)
            .OrderBy(artifact => artifact.CreatedAt)
            .LastOrDefault();
        _currentTrainerTakeArtifactId = take?.Id;
        _currentTrainerGeneratedArtifactId = generated?.Id;
        _currentTrainerTakeRelativePath = take?.RelativePath;
        _currentTrainerGeneratedRelativePath = generated?.RelativePath;
        IsTrainerTakeAvailable = take is not null;
        IsTrainerGeneratedAudioAvailable = generated is not null;
        TrainerGeneratedStatusText = generated is null
            ? "Create audio to hear the improved version."
            : $"{FormatDuration(generated.Duration)} · "
                + $"{FormatGenerator(generated.Generator)} · saved locally";

        IReadOnlyList<TranscriptRevision> revisions = await _recordings
            .GetTranscriptRevisionsAsync(trainer.Id)
            .ConfigureAwait(true);
        PronunciationAssessment? pronunciation = await _recordings
            .GetPronunciationAssessmentAsync(trainer.Id)
            .ConfigureAwait(true);
        TranscriptRevision? source = revisions.LastOrDefault(
            revision => revision.Kind is TranscriptRevisionKind.Recognized
                or TranscriptRevisionKind.UserEdited);
        TranscriptRevision? improved = revisions.LastOrDefault(
            revision => revision.Kind is TranscriptRevisionKind.Corrected
                or TranscriptRevisionKind.Polished);

        if (recordingChanged)
        {
            _loadedTrainerSourceRevisionId = null;
            _loadedTrainerImprovedRevisionId = null;
            _loadedTrainerPronunciationAt = null;
            _trainerSourceDirty = false;
            _trainerImprovedDirty = false;
            TrainerChangeNotes.Clear();
            TrainerAmbiguityNotes.Clear();
            TrainerProtectedWarnings.Clear();
            HasTrainerFeedback = false;
            HasTrainerAmbiguities = false;
        }

        if (pronunciation is null)
        {
            if (_loadedTrainerPronunciationAt.HasValue
                || TrainerPronunciationWords.Count > 0)
            {
                TrainerPronunciationWords.Clear();
            }

            _loadedTrainerPronunciationAt = null;
            HasTrainerPronunciation = false;
            HasTrainerPhoneticTranscript = false;
            TrainerPhoneticTranscript = string.Empty;
            TrainerPronunciationSummary = "Pronunciation analysis pending";
            TrainerPronunciationStatus = source is null
                ? "Word-level pronunciation data will appear after local recognition finishes."
                : "Buddy is preparing word clarity and timing locally from this take.";
        }
        else
        {
            if (force
                || recordingChanged
                || pronunciation.CreatedAt != _loadedTrainerPronunciationAt)
            {
                TrainerPronunciationWords.Clear();
                foreach (PronunciationWord word in pronunciation.Words)
                {
                    TrainerPronunciationWords.Add(new PronunciationWordViewModel(word));
                }

                _loadedTrainerPronunciationAt = pronunciation.CreatedAt;
            }

            HasTrainerPronunciation = TrainerPronunciationWords.Count > 0;
            HasTrainerPhoneticTranscript = !string.IsNullOrWhiteSpace(
                pronunciation.PhoneticTranscript);
            TrainerPhoneticTranscript = HasTrainerPhoneticTranscript
                ? $"/{pronunciation.PhoneticTranscript.Trim().Trim('/')}/"
                : string.Empty;
            TrainerPronunciationSummary = CreatePronunciationSummary(pronunciation);
            TrainerPronunciationStatus =
                "Red and amber words deserve a replay. This is Whisper's local "
                + "intelligibility signal, not a phoneme-level accent score.";
        }

        if ((force || recordingChanged || source?.Id != _loadedTrainerSourceRevisionId)
            && (!_trainerSourceDirty || force || recordingChanged))
        {
            _loadingTrainerText = true;
            TrainerTranscript = source?.Text ?? string.Empty;
            _loadingTrainerText = false;
            _trainerSourceDirty = false;
            _loadedTrainerSourceRevisionId = source?.Id;
        }

        if ((force
                || recordingChanged
                || improved?.Id != _loadedTrainerImprovedRevisionId)
            && (!_trainerImprovedDirty || force || recordingChanged))
        {
            _loadingTrainerText = true;
            TrainerImprovedText = improved?.Text ?? string.Empty;
            _loadingTrainerText = false;
            _trainerImprovedDirty = false;
            _loadedTrainerImprovedRevisionId = improved?.Id;
        }

        TrainerStatusMessage = trainer.Status switch
        {
            RecordingStatus.Capturing => "Listening to your practice take…",
            RecordingStatus.FinalizingSource => "Saving your original take…",
            RecordingStatus.ReadyForPlayback => "Finding speech locally…",
            RecordingStatus.DetectingSpeech => "Finding speech locally…",
            RecordingStatus.BuildingCompactAudio => "Removing long pauses…",
            RecordingStatus.Transcribing => "Recognizing your words locally…",
            RecordingStatus.NeedsAttention =>
                trainer.LastErrorMessage ?? "This take needs attention.",
            RecordingStatus.Ready when source is null =>
                "No clear speech was recognized in this take.",
            RecordingStatus.Ready =>
                "Recognition is editable. Fix any mistakes, then improve it.",
            _ => "Preparing your practice take…",
        };
        OnPropertyChanged(nameof(TrainerPlaybackButtonText));
        OnPropertyChanged(nameof(TrainerGeneratedPlaybackButtonText));
        OnPropertyChanged(nameof(CanSynthesizeTrainerText));
    }

    private async Task<TranscriptRevision> SaveTrainerSourceEditAsync(
        Guid recordingId)
    {
        string text = TrainerTranscript.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        IReadOnlyList<TranscriptRevision> revisions = await _recordings
            .GetTranscriptRevisionsAsync(recordingId)
            .ConfigureAwait(true);
        TranscriptRevision? source = revisions.LastOrDefault(
            revision => revision.Kind is TranscriptRevisionKind.Recognized
                or TranscriptRevisionKind.UserEdited);
        if (source is not null
            && string.Equals(source.Text, text, StringComparison.Ordinal))
        {
            _trainerSourceDirty = false;
            return source;
        }

        TranscriptRevision edited = CreateTranscriptRevision(
            recordingId,
            source?.Id,
            TranscriptRevisionKind.UserEdited,
            text,
            "user",
            null,
            isCurrent: true);
        await _recordings.AddTranscriptRevisionAsync(edited).ConfigureAwait(true);
        _loadedTrainerSourceRevisionId = edited.Id;
        _trainerSourceDirty = false;
        return edited;
    }

    private async Task<TranscriptRevision> SaveTrainerImprovedEditAsync(
        Guid recordingId)
    {
        string text = TrainerImprovedText.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        IReadOnlyList<TranscriptRevision> revisions = await _recordings
            .GetTranscriptRevisionsAsync(recordingId)
            .ConfigureAwait(true);
        TranscriptRevision? improved = revisions.LastOrDefault(
            revision => revision.Kind is TranscriptRevisionKind.Corrected
                or TranscriptRevisionKind.Polished);
        if (improved is not null
            && string.Equals(improved.Text, text, StringComparison.Ordinal))
        {
            _trainerImprovedDirty = false;
            return improved;
        }

        TranscriptRevision edited = CreateTranscriptRevision(
            recordingId,
            improved?.Id ?? _loadedTrainerSourceRevisionId,
            TranscriptRevisionKind.Polished,
            text,
            "user",
            null,
            isCurrent: true);
        await _recordings.AddTranscriptRevisionAsync(edited).ConfigureAwait(true);
        _loadedTrainerImprovedRevisionId = edited.Id;
        _trainerImprovedDirty = false;
        return edited;
    }

    private static TranscriptRevision CreateTranscriptRevision(
        Guid recordingId,
        Guid? parentRevisionId,
        TranscriptRevisionKind kind,
        string text,
        string? provider,
        string? model,
        bool isCurrent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        string normalized = text.Trim();
        return new TranscriptRevision(
            Guid.NewGuid(),
            recordingId,
            parentRevisionId,
            kind,
            normalized,
            Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(normalized))),
            DateTimeOffset.Now,
            provider,
            model,
            "buddy.transcript.v1",
            isCurrent);
    }

    partial void OnTrainerTranscriptChanged(string value)
    {
        if (!_loadingTrainerText)
        {
            _trainerSourceDirty = true;
        }
    }

    partial void OnTrainerImprovedTextChanged(string value)
    {
        if (!_loadingTrainerText)
        {
            _trainerImprovedDirty = true;
        }
    }

    private async Task LoadTrainerVoicesAsync()
    {
        if (_trainerVoices.Count > 0)
        {
            return;
        }

        IReadOnlyList<SpeechVoice> voices = await _synthesis
            .GetVoicesAsync()
            .ConfigureAwait(true);
        foreach (SpeechVoice voice in voices)
        {
            _trainerVoices.Add(voice);
            TrainerVoiceNames.Add(voice.DisplayName);
        }

        SelectedTrainerVoiceIndex = Math.Max(
            0,
            _trainerVoices.FindIndex(voice => voice.Id == "af_heart"));
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static string FormatGenerator(string? generator)
    {
        if (string.IsNullOrWhiteSpace(generator))
        {
            return "Kokoro";
        }

        string model = generator.Split(';', 2)[0];
        return model == LocalSpeechModels.KokoroEnglishV1
            ? "Kokoro 82M"
            : model;
    }

    private static string CreatePronunciationSummary(
        PronunciationAssessment assessment)
    {
        string confidence = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{assessment.OverallConfidence * 100:0}% average confidence");
        string pace = assessment.WordsPerMinute > 0
            ? string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{assessment.WordsPerMinute:0} wpm")
            : "pace unavailable";
        return $"{confidence} · {pace} · "
            + $"{assessment.LikelyIssueWordCount} likely unclear · "
            + $"{assessment.ReviewWordCount} review";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(
                @"h\:mm\:ss",
                System.Globalization.CultureInfo.InvariantCulture)
            : duration.ToString(
                @"m\:ss",
                System.Globalization.CultureInfo.InvariantCulture);
    }
}
