using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Buddy.App.Services;
using Buddy.App.State;
using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using Buddy.Core.Services;
using Buddy.Language;
using Buddy.Persistence;
using Buddy.Speech;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Buddy.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IBuddyDatabase _database;
    private readonly IRecordingRepository _recordings;
    private readonly IWindowController _window;
    private readonly RecordingCoordinator _recordingCoordinator;
    private readonly SpeechProcessingCoordinator _speechProcessing;
    private readonly IAudioPlaybackService _playback;
    private readonly ILanguageImprovementProvider _language;
    private readonly ISpeechSynthesisService _synthesis;
    private readonly LocalSetupCoordinator _localSetup;
    private readonly LanguagePreferences _languages;
    private readonly UiLocalizationService _localization;
    private readonly BuddyDataPaths _paths;
    private readonly SemaphoreSlim _workspaceInitializationGate = new(1, 1);
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
    private bool _workspaceInitialized;
    private string? _trainerVoiceLanguageId;

    public MainViewModel(
        IBuddyDatabase database,
        IRecordingRepository recordings,
        IWindowController window,
        RecordingCoordinator recordingCoordinator,
        SpeechProcessingCoordinator speechProcessing,
        IAudioPlaybackService playback,
        ILanguageImprovementProvider language,
        ISpeechSynthesisService synthesis,
        LocalSetupCoordinator localSetup,
        LanguagePreferences languages,
        UiLocalizationService localization,
        BuddyDataPaths paths,
        BuddyRuntimeState runtime,
        SettingsViewModel settings,
        FeedbackViewModel feedback,
        DialogViewModel dialog,
        OnboardingViewModel onboarding)
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
        _localSetup = localSetup
            ?? throw new ArgumentNullException(nameof(localSetup));
        _languages = languages ?? throw new ArgumentNullException(nameof(languages));
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        Dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        Onboarding = onboarding ?? throw new ArgumentNullException(nameof(onboarding));
        RefreshLocalizedChoiceLists();
        Onboarding.Completed += OnOnboardingCompleted;
        _localization.Changed += OnLocalizationChanged;
        _languages.Changed += OnLanguagePreferencesChanged;
        Runtime.PropertyChanged += OnRuntimePropertyChanged;
        _recordingCoordinator.LibraryChanged += OnLibraryChanged;
        _speechProcessing.LibraryChanged += OnLibraryChanged;
        _playback.StateChanged += OnPlaybackStateChanged;
    }

    public BuddyRuntimeState Runtime { get; }

    public SettingsViewModel Settings { get; }

    public FeedbackViewModel Feedback { get; }

    public DialogViewModel Dialog { get; }

    public OnboardingViewModel Onboarding { get; }

    public LocalSetupCoordinator LocalSetup => _localSetup;

    public ObservableCollection<RecordingCardViewModel> Recordings { get; } = [];

    public ObservableCollection<string> TrainerChangeNotes { get; } = [];

    public ObservableCollection<string> TrainerAmbiguityNotes { get; } = [];

    public ObservableCollection<string> TrainerProtectedWarnings { get; } = [];

    public ObservableCollection<string> TrainerVoiceNames { get; } = [];

    public ObservableCollection<PronunciationWordViewModel> TrainerPronunciationWords { get; } = [];

    public ObservableCollection<string> ImprovementModeNames { get; } = [];

    public ObservableCollection<string> TrainerSpeedNames { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecordingsSelected))]
    [NotifyPropertyChangedFor(nameof(IsSpeakSelected))]
    [NotifyPropertyChangedFor(nameof(IsSpeakModeChooserVisible))]
    [NotifyPropertyChangedFor(nameof(IsMonologueMode))]
    [NotifyPropertyChangedFor(nameof(IsDialogMode))]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpeakModeChooserVisible))]
    [NotifyPropertyChangedFor(nameof(IsMonologueMode))]
    [NotifyPropertyChangedFor(nameof(IsDialogMode))]
    public partial SpeakMode? SelectedSpeakMode { get; set; }

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

    public bool IsSpeakModeChooserVisible =>
        SpeakNavigationState.IsChooser(
            SelectedTabIndex,
            SelectedSpeakMode);

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
            $"{_localization.Get("StopAndSave")} · {FormatDuration(Runtime.RecordingElapsed)}",
        BuddyRuntimeMode.Recording when Runtime.ActiveRecordingKind == RecordingKind.Dialog =>
            _localization.Get("FinishAiDialogFirst"),
        BuddyRuntimeMode.Recording => _localization.Get("StopPracticeFirst"),
        BuddyRuntimeMode.Processing => _localization.Get("Saving"),
        _ => _localization.Get("StartMicRecording"),
    };

    public string TrayRecordingMenuText => Runtime.IsRecording
        ? _localization.Get("StopAndSaveRecording")
        : _localization.Get("StartMicRecording");

    public string TrainerRecordingButtonText =>
        Runtime.Mode == BuddyRuntimeMode.Recording
        && Runtime.ActiveRecordingKind == RecordingKind.Trainer
            ? $"{_localization.Get("StopAndSave")} · {FormatDuration(Runtime.RecordingElapsed)}"
            : Runtime.ActiveRecordingKind == RecordingKind.Dialog
                ? _localization.Get("FinishAiDialogFirst")
            : Runtime.IsRecording
                ? _localization.Get("StopMeetingFirst")
                : _localization.Get("Record");

    public string TrainerTakeStatusText =>
        Runtime.Mode == BuddyRuntimeMode.Recording
        && Runtime.ActiveRecordingKind == RecordingKind.Trainer
            ? $"{Runtime.RecordingDeviceName} · {FormatDuration(Runtime.RecordingElapsed)}"
            : _localization.Get("NoTakeInProgress");

    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        IsLoading = true;
        try
        {
            await _database.InitializeAsync().ConfigureAwait(true);
            await _languages.LoadAsync().ConfigureAwait(true);
            await Onboarding.InitializeAsync().ConfigureAwait(true);
            if (!Onboarding.IsVisible)
            {
                await InitializeWorkspaceAsync().ConfigureAwait(true);
            }
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

    private async void OnOnboardingCompleted(object? sender, EventArgs eventArgs)
    {
        try
        {
            IsLoading = true;
            await InitializeWorkspaceAsync().ConfigureAwait(true);
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

    private async Task InitializeWorkspaceAsync()
    {
        await _workspaceInitializationGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_workspaceInitialized)
            {
                return;
            }

            await Settings.LoadAsync().ConfigureAwait(true);
            await LoadTrainerVoicesAsync().ConfigureAwait(true);
            await _recordingCoordinator
                .RecoverInterruptedCapturesAsync()
                .ConfigureAwait(true);
            await Dialog.InitializeAsync().ConfigureAwait(true);
            await _speechProcessing.StartAsync().ConfigureAwait(true);
            await RefreshRecordingsAsync().ConfigureAwait(true);
            await LoadLatestTrainerAsync().ConfigureAwait(true);
            _workspaceInitialized = true;
        }
        finally
        {
            _workspaceInitializationGate.Release();
        }
    }

    [RelayCommand]
    public async Task RefreshRecordingsAsync()
    {
        IsLoading = true;
        try
        {
            Dictionary<Guid, RecordingTranscriptUiState> transcriptUiStates =
                Recordings.ToDictionary(
                    card => card.Id,
                    card => card.CaptureTranscriptUiState());
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
                AudioArtifact? playbackArtifact =
                    CanonicalAudioArtifactSelector.Select(artifacts);
                AudioWaveform? waveform = playbackArtifact is null
                    ? null
                    : await _recordings
                        .GetAudioWaveformAsync(playbackArtifact.Id)
                        .ConfigureAwait(true);
                IReadOnlyList<TranscriptRevision> revisions = await _recordings
                    .GetTranscriptRevisionsAsync(recording.Id)
                    .ConfigureAwait(true);
                TranscriptRevision? sourceTranscript =
                    EditableRecordingTranscriptSelector.Select(revisions);
                RecordingCardViewModel card = new(
                    recording,
                    playbackArtifact,
                    waveform,
                    sourceTranscript)
                {
                    IsPlaying = playbackArtifact is not null
                        && IsArtifactLoaded(playbackArtifact)
                        && _playback.IsPlaying,
                };
                if (transcriptUiStates.TryGetValue(
                        recording.Id,
                        out RecordingTranscriptUiState? transcriptState))
                {
                    card.RestoreTranscriptUiState(transcriptState);
                }

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
    public void SelectSpeak()
    {
        SelectedTabIndex = SpeakNavigationState.SpeakTabIndex;
        SelectedSpeakMode = null;
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
    public async Task StartDialogFromChooserAsync()
    {
        SelectDialog();
        if (IsDemoPreviewOnly)
        {
            return;
        }
        if (Dialog.StartDialogCommand.CanExecute(null))
        {
            await Dialog.StartDialogCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task StartMonologueFromChooserAsync()
    {
        await SelectMonologueAsync().ConfigureAwait(true);
        if (IsDemoPreviewOnly)
        {
            return;
        }
        if (!_recordingCoordinator.IsRecording)
        {
            await RecordTrainerTakeAsync().ConfigureAwait(true);
        }
    }

    private static bool IsDemoPreviewOnly =>
        string.Equals(
            Environment.GetEnvironmentVariable("BUDDY_DEMO_PREVIEW_ONLY"),
            "1",
            StringComparison.Ordinal);

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
        if (!IsSettingsOpen)
        {
            Feedback.Close();
        }

        IsSettingsOpen = !IsSettingsOpen;
    }

    [RelayCommand]
    public void CloseSettings()
    {
        IsSettingsOpen = false;
    }

    [RelayCommand]
    public void OpenFeedback()
    {
        IsSettingsOpen = false;
        Feedback.Open();
    }

    [RelayCommand]
    public void OpenFeedbackWindow()
    {
        OpenFeedback();
        _window.Show();
    }

    [RelayCommand]
    public void ExitApplication()
    {
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
                        _languages.DialogLanguage.Locale,
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
            TrainerStatusMessage = "No local speaking voice is available.";
            return;
        }

        IsTrainerBusy = true;
        TrainerStatusMessage = "Generating the better version with a local voice…";
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
            if (SpeechVoiceSelector.RequiresKokoro(_languages.DialogLanguage))
            {
                await _localSetup.EnsureSpeechSynthesisAsync().ConfigureAwait(true);
            }
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
                result.SampleRate,
                result.Channels,
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
    private void ToggleRecordingTranscript(RecordingCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        AudioOperationMessage = null;
        card.IsTranscriptExpanded = !card.IsTranscriptExpanded;
    }

    [RelayCommand]
    private async Task RequestRecordingTranscriptionAsync(
        RecordingCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!card.CanRequestTranscription)
        {
            return;
        }

        card.IsTranscriptExpanded = true;
        AudioOperationMessage = null;
        try
        {
            if (card.IsTranscriptDirty)
            {
                await SaveRecordingTranscriptCoreAsync(card).ConfigureAwait(true);
            }

            await _localSetup
                .EnsureSpeechRecognitionAsync()
                .ConfigureAwait(true);
            await _speechProcessing
                .QueueTranscriptionAsync(
                    card.Id,
                    replaceCurrent: card.HasTranscript)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            AudioOperationMessage =
                _localization.Get("TranscriptionSetupPaused");
        }
        catch (Exception error) when (
            error is HttpRequestException
                or IOException
                or InvalidDataException
                or UnauthorizedAccessException
                or InvalidOperationException
                or NotSupportedException)
        {
            AudioOperationMessage = error.Message;
        }
    }

    [RelayCommand]
    private async Task SaveRecordingTranscriptAsync(
        RecordingCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!card.CanSaveTranscript)
        {
            return;
        }

        try
        {
            AudioOperationMessage = null;
            await SaveRecordingTranscriptCoreAsync(card).ConfigureAwait(true);
            AudioOperationMessage = _localization.Get("TranscriptSaved");
        }
        catch (Exception error) when (
            error is IOException
                or InvalidOperationException
                or UnauthorizedAccessException)
        {
            AudioOperationMessage = error.Message;
        }
    }

    [RelayCommand]
    private async Task CopyRecordingTranscriptAsync(
        RecordingCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!card.HasTranscript)
        {
            return;
        }

        try
        {
            await Clipboard.Default
                .SetTextAsync(card.TranscriptText)
                .ConfigureAwait(true);
            AudioOperationMessage = _localization.Get("TranscriptCopied");
        }
        catch (Exception error) when (
            error is InvalidOperationException
                or NotSupportedException)
        {
            AudioOperationMessage = error.Message;
        }
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
        return CanonicalAudioArtifactSelector.Select(artifacts);
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
        AudioArtifact? take = CanonicalAudioArtifactSelector.Select(artifacts);
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

    private async Task SaveRecordingTranscriptCoreAsync(
        RecordingCardViewModel card)
    {
        string text = card.TranscriptText.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        TranscriptRevision edited = CreateTranscriptRevision(
            card.Id,
            card.TranscriptRevisionId,
            TranscriptRevisionKind.UserEdited,
            text,
            "user",
            null,
            isCurrent: true);
        await _recordings.AddTranscriptRevisionAsync(edited).ConfigureAwait(true);
        card.AcceptSavedTranscript(edited);
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

    private async Task LoadTrainerVoicesAsync(bool force = false)
    {
        if (!force
            && _trainerVoices.Count > 0
            && string.Equals(
                _trainerVoiceLanguageId,
                _languages.DialogLanguage.Id,
                StringComparison.Ordinal))
        {
            return;
        }

        IReadOnlyList<SpeechVoice> voices = await _synthesis
            .GetVoicesAsync()
            .ConfigureAwait(true);
        _trainerVoices.Clear();
        TrainerVoiceNames.Clear();
        _trainerVoiceLanguageId = _languages.DialogLanguage.Id;

        SpeechVoice? preferred = SpeechVoiceSelector.FindPreferred(
            voices,
            _languages.DialogLanguage);
        string localePrefix = _languages.DialogLanguage.Locale.Split('-', 2)[0];
        IEnumerable<SpeechVoice> suitable = voices.Where(voice =>
            string.Equals(
                voice.Locale,
                localePrefix,
                StringComparison.OrdinalIgnoreCase)
            || voice.Locale.StartsWith(
                localePrefix + "-",
                StringComparison.OrdinalIgnoreCase));
        if (_languages.DialogLanguage.Id == "be")
        {
            suitable = suitable.Concat(voices.Where(voice =>
                PlatformSpeechVoiceIds.IsPlatformVoice(voice.Id)
                && (string.Equals(
                        voice.Locale,
                        "ru",
                        StringComparison.OrdinalIgnoreCase)
                    || voice.Locale.StartsWith(
                        "ru-",
                        StringComparison.OrdinalIgnoreCase))));
        }

        IEnumerable<SpeechVoice> ordered = preferred is null
            ? suitable
            : new[] { preferred }.Concat(suitable);
        foreach (SpeechVoice voice in ordered.DistinctBy(voice => voice.Id))
        {
            _trainerVoices.Add(voice);
            TrainerVoiceNames.Add(voice.DisplayName);
        }

        SelectedTrainerVoiceIndex = preferred is null
            ? (_trainerVoices.Count == 0 ? -1 : 0)
            : Math.Max(0, _trainerVoices.IndexOf(preferred));
    }

    private void OnLocalizationChanged(object? sender, EventArgs eventArgs)
    {
        RefreshLocalizedChoiceLists();
        OnPropertyChanged(nameof(MeetingButtonText));
        OnPropertyChanged(nameof(TrayRecordingMenuText));
        OnPropertyChanged(nameof(TrainerRecordingButtonText));
        OnPropertyChanged(nameof(TrainerTakeStatusText));
    }

    private void RefreshLocalizedChoiceLists()
    {
        UpdateLocalizedChoices(
            ImprovementModeNames,
            [
                _localization.Get("CorrectOnly"),
                _localization.Get("NaturalSpoken"),
                _localization.Get("ClearConcise"),
            ]);
        UpdateLocalizedChoices(
            TrainerSpeedNames,
            [
                _localization.Get("SpeedRelaxed"),
                _localization.Get("SpeedNatural"),
                _localization.Get("SpeedFocused"),
            ]);
    }

    private static void UpdateLocalizedChoices(
        ObservableCollection<string> target,
        IReadOnlyList<string> values)
    {
        if (target.Count != values.Count)
        {
            target.Clear();
            foreach (string value in values)
            {
                target.Add(value);
            }

            return;
        }

        for (int index = 0; index < values.Count; index++)
        {
            if (!string.Equals(target[index], values[index], StringComparison.Ordinal))
            {
                target[index] = values[index];
            }
        }
    }

    private void OnLanguagePreferencesChanged(object? sender, EventArgs eventArgs)
    {
        if (!_workspaceInitialized
            || string.Equals(
                _trainerVoiceLanguageId,
                _languages.DialogLanguage.Id,
                StringComparison.Ordinal))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(
            async () =>
            {
                try
                {
                    await LoadTrainerVoicesAsync(force: true)
                        .ConfigureAwait(true);
                }
                catch (Exception error) when (
                    error is InvalidOperationException
                        or IOException
                        or UnauthorizedAccessException)
                {
                    TrainerStatusMessage = error.Message;
                }
            });
    }

    public void Dispose()
    {
        Onboarding.Completed -= OnOnboardingCompleted;
        _localization.Changed -= OnLocalizationChanged;
        _languages.Changed -= OnLanguagePreferencesChanged;
        Runtime.PropertyChanged -= OnRuntimePropertyChanged;
        _recordingCoordinator.LibraryChanged -= OnLibraryChanged;
        _speechProcessing.LibraryChanged -= OnLibraryChanged;
        _playback.StateChanged -= OnPlaybackStateChanged;
        _workspaceInitializationGate.Dispose();
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
