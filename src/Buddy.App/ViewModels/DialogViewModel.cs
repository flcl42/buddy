using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
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

public sealed partial class DialogViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan[] AllowedPauseDurations =
    [
        TimeSpan.FromMilliseconds(750),
        TimeSpan.FromMilliseconds(1_100),
        TimeSpan.FromMilliseconds(1_500),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(12),
        TimeSpan.FromSeconds(15),
    ];

    private readonly DialogCoordinator _coordinator;
    private readonly IDialogRepository _dialogs;
    private readonly IRecordingRepository _recordings;
    private readonly IAudioPlaybackService _playback;
    private readonly ISpeechSynthesisService _synthesis;
    private readonly DialogSpeechCacheService _speechCache;
    private readonly IPhoneticTranscriptionService _phonetics;
    private readonly IWordDefinitionProvider _wordDefinitions;
    private readonly BuddyDataPaths _paths;
    private readonly BuddyRuntimeState _runtime;
    private readonly LanguagePreferences _languages;
    private readonly UiLocalizationService _localization;
    private readonly System.Timers.Timer _silenceProgressTimer;
    private Guid? _recordingId;
    private Guid? _playingArtifactId;
    private DialogPlaybackPurpose _playbackPurpose;
    private Guid? _playbackMessageId;
    private string? _playbackWord;
    private string? _dialogPlaybackPath;
    private int _busyOperationCount;
    private CancellationTokenSource? _wordLookupCancellation;
    private CancellationTokenSource? _pauseSettingCancellation;
    private CancellationTokenSource? _savedDialogLoadCancellation;
    private DialogMessageViewModel? _activeWordLookupMessage;
    private long _wordLookupGeneration;
    private TimeSpan _allowedPause = DialogTurnBoundaryDetector.DefaultAllowedPause;
    private TimeSpan _silenceProgressBase;
    private DateTimeOffset _silenceProgressObservedAt = DateTimeOffset.UtcNow;
    private bool _interpolateSilenceProgress;
    private bool _applyingPauseSelection;
    private bool _demoWordCardShown;
    private bool _initialized;
    private bool _disposed;
    private DialogPhase _currentPhase = DialogPhase.Idle;

    public DialogViewModel(
        DialogCoordinator coordinator,
        IDialogRepository dialogs,
        IRecordingRepository recordings,
        IAudioPlaybackService playback,
        ISpeechSynthesisService synthesis,
        DialogSpeechCacheService speechCache,
        IPhoneticTranscriptionService phonetics,
        IWordDefinitionProvider wordDefinitions,
        BuddyDataPaths paths,
        LanguagePreferences languages,
        UiLocalizationService localization,
        BuddyRuntimeState runtime)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _recordings = recordings ?? throw new ArgumentNullException(nameof(recordings));
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _synthesis = synthesis ?? throw new ArgumentNullException(nameof(synthesis));
        _speechCache = speechCache
            ?? throw new ArgumentNullException(nameof(speechCache));
        _phonetics = phonetics ?? throw new ArgumentNullException(nameof(phonetics));
        _wordDefinitions = wordDefinitions
            ?? throw new ArgumentNullException(nameof(wordDefinitions));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _languages = languages ?? throw new ArgumentNullException(nameof(languages));
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _coordinator.StateChanged += OnDialogStateChanged;
        _playback.StateChanged += OnPlaybackStateChanged;
        _runtime.PropertyChanged += OnRuntimePropertyChanged;
        _localization.Changed += OnLocalizationChanged;
        _silenceProgressTimer = new System.Timers.Timer(100)
        {
            AutoReset = true,
        };
        _silenceProgressTimer.Elapsed += OnSilenceProgressTimerElapsed;
        RefreshAllowedPauseOptions();
        StatusMessage = _localization.Get("DialogReadyStatus");
        SilenceCountdownText = string.Format(
            CultureInfo.CurrentCulture,
            _localization.Get("PauseToSendFormat"),
            FormatPause(_allowedPause));
        PhaseText = FormatPhase(_currentPhase);
    }

    public ObservableCollection<DialogMessageViewModel> Messages { get; } = [];

    public ObservableCollection<DialogMessageViewModel> SavedMessages { get; } = [];

    public ObservableCollection<string> AllowedPauseOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanFinish))]
    [NotifyPropertyChangedFor(nameof(CanSendNow))]
    [NotifyPropertyChangedFor(nameof(CanRetryAnswer))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLiveTranscript))]
    [NotifyPropertyChangedFor(nameof(CanSendNow))]
    public partial string LiveTranscript { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "Start a dialog when you are ready.";

    [ObservableProperty]
    public partial string PhaseText { get; set; } = "Ready";

    [ObservableProperty]
    public partial bool IsGeneratingReply { get; set; }

    [ObservableProperty]
    public partial bool IsSavedDialogOpen { get; set; }

    [ObservableProperty]
    public partial bool IsSavedDialogLoading { get; set; }

    [ObservableProperty]
    public partial bool HasSavedDialogMessages { get; set; }

    [ObservableProperty]
    public partial string SavedDialogTitle { get; set; } = "Saved dialog";

    [ObservableProperty]
    public partial string SavedDialogMetadata { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SavedDialogStatus { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInactive))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanFinish))]
    [NotifyPropertyChangedFor(nameof(CanSendNow))]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFinish))]
    [NotifyPropertyChangedFor(nameof(FinishButtonText))]
    public partial bool IsFinishing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetryAnswer))]
    public partial bool RetryAvailable { get; set; }

    [ObservableProperty]
    public partial string? OperationMessage { get; set; }

    [ObservableProperty]
    public partial int SelectedPauseIndex { get; set; } = 4;

    [ObservableProperty]
    public partial double SilenceProgress { get; set; }

    [ObservableProperty]
    public partial string SilenceCountdownText { get; set; } =
        "Pause to send automatically after 3 seconds.";

    [ObservableProperty]
    public partial bool IsSilenceCountdownVisible { get; set; }

    [ObservableProperty]
    public partial bool CanPostponeSending { get; set; }

    public bool HasLiveTranscript => !string.IsNullOrWhiteSpace(LiveTranscript);

    public bool IsInactive => !IsActive;

    public bool CanStart => !IsActive && !IsBusy && !_runtime.IsRecording;

    public bool CanFinish => IsActive && !IsFinishing;

    public string FinishButtonText => IsFinishing
        ? _localization.Get("Saving")
        : _localization.Get("FinishSave");

    public bool CanSendNow => IsActive && !IsBusy && HasLiveTranscript;

    public bool CanRetryAnswer => IsActive && !IsBusy && RetryAvailable;

    partial void OnSelectedPauseIndexChanged(int value)
    {
        if (_applyingPauseSelection
            || value < 0
            || value >= AllowedPauseDurations.Length)
        {
            return;
        }

        _pauseSettingCancellation?.Cancel();
        _pauseSettingCancellation?.Dispose();
        _pauseSettingCancellation = new CancellationTokenSource();
        _ = SaveAllowedPauseAsync(
            AllowedPauseDurations[value],
            _pauseSettingCancellation.Token);
    }

    partial void OnCanPostponeSendingChanged(bool value)
    {
        KeepTalkingCommand.NotifyCanExecuteChanged();
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _coordinator.InitializeAsync().ConfigureAwait(true);
        ApplySnapshot(_coordinator.Snapshot);
        _initialized = true;
    }

    [RelayCommand]
    private async Task OpenSavedDialogAsync(RecordingCardViewModel recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        if (!recording.IsDialog)
        {
            return;
        }

        bool ownsSavedPlayback = _playbackMessageId.HasValue
            && SavedMessages.Any(message => message.Id == _playbackMessageId.Value);
        if (ownsSavedPlayback)
        {
            await StopDialogPlaybackSafelyAsync(
                    "Previous saved-dialog audio could not be stopped")
                .ConfigureAwait(true);
        }

        if (_activeWordLookupMessage is not null
            && SavedMessages.Contains(_activeWordLookupMessage))
        {
            CancelActiveWordLookup(dismiss: false);
            _activeWordLookupMessage = null;
        }

        _savedDialogLoadCancellation?.Cancel();
        _savedDialogLoadCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _savedDialogLoadCancellation = cancellation;

        IsSavedDialogOpen = true;
        IsSavedDialogLoading = true;
        OperationMessage = null;
        HasSavedDialogMessages = false;
        SavedDialogTitle = recording.Title;
        SavedDialogMetadata =
            $"{recording.DateText} · {recording.DurationText} · saved locally";
        SavedDialogStatus = "Loading the complete dialog…";
        SavedMessages.Clear();

        try
        {
            DialogSession? session = await _dialogs
                .GetSessionByRecordingIdAsync(recording.Id, cancellation.Token)
                .ConfigureAwait(true);
            if (session is null)
            {
                SavedDialogStatus =
                    "No dialog transcript is linked to this recording.";
                return;
            }

            Task<IReadOnlyList<DialogMessage>> messagesTask = _dialogs
                .GetMessagesAsync(session.Id, cancellation.Token);
            Task<IReadOnlyDictionary<Guid, DialogPronunciationAssessment>>
                pronunciationsTask = _dialogs.GetPronunciationAssessmentsAsync(
                    session.Id,
                    cancellation.Token);
            await Task.WhenAll(messagesTask, pronunciationsTask)
                .ConfigureAwait(true);

            IReadOnlyList<DialogMessage> messages = await messagesTask
                .ConfigureAwait(true);
            IReadOnlyDictionary<Guid, DialogPronunciationAssessment>
                pronunciations = await pronunciationsTask.ConfigureAwait(true);
            foreach (DialogMessage message in messages)
            {
                pronunciations.TryGetValue(
                    message.Id,
                    out DialogPronunciationAssessment? pronunciation);
                SavedMessages.Add(
                    new DialogMessageViewModel(
                        message,
                        isPlaying: false,
                        pronunciation,
                        recording.Id));
            }

            HasSavedDialogMessages = SavedMessages.Count > 0;
            SavedDialogStatus = HasSavedDialogMessages
                ? $"{SavedMessages.Count} saved turn"
                    + (SavedMessages.Count == 1 ? string.Empty : "s")
                    + " · click any word for pronunciation and meaning"
                : "This recording does not contain a completed dialog turn yet.";
            UpdatePlaybackMessages();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error) when (
            error is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or InvalidDataException
                or System.Data.Common.DbException)
        {
            SavedDialogStatus = $"The saved dialog could not be opened: {error.Message}";
        }
        finally
        {
            if (ReferenceEquals(_savedDialogLoadCancellation, cancellation))
            {
                IsSavedDialogLoading = false;
                _savedDialogLoadCancellation.Dispose();
                _savedDialogLoadCancellation = null;
            }
        }
    }

    [RelayCommand]
    private async Task CloseSavedDialogAsync()
    {
        _savedDialogLoadCancellation?.Cancel();
        _savedDialogLoadCancellation?.Dispose();
        _savedDialogLoadCancellation = null;

        bool ownsPlayback = _playbackMessageId.HasValue
            && SavedMessages.Any(message => message.Id == _playbackMessageId.Value);
        if (ownsPlayback)
        {
            await StopDialogPlaybackSafelyAsync(
                    "Saved-dialog audio could not be stopped")
                .ConfigureAwait(true);
        }

        if (_activeWordLookupMessage is not null
            && SavedMessages.Contains(_activeWordLookupMessage))
        {
            CancelActiveWordLookup(dismiss: false);
            _activeWordLookupMessage = null;
        }

        IsSavedDialogOpen = false;
        IsSavedDialogLoading = false;
        HasSavedDialogMessages = false;
        SavedMessages.Clear();
    }

    public async Task FinishForShutdownAsync()
    {
        try
        {
            await _coordinator.FinishAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (
            error is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or InvalidDataException
                or LanguageProviderException
                or LocalModelNotInstalledException
                or System.Runtime.InteropServices.COMException)
        {
            OperationMessage =
                $"The microphone was stopped safely, but dialog processing did not finish: "
                + error.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartDialogAsync()
    {
        BeginBusyOperation();
        OperationMessage = null;
        try
        {
            await _coordinator.StartAsync().ConfigureAwait(true);
        }
        catch (LocalModelNotInstalledException error)
        {
            OperationMessage =
                $"{error.Message} Open Settings and download the verified model.";
        }
        catch (Exception error) when (
            error is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or LanguageProviderException
                or System.Runtime.InteropServices.COMException)
        {
            OperationMessage = error.Message;
        }
        finally
        {
            EndBusyOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanFinish))]
    private async Task FinishDialogAsync()
    {
        IsFinishing = true;
        BeginBusyOperation();
        OperationMessage = null;
        try
        {
            await _coordinator.FinishAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (
            error is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or InvalidDataException
                or LanguageProviderException
                or LocalModelNotInstalledException
                or System.Runtime.InteropServices.COMException)
        {
            OperationMessage = error.Message;
        }
        finally
        {
            EndBusyOperation();
            IsFinishing = false;
            NotifyCommandStates();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSendNow))]
    private async Task SendNowAsync()
    {
        BeginBusyOperation();
        OperationMessage = null;
        try
        {
            await _coordinator.SendNowAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (
            error is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or InvalidDataException
                or LocalModelNotInstalledException)
        {
            OperationMessage = error.Message;
        }
        finally
        {
            EndBusyOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPostponeSending))]
    private void KeepTalking()
    {
        if (!_coordinator.PostponePendingTurn())
        {
            return;
        }

        _silenceProgressBase = TimeSpan.Zero;
        _silenceProgressObservedAt = DateTimeOffset.UtcNow;
        _interpolateSilenceProgress = false;
        _silenceProgressTimer.Stop();
        SilenceProgress = 0;
        SilenceCountdownText = string.Format(
            CultureInfo.CurrentCulture,
            _localization.Get("CountdownResetFormat"),
            FormatPause(_allowedPause));
    }

    [RelayCommand(CanExecute = nameof(CanRetryAnswer))]
    private async Task RetryAnswerAsync()
    {
        BeginBusyOperation();
        OperationMessage = null;
        try
        {
            await _coordinator.RetryAnswerAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (
            error is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or InvalidDataException
                or LanguageProviderException)
        {
            OperationMessage = error.Message;
        }
        finally
        {
            EndBusyOperation();
        }
    }

    [RelayCommand]
    private async Task PlayMessageAudioAsync(DialogMessageViewModel message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.MessageAudioState == AudioTransportState.Preparing)
        {
            return;
        }

        DialogPlaybackPurpose purpose = message.IsUser
            ? DialogPlaybackPurpose.UserReply
            : DialogPlaybackPurpose.Answer;
        if (message.MessageAudioState == AudioTransportState.Paused
            && IsOwnedPlayback(purpose, message.Id))
        {
            await ResumeDialogPlaybackSafelyAsync(
                    "Audio could not be resumed")
                .ConfigureAwait(true);
            return;
        }

        if (message.IsUser)
        {
            await StartUserReplyPlaybackAsync(message).ConfigureAwait(true);
            return;
        }

        Guid? recordingId = message.RecordingId ?? _recordingId;
        if (!message.AudioArtifactId.HasValue || !recordingId.HasValue)
        {
            return;
        }

        try
        {
            OperationMessage = null;
            IReadOnlyList<AudioArtifact> artifacts = await _recordings
                .GetAudioArtifactsAsync(recordingId.Value)
                .ConfigureAwait(true);
            AudioArtifact? artifact = artifacts.FirstOrDefault(
                item => item.Id == message.AudioArtifactId.Value);
            if (artifact is null)
            {
                OperationMessage = "The generated answer audio is no longer available.";
                return;
            }

            if (NeedsAnswerAudioRefresh(message, artifact))
            {
                message.MessageAudioState = AudioTransportState.Preparing;
                try
                {
                    artifact = await RefreshAnswerAudioAsync(message, artifact)
                        .ConfigureAwait(true);
                }
                finally
                {
                    message.MessageAudioState = AudioTransportState.Idle;
                }
            }

            string path = _paths.ResolveRecordingArtifact(artifact.RelativePath);
            await StartDialogPlaybackAsync(
                    DialogPlaybackPurpose.Answer,
                    message,
                    path,
                    artifactId: artifact.Id)
                .ConfigureAwait(true);
        }
        catch (Exception error) when (
            error is IOException
                or InvalidOperationException
                or NotSupportedException
                or UnauthorizedAccessException
                or ArgumentException
                or LocalModelNotInstalledException)
        {
            OperationMessage = error.Message;
            if (message.MessageAudioState == AudioTransportState.Preparing)
            {
                message.MessageAudioState = AudioTransportState.Idle;
            }
        }
    }

    private async Task StartUserReplyPlaybackAsync(
        DialogMessageViewModel message)
    {
        if (!message.IsUser)
        {
            return;
        }

        message.MessageAudioState = AudioTransportState.Preparing;
        OperationMessage = null;
        try
        {
            string path = await _speechCache
                .GetOrCreateAsync(message.PlainText)
                .ConfigureAwait(true);
            await StartDialogPlaybackAsync(
                    DialogPlaybackPurpose.UserReply,
                    message,
                    path)
                .ConfigureAwait(true);
        }
        catch (Exception error) when (IsSpeechPlaybackFailure(error))
        {
            OperationMessage = $"Your reply could not be spoken: {error.Message}";
        }
        finally
        {
            if (message.MessageAudioState == AudioTransportState.Preparing)
            {
                message.MessageAudioState = AudioTransportState.Idle;
            }
        }
    }

    [RelayCommand]
    private async Task PlayWordAudioAsync(DialogMessageViewModel message)
    {
        ArgumentNullException.ThrowIfNull(message);
        string word = message.SelectedWord.Trim();
        if (!message.IsWordLookupVisible
            || word.Length == 0
            || message.WordAudioState == AudioTransportState.Preparing)
        {
            return;
        }

        if (message.WordAudioState == AudioTransportState.Paused
            && IsOwnedPlayback(
                DialogPlaybackPurpose.Word,
                message.Id,
                word))
        {
            await ResumeDialogPlaybackSafelyAsync(
                    "The word could not be resumed")
                .ConfigureAwait(true);
            return;
        }

        message.WordAudioState = AudioTransportState.Preparing;
        OperationMessage = null;
        try
        {
            string path = await _speechCache
                .GetOrCreateAsync(word)
                .ConfigureAwait(true);
            await StartDialogPlaybackAsync(
                    DialogPlaybackPurpose.Word,
                    message,
                    path,
                    word)
                .ConfigureAwait(true);
        }
        catch (Exception error) when (IsSpeechPlaybackFailure(error))
        {
            OperationMessage = $"The word could not be spoken: {error.Message}";
        }
        finally
        {
            if (message.WordAudioState == AudioTransportState.Preparing)
            {
                message.WordAudioState = AudioTransportState.Idle;
            }
        }
    }

    [RelayCommand]
    private async Task PauseDialogAudioAsync(DialogMessageViewModel message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!IsOwnedPlaybackForMessage(message.Id) || !_playback.IsPlaying)
        {
            return;
        }

        try
        {
            OperationMessage = null;
            await _playback.PauseAsync().ConfigureAwait(true);
            UpdatePlaybackMessages();
        }
        catch (Exception error) when (IsSpeechPlaybackFailure(error))
        {
            OperationMessage = $"Audio could not be paused: {error.Message}";
        }
    }

    [RelayCommand]
    private async Task StopDialogAudioAsync(DialogMessageViewModel message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!IsOwnedPlaybackForMessage(message.Id))
        {
            return;
        }

        await StopDialogPlaybackSafelyAsync("Audio could not be stopped")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestartDialogAudioAsync(DialogMessageViewModel message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!IsOwnedPlaybackForMessage(message.Id) || !_playback.IsPaused)
        {
            return;
        }

        try
        {
            OperationMessage = null;
            _coordinator.SuppressRecognitionForPlayback();
            await _playback.RestartAsync().ConfigureAwait(true);
            UpdatePlaybackMessages();
        }
        catch (Exception error) when (IsSpeechPlaybackFailure(error))
        {
            OperationMessage =
                $"Audio could not restart from the beginning: {error.Message}";
        }
    }

    private async Task<AudioArtifact> RefreshAnswerAudioAsync(
        DialogMessageViewModel message,
        AudioArtifact artifact)
    {
        string path = _paths.ResolveRecordingArtifact(artifact.RelativePath);
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The generated answer audio has no parent directory.");
        DialogAnswerDocument? answerDocument = await DialogAnswerDocumentStore
            .ReadAsync(directory, message.Id)
            .ConfigureAwait(true);
        string synthesisText = message.Text;
        bool usesSpeakerAwareAnswer = false;
        if (answerDocument is not null)
        {
            if (!string.Equals(
                answerDocument.DisplayMarkdown,
                message.Text.Trim(),
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The saved narration does not match the displayed answer.");
            }

            synthesisText = answerDocument.SpokenText;
            usesSpeakerAwareAnswer = true;
        }

        IReadOnlyList<SpeechVoice> voices = await _synthesis
            .GetVoicesAsync()
            .ConfigureAwait(true);
        string? savedVoiceId = GetGeneratorValue(artifact.Generator, "voice");
        SpeechVoice? voice = voices.FirstOrDefault(
                item => string.Equals(
                    item.Id,
                    savedVoiceId,
                    StringComparison.Ordinal))
            ?? SpeechVoiceSelector.FindPreferred(
                voices,
                _languages.DialogLanguage);
        if (voice is null)
        {
            throw new InvalidOperationException(
                "No local speaking voice is available to refresh this answer.");
        }

        SpeechSynthesisResult result = await _synthesis
            .SynthesizeAsync(
                synthesisText,
                path,
                new SpeechSynthesisOptions(voice.Id, 1.0f, []))
            .ConfigureAwait(true);
        FileInfo file = new(result.OutputPath);
        AudioArtifact refreshed = artifact with
        {
            Container = AudioContainer.Wave,
            SampleRate = result.SampleRate,
            Channels = result.Channels,
            Duration = result.Duration,
            ByteLength = file.Length,
            Sha256 = await ComputeSha256Async(result.OutputPath)
                .ConfigureAwait(true),
            Generator = $"{result.Model}; voice={result.VoiceId}; "
                + "text-normalization="
                + $"{MarkdownTextProcessor.SpeechNormalizationVersion}; "
                + $"synthesis={LocalSpeechSynthesisService.SynthesisVersion}; "
                + (usesSpeakerAwareAnswer
                    ? "answer-contract="
                        + $"{ConversationAnswerContract.SchemaVersion}; "
                        + "answer-document=saved; "
                    : string.Empty)
                + $"dialog-message={message.Id:D}",
            CreatedAt = DateTimeOffset.Now,
        };
        if (!await _recordings
                .UpdateAudioArtifactAsync(refreshed)
                .ConfigureAwait(true))
        {
            throw new InvalidOperationException(
                "The refreshed answer audio could not be saved.");
        }

        return refreshed;
    }

    private static bool NeedsAnswerAudioRefresh(
        DialogMessageViewModel message,
        AudioArtifact artifact)
    {
        if (artifact.Kind != AudioArtifactKind.DialogAssistant)
        {
            return false;
        }

        bool usesCurrentSynthesis = artifact.Generator?.Contains(
            $"synthesis={LocalSpeechSynthesisService.SynthesisVersion}",
            StringComparison.Ordinal) == true;
        bool usesCurrentTextNormalization = artifact.Generator?.Contains(
            $"text-normalization={MarkdownTextProcessor.SpeechNormalizationVersion}",
            StringComparison.Ordinal) == true;
        return !usesCurrentSynthesis
            || (!usesCurrentTextNormalization
                && !string.Equals(
                    MarkdownTextProcessor.ToSpeechText(message.Text),
                    message.Text.Trim(),
                    StringComparison.Ordinal));
    }

    private static string? GetGeneratorValue(string? generator, string key)
    {
        if (string.IsNullOrWhiteSpace(generator))
        {
            return null;
        }

        string prefix = $"{key}=";
        return generator.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
            .FirstOrDefault(
                part => part.StartsWith(prefix, StringComparison.Ordinal))
            ?[prefix.Length..]
            .Trim();
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
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [RelayCommand]
    private async Task LookupWordAsync(DialogWordLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Message);
        if (string.IsNullOrWhiteSpace(request.Word))
        {
            return;
        }

        string word = request.Word.Trim();
        if (request.Message.IsLookingUpWord(word))
        {
            return;
        }

        if (_playbackPurpose == DialogPlaybackPurpose.Word
            && (_playback.IsPlaying || _playback.IsPaused))
        {
            await StopDialogPlaybackSafelyAsync(
                    "The previous word could not be stopped")
                .ConfigureAwait(true);
        }

        CancelActiveWordLookup(dismiss: _activeWordLookupMessage is not null
            && !ReferenceEquals(_activeWordLookupMessage, request.Message));
        _activeWordLookupMessage = request.Message;
        if (request.Message.TryShowCachedWordLookup(word))
        {
            return;
        }

        request.Message.BeginWordLookup(word);
        CancellationTokenSource cancellation = new();
        _wordLookupCancellation = cancellation;
        long generation = checked(++_wordLookupGeneration);
        Task phoneticTask = LoadWordPhoneticAsync(
            request.Message,
            word,
            generation,
            cancellation.Token);
        Task definitionTask = LoadWordDefinitionAsync(
            request.Message,
            word,
            generation,
            cancellation.Token);
        await Task.WhenAll(phoneticTask, definitionTask).ConfigureAwait(true);

        if (generation == _wordLookupGeneration
            && ReferenceEquals(_wordLookupCancellation, cancellation))
        {
            _wordLookupCancellation.Dispose();
            _wordLookupCancellation = null;
        }
    }

    [RelayCommand]
    private async Task DismissWordLookupAsync(DialogMessageViewModel message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (_playbackPurpose == DialogPlaybackPurpose.Word
            && _playbackMessageId == message.Id
            && (_playback.IsPlaying || _playback.IsPaused))
        {
            await StopDialogPlaybackSafelyAsync(
                    "The word could not be stopped")
                .ConfigureAwait(true);
        }

        if (ReferenceEquals(_activeWordLookupMessage, message))
        {
            CancelActiveWordLookup(dismiss: false);
            _activeWordLookupMessage = null;
        }

        message.DismissWordLookupCommand.Execute(null);
    }

    private async Task LoadWordPhoneticAsync(
        DialogMessageViewModel message,
        string word,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            string phonetic = await _phonetics
                .TranscribeAsync(
                    word,
                    _languages.DialogLanguage.Locale,
                    cancellationToken)
                .ConfigureAwait(true);
            if (IsCurrentWordLookup(message, generation))
            {
                message.ApplyWordPhonetic(word, phonetic);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (IsCurrentWordLookup(message, generation))
            {
                message.ApplyWordPhoneticError(
                    word,
                    $"Phonetics unavailable: {error.Message}");
            }
        }
    }

    private async Task LoadWordDefinitionAsync(
        DialogMessageViewModel message,
        string word,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            WordDefinitionResult definition = await _wordDefinitions
                .DefineAsync(
                    new WordDefinitionRequest(
                        word,
                        message.PlainText,
                        _languages.DialogLanguage.Locale),
                    cancellationToken)
                .ConfigureAwait(true);
            if (IsCurrentWordLookup(message, generation))
            {
                message.ApplyWordDefinition(word, definition);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (IsCurrentWordLookup(message, generation))
            {
                message.ApplyWordDefinitionError(
                    word,
                    $"Meaning unavailable: {error.Message}");
            }
        }
    }

    private bool IsCurrentWordLookup(
        DialogMessageViewModel message,
        long generation)
    {
        return generation == _wordLookupGeneration
            && ReferenceEquals(message, _activeWordLookupMessage);
    }

    private void CancelActiveWordLookup(bool dismiss)
    {
        _wordLookupGeneration++;
        _wordLookupCancellation?.Cancel();
        _wordLookupCancellation?.Dispose();
        _wordLookupCancellation = null;
        if (_activeWordLookupMessage is not null)
        {
            _activeWordLookupMessage.CancelWordLookup();
            if (dismiss)
            {
                _activeWordLookupMessage.DismissWordLookupCommand.Execute(null);
            }
        }
    }

    private void OnDialogStateChanged(
        object? sender,
        DialogStateChangedEventArgs eventArgs)
    {
        MainThread.BeginInvokeOnMainThread(
            () => ApplySnapshot(eventArgs.Snapshot));
    }

    private void ApplySnapshot(DialogSnapshot snapshot)
    {
        _recordingId = snapshot.Session?.RecordingId;
        if (snapshot.Phase == DialogPhase.Speaking
            && (_playback.IsPlaying || _playback.IsPaused)
            && !IsOwnedPlaybackActive())
        {
            DialogMessage? automaticAnswer = snapshot.Messages
                .LastOrDefault(
                    message => message.Role == DialogMessageRole.Assistant
                        && message.AudioArtifactId.HasValue
                        && PathRepresentsArtifact(
                            _playback.LoadedPath,
                            message.AudioArtifactId.Value));
            if (automaticAnswer is not null)
            {
                _playbackPurpose = DialogPlaybackPurpose.Answer;
                _playbackMessageId = automaticAnswer.Id;
                _playbackWord = null;
                _dialogPlaybackPath = _playback.LoadedPath;
                _playingArtifactId = automaticAnswer.AudioArtifactId;
            }
        }

        IsActive = snapshot.IsActive;
        LiveTranscript = snapshot.LiveTranscript;
        StatusMessage = snapshot.StatusMessage;
        _currentPhase = snapshot.Phase;
        PhaseText = FormatPhase(snapshot.Phase);
        IsGeneratingReply = snapshot.Phase == DialogPhase.Thinking;
        RetryAvailable = snapshot.CanRetryAnswer;
        ApplySilenceSnapshot(snapshot);

        DialogMessageCollectionReconciler.Reconcile(
            Messages,
            snapshot.Messages,
            snapshot.Pronunciations,
            snapshot.Session?.RecordingId);
        ApplyDemoWordCardIfRequested();
        UpdatePlaybackMessages();

        NotifyCommandStates();
    }

    private void ApplyDemoWordCardIfRequested()
    {
        if (_demoWordCardShown)
        {
            return;
        }

        string? word = Environment.GetEnvironmentVariable(
            "BUDDY_DEMO_WORD_CARD");
        string? phonetic = Environment.GetEnvironmentVariable(
            "BUDDY_DEMO_WORD_PHONETIC");
        string? definition = Environment.GetEnvironmentVariable(
            "BUDDY_DEMO_WORD_DEFINITION");
        if (string.IsNullOrWhiteSpace(word)
            || string.IsNullOrWhiteSpace(phonetic)
            || string.IsNullOrWhiteSpace(definition))
        {
            return;
        }

        DialogMessageViewModel? message = Messages.LastOrDefault(
            candidate => candidate.IsAssistant
                && candidate.PlainText.Contains(
                    word,
                    StringComparison.OrdinalIgnoreCase));
        if (message is null)
        {
            return;
        }

        string? partOfSpeech = Environment.GetEnvironmentVariable(
            "BUDDY_DEMO_WORD_PART_OF_SPEECH");
        message.BeginWordLookup(word);
        message.ApplyWordPhonetic(word, phonetic);
        message.ApplyWordDefinition(
            word,
            new WordDefinitionResult(
                word,
                partOfSpeech,
                definition,
                "demo",
                "website",
                TimeSpan.Zero));
        _activeWordLookupMessage = message;
        _demoWordCardShown = true;
    }

    private async Task SaveAllowedPauseAsync(
        TimeSpan allowedPause,
        CancellationToken cancellationToken)
    {
        try
        {
            await _coordinator
                .SetAllowedPauseAsync(allowedPause, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (
            error is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentOutOfRangeException
                or System.Data.Common.DbException)
        {
            OperationMessage =
                $"The allowed pause could not be saved: {error.Message}";
            ApplySilenceSnapshot(_coordinator.Snapshot);
        }
    }

    private void ApplySilenceSnapshot(DialogSnapshot snapshot)
    {
        _allowedPause = snapshot.AllowedPause;
        _silenceProgressBase = snapshot.TrailingSilence;
        _silenceProgressObservedAt = snapshot.SilenceObservedAt;
        _interpolateSilenceProgress = snapshot.CanPostponeTurn
            && snapshot.TrailingSilence > TimeSpan.Zero
            && snapshot.Phase == DialogPhase.Listening;

        int pauseIndex = FindPauseIndex(snapshot.AllowedPause);
        if (SelectedPauseIndex != pauseIndex)
        {
            _applyingPauseSelection = true;
            try
            {
                SelectedPauseIndex = pauseIndex;
            }
            finally
            {
                _applyingPauseSelection = false;
            }
        }

        CanPostponeSending = snapshot.CanPostponeTurn;
        IsSilenceCountdownVisible = snapshot.IsActive
            && snapshot.Phase is DialogPhase.Listening
                or DialogPhase.Transcribing;
        UpdateSilencePresentation(
            snapshot.Phase == DialogPhase.Transcribing
                ? snapshot.AllowedPause
                : snapshot.TrailingSilence);

        if (_interpolateSilenceProgress)
        {
            _silenceProgressTimer.Start();
        }
        else
        {
            _silenceProgressTimer.Stop();
        }
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs eventArgs)
    {
        MainThread.BeginInvokeOnMainThread(
            () =>
            {
                if (_playback.LastError is not null)
                {
                    OperationMessage =
                        $"Audio playback stopped: {_playback.LastError.Message}";
                }

                UpdatePlaybackMessages();
            });
    }

    private void OnRuntimePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(BuddyRuntimeState.Mode))
        {
            MainThread.BeginInvokeOnMainThread(NotifyCommandStates);
        }
    }

    private void OnSilenceProgressTimerElapsed(
        object? sender,
        System.Timers.ElapsedEventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(
            () =>
            {
                if (_disposed || !_interpolateSilenceProgress)
                {
                    return;
                }

                TimeSpan wallElapsed = DateTimeOffset.UtcNow
                    - _silenceProgressObservedAt;
                TimeSpan estimated = _silenceProgressBase
                    + (wallElapsed > TimeSpan.Zero
                        ? wallElapsed
                        : TimeSpan.Zero);
                UpdateSilencePresentation(estimated);
                if (SilenceProgress >= 1)
                {
                    _silenceProgressTimer.Stop();
                }
            });
    }

    private void UpdateSilencePresentation(TimeSpan elapsed)
    {
        double progress = _allowedPause <= TimeSpan.Zero
            ? 0
            : Math.Clamp(
                elapsed.TotalMilliseconds / _allowedPause.TotalMilliseconds,
                0,
                1);
        SilenceProgress = progress;
        if (progress >= 1)
        {
            SilenceCountdownText = _localization.Get("PauseComplete");
            return;
        }

        TimeSpan remaining = _allowedPause - elapsed;
        SilenceCountdownText = string.Format(
            CultureInfo.CurrentCulture,
            _localization.Get(
                elapsed > TimeSpan.Zero
                    ? "SendingInFormat"
                    : "PauseToSendFormat"),
            FormatPause(
                elapsed > TimeSpan.Zero ? remaining : _allowedPause));
    }

    private static int FindPauseIndex(TimeSpan pause)
    {
        int bestIndex = 0;
        double bestDifference = double.MaxValue;
        for (int index = 0; index < AllowedPauseDurations.Length; index++)
        {
            double difference = Math.Abs(
                (AllowedPauseDurations[index] - pause).TotalMilliseconds);
            if (difference < bestDifference)
            {
                bestDifference = difference;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private string FormatPause(TimeSpan pause)
    {
        double seconds = Math.Max(0, pause.TotalSeconds);
        return $"{seconds:0.#} {_localization.Get("SecondsShort")}";
    }

    private void OnLocalizationChanged(object? sender, EventArgs eventArgs)
    {
        RefreshAllowedPauseOptions();
        OnPropertyChanged(nameof(FinishButtonText));
        PhaseText = FormatPhase(_currentPhase);
        if (!IsActive && _currentPhase is DialogPhase.Idle)
        {
            StatusMessage = _localization.Get("DialogReadyStatus");
        }

        UpdateSilencePresentation(_silenceProgressBase);
    }

    private void RefreshAllowedPauseOptions()
    {
        string[] values = AllowedPauseDurations
            .Select((pause, index) => index switch
            {
                0 => $"{FormatPause(pause)} · {_localization.Get("VeryQuick")}",
                1 => $"{FormatPause(pause)} · {_localization.Get("Quick")}",
                _ => FormatPause(pause),
            })
            .ToArray();
        if (AllowedPauseOptions.Count != values.Length)
        {
            AllowedPauseOptions.Clear();
            foreach (string value in values)
            {
                AllowedPauseOptions.Add(value);
            }

            return;
        }

        for (int index = 0; index < values.Length; index++)
        {
            if (!string.Equals(
                    AllowedPauseOptions[index],
                    values[index],
                    StringComparison.Ordinal))
            {
                AllowedPauseOptions[index] = values[index];
            }
        }
    }

    private void UpdatePlaybackMessages()
    {
        bool isActive = IsOwnedPlaybackActive();
        AudioTransportState state = !isActive
            ? AudioTransportState.Idle
            : _playback.IsPlaying
                ? AudioTransportState.Playing
                : AudioTransportState.Paused;
        if (!isActive && !_playback.IsPlaying && !_playback.IsPaused)
        {
            ClearPlaybackOwner();
        }

        foreach (DialogMessageViewModel message in Messages.Concat(SavedMessages))
        {
            if (message.MessageAudioState != AudioTransportState.Preparing)
            {
                bool ownsMainAudio = isActive
                    && message.Id == _playbackMessageId
                    && (_playbackPurpose == DialogPlaybackPurpose.UserReply
                        || (_playbackPurpose == DialogPlaybackPurpose.Answer
                            && message.AudioArtifactId == _playingArtifactId));
                message.MessageAudioState = ownsMainAudio
                    ? state
                    : AudioTransportState.Idle;
            }

            if (message.WordAudioState != AudioTransportState.Preparing)
            {
                bool ownsWordAudio = isActive
                    && _playbackPurpose == DialogPlaybackPurpose.Word
                    && message.Id == _playbackMessageId
                    && string.Equals(
                        message.SelectedWord,
                        _playbackWord,
                        StringComparison.OrdinalIgnoreCase);
                message.WordAudioState = ownsWordAudio
                    ? state
                    : AudioTransportState.Idle;
            }
        }
    }

    private async Task StartDialogPlaybackAsync(
        DialogPlaybackPurpose purpose,
        DialogMessageViewModel message,
        string path,
        string? word = null,
        Guid? artifactId = null)
    {
        _coordinator.SuppressRecognitionForPlayback();
        await _playback.LoadAsync(path).ConfigureAwait(true);
        SetPlaybackOwner(
            purpose,
            message,
            path,
            artifactId,
            word);
        await _playback.PlayAsync().ConfigureAwait(true);
        if (purpose == DialogPlaybackPurpose.Word)
        {
            message.WordAudioState = AudioTransportState.Idle;
        }
        else
        {
            message.MessageAudioState = AudioTransportState.Idle;
        }

        UpdatePlaybackMessages();
    }

    private async Task ResumeDialogPlaybackSafelyAsync(string errorPrefix)
    {
        try
        {
            OperationMessage = null;
            _coordinator.SuppressRecognitionForPlayback();
            await _playback.PlayAsync().ConfigureAwait(true);
            UpdatePlaybackMessages();
        }
        catch (Exception error) when (IsSpeechPlaybackFailure(error))
        {
            OperationMessage = $"{errorPrefix}: {error.Message}";
        }
    }

    private async Task StopDialogPlaybackAsync()
    {
        try
        {
            if (_playback.LoadedPath is not null)
            {
                await _playback.StopAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            ClearPlaybackOwner();
            UpdatePlaybackMessages();
        }
    }

    private async Task StopDialogPlaybackSafelyAsync(string errorPrefix)
    {
        try
        {
            await StopDialogPlaybackAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (IsSpeechPlaybackFailure(error))
        {
            OperationMessage = $"{errorPrefix}: {error.Message}";
        }
    }

    private void SetPlaybackOwner(
        DialogPlaybackPurpose purpose,
        DialogMessageViewModel message,
        string path,
        Guid? artifactId = null,
        string? word = null)
    {
        _playbackPurpose = purpose;
        _playbackMessageId = message.Id;
        _playbackWord = word;
        _dialogPlaybackPath = Path.GetFullPath(path);
        _playingArtifactId = artifactId;
    }

    private void ClearPlaybackOwner()
    {
        _playbackPurpose = DialogPlaybackPurpose.None;
        _playbackMessageId = null;
        _playbackWord = null;
        _dialogPlaybackPath = null;
        _playingArtifactId = null;
    }

    private bool IsOwnedPlayback(
        DialogPlaybackPurpose purpose,
        Guid messageId,
        string? word = null)
    {
        return IsOwnedPlaybackActive()
            && _playbackPurpose == purpose
            && _playbackMessageId == messageId
            && (word is null
                || string.Equals(
                    _playbackWord,
                    word,
                    StringComparison.OrdinalIgnoreCase));
    }

    private bool IsOwnedPlaybackForMessage(Guid messageId)
    {
        return IsOwnedPlaybackActive()
            && _playbackMessageId == messageId;
    }

    private bool IsOwnedPlaybackActive()
    {
        return (_playback.IsPlaying || _playback.IsPaused)
            && PathsEqual(_dialogPlaybackPath, _playback.LoadedPath);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathRepresentsArtifact(string? path, Guid artifactId)
    {
        return !string.IsNullOrWhiteSpace(path)
            && Path.GetFileNameWithoutExtension(path).EndsWith(
                artifactId.ToString("N"),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSpeechPlaybackFailure(Exception error)
    {
        return error is IOException
            or InvalidOperationException
            or InvalidDataException
            or NotSupportedException
            or UnauthorizedAccessException
            or ArgumentException
            or LocalModelNotInstalledException
            or System.Runtime.InteropServices.COMException;
    }

    private void BeginBusyOperation()
    {
        _busyOperationCount = checked(_busyOperationCount + 1);
        IsBusy = true;
        NotifyCommandStates();
    }

    private void EndBusyOperation()
    {
        if (_busyOperationCount == 0)
        {
            throw new InvalidOperationException(
                "A dialog operation ended without a matching start.");
        }

        _busyOperationCount--;
        IsBusy = _busyOperationCount > 0;
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanFinish));
        OnPropertyChanged(nameof(CanSendNow));
        OnPropertyChanged(nameof(CanRetryAnswer));
        StartDialogCommand.NotifyCanExecuteChanged();
        FinishDialogCommand.NotifyCanExecuteChanged();
        SendNowCommand.NotifyCanExecuteChanged();
        RetryAnswerCommand.NotifyCanExecuteChanged();
        KeepTalkingCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _silenceProgressTimer.Stop();
        _silenceProgressTimer.Elapsed -= OnSilenceProgressTimerElapsed;
        _silenceProgressTimer.Dispose();
        _pauseSettingCancellation?.Cancel();
        _pauseSettingCancellation?.Dispose();
        _savedDialogLoadCancellation?.Cancel();
        _savedDialogLoadCancellation?.Dispose();
        CancelActiveWordLookup(dismiss: false);
        _coordinator.StateChanged -= OnDialogStateChanged;
        _playback.StateChanged -= OnPlaybackStateChanged;
        _runtime.PropertyChanged -= OnRuntimePropertyChanged;
        _localization.Changed -= OnLocalizationChanged;
    }

    private string FormatPhase(DialogPhase phase)
    {
        return phase switch
        {
            DialogPhase.Listening => _localization.Get("PhaseListening"),
            DialogPhase.Transcribing => _localization.Get("PhaseTranscribing"),
            DialogPhase.Thinking => _localization.Get("PhaseThinking"),
            DialogPhase.Synthesizing => _localization.Get("PhasePreparingVoice"),
            DialogPhase.Speaking => _localization.Get("PhaseSpeaking"),
            DialogPhase.Finishing => _localization.Get("Saving"),
            DialogPhase.Completed => _localization.Get("PhaseSaved"),
            DialogPhase.Error => _localization.Get("PhaseNeedsAttention"),
            _ => _localization.Get("PhaseReady"),
        };
    }

    private enum DialogPlaybackPurpose
    {
        None = 0,
        Answer = 1,
        UserReply = 2,
        Word = 3,
    }
}
