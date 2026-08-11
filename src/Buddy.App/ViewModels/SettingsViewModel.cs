using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Buddy.App.Services;
using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using Buddy.Language;
using Buddy.Persistence;
using Buddy.Speech;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Buddy.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    internal const string ProxySecretKey = BuddyProxyLanguageProvider.SecretKey;
    internal const string DeepSeekSecretKey = DeepSeekLanguageProvider.SecretKey;
    internal const string KimiSecretKey = "provider.kimi.api-key";
    internal const string OpenAiSecretKey = "provider.openai.api-key";

    private readonly ISecretStore _secrets;
    private readonly ILocalModelManager _models;
    private readonly IAudioCaptureService _capture;
    private readonly IAudioInputTestService _inputTest;
    private readonly IAudioPlaybackService _playback;
    private readonly IAppSettingsStore _settings;
    private readonly LanguageProviderRouter _languageProviders;
    private readonly IQwenModelRuntime _qwenRuntime;
    private readonly IQwenModelInstaller _qwenInstaller;
    private readonly LocalSetupCoordinator _localSetup;
    private readonly BuddyProxyClientConfiguration _proxyConfiguration;
    private readonly RecordingCoordinator _recordingCoordinator;
    private readonly SpeechProcessingCoordinator _speechProcessing;
    private readonly LanguagePreferences _languages;
    private readonly UiLocalizationService _localization;
    private IReadOnlyList<AudioInputDevice> _microphones = [];
    private IReadOnlyList<AudioOutputDevice> _outputs = [];

    public SettingsViewModel(
        BuddyDataPaths paths,
        ISecretStore secrets,
        ILocalModelManager models,
        IAudioCaptureService capture,
        IAudioInputTestService inputTest,
        IAudioPlaybackService playback,
        IAppSettingsStore settings,
        LanguageProviderRouter languageProviders,
        IQwenModelRuntime qwenRuntime,
        IQwenModelInstaller qwenInstaller,
        LocalSetupCoordinator localSetup,
        BuddyProxyClientConfiguration proxyConfiguration,
        RecordingCoordinator recordingCoordinator,
        SpeechProcessingCoordinator speechProcessing,
        LanguagePreferences languages,
        UiLocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _inputTest = inputTest ?? throw new ArgumentNullException(nameof(inputTest));
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _languageProviders = languageProviders
            ?? throw new ArgumentNullException(nameof(languageProviders));
        _qwenRuntime = qwenRuntime
            ?? throw new ArgumentNullException(nameof(qwenRuntime));
        _qwenInstaller = qwenInstaller
            ?? throw new ArgumentNullException(nameof(qwenInstaller));
        _localSetup = localSetup
            ?? throw new ArgumentNullException(nameof(localSetup));
        _proxyConfiguration = proxyConfiguration
            ?? throw new ArgumentNullException(nameof(proxyConfiguration));
        _recordingCoordinator = recordingCoordinator
            ?? throw new ArgumentNullException(nameof(recordingCoordinator));
        _speechProcessing = speechProcessing
            ?? throw new ArgumentNullException(nameof(speechProcessing));
        _languages = languages ?? throw new ArgumentNullException(nameof(languages));
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));
        StoragePath = paths.Root;
        QwenModelPath = _qwenRuntime.Options.ModelPath;
        RefreshLanguageNames();
        _localization.Changed += OnLocalizationChanged;
    }

    public string StoragePath { get; }

    public ObservableCollection<string> MicrophoneNames { get; } = [];

    public ObservableCollection<string> OutputNames { get; } = [];

    public ObservableCollection<string> LanguageProviderNames { get; } = [];

    public ObservableCollection<string> InterfaceLanguageNames { get; } = [];

    public ObservableCollection<string> DialogLanguageNames { get; } = [];

    public string QwenModelPath { get; }

    [ObservableProperty]
    public partial int SelectedLanguageProviderIndex { get; set; } = -1;

    [ObservableProperty]
    public partial int SelectedInterfaceLanguageIndex { get; set; } = -1;

    [ObservableProperty]
    public partial int SelectedDialogLanguageIndex { get; set; } = -1;

    [ObservableProperty]
    public partial string LanguageProviderDescription { get; set; } =
        "Checking the selected language provider…";

    [ObservableProperty]
    public partial string LanguageProviderStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string QwenModelStatus { get; set; } =
        "Checking Qwen 3.6 27B…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadQwen))]
    [NotifyPropertyChangedFor(nameof(CanUnloadQwen))]
    [NotifyCanExecuteChangedFor(nameof(LoadQwenCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnloadQwenCommand))]
    [NotifyPropertyChangedFor(nameof(QwenPrimaryButtonText))]
    public partial bool IsManagingQwen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadQwen))]
    [NotifyPropertyChangedFor(nameof(CanUnloadQwen))]
    [NotifyCanExecuteChangedFor(nameof(LoadQwenCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnloadQwenCommand))]
    [NotifyPropertyChangedFor(nameof(QwenPrimaryButtonText))]
    public partial bool IsQwenInstalled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadQwen))]
    [NotifyPropertyChangedFor(nameof(CanUnloadQwen))]
    [NotifyCanExecuteChangedFor(nameof(LoadQwenCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnloadQwenCommand))]
    [NotifyPropertyChangedFor(nameof(QwenPrimaryButtonText))]
    public partial bool IsQwenLoaded { get; set; }

    [ObservableProperty]
    public partial int SelectedMicrophoneIndex { get; set; } = -1;

    [ObservableProperty]
    public partial string MicrophoneStatus { get; set; } =
        "Checking available microphones…";

    [ObservableProperty]
    public partial double MicrophoneLevel { get; set; }

    [ObservableProperty]
    public partial int SelectedOutputIndex { get; set; } = -1;

    [ObservableProperty]
    public partial string OutputStatus { get; set; } =
        "Checking available speakers…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestOutputButtonText))]
    [NotifyPropertyChangedFor(nameof(CanTestOutput))]
    [NotifyCanExecuteChangedFor(nameof(TestOutputCommand))]
    public partial bool IsTestingOutput { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestMicrophoneButtonText))]
    [NotifyPropertyChangedFor(nameof(CanTestMicrophone))]
    [NotifyCanExecuteChangedFor(nameof(TestMicrophoneCommand))]
    public partial bool IsTestingMicrophone { get; set; }

    [ObservableProperty]
    public partial string ProxyApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DeepSeekApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string KimiApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OpenAiApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProxyStatus { get; set; } = "Checking included access…";

    [ObservableProperty]
    public partial string DeepSeekStatus { get; set; } = "Not configured";

    [ObservableProperty]
    public partial string KimiStatus { get; set; } = "Not configured";

    [ObservableProperty]
    public partial string OpenAiStatus { get; set; } = "Not configured";

    [ObservableProperty]
    public partial string SaveStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string WhisperModelStatus { get; set; } =
        "Checking local model…";

    [ObservableProperty]
    public partial string VadModelStatus { get; set; } =
        "Installed automatically when needed";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WhisperDownloadButtonText))]
    [NotifyPropertyChangedFor(nameof(CanDownloadWhisper))]
    [NotifyCanExecuteChangedFor(nameof(DownloadWhisperCommand))]
    public partial bool IsDownloadingWhisper { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownloadWhisper))]
    [NotifyPropertyChangedFor(nameof(WhisperDownloadButtonText))]
    [NotifyCanExecuteChangedFor(nameof(DownloadWhisperCommand))]
    public partial bool IsWhisperReady { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WhisperDownloadButtonText))]
    public partial double WhisperDownloadProgress { get; set; }

    [ObservableProperty]
    public partial string KokoroModelStatus { get; set; } =
        "Checking local model…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KokoroDownloadButtonText))]
    [NotifyPropertyChangedFor(nameof(CanDownloadKokoro))]
    [NotifyCanExecuteChangedFor(nameof(DownloadKokoroCommand))]
    public partial bool IsDownloadingKokoro { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownloadKokoro))]
    [NotifyPropertyChangedFor(nameof(KokoroDownloadButtonText))]
    [NotifyCanExecuteChangedFor(nameof(DownloadKokoroCommand))]
    public partial bool IsKokoroReady { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KokoroDownloadButtonText))]
    public partial double KokoroDownloadProgress { get; set; }

    public bool CanDownloadWhisper => !IsDownloadingWhisper && !IsWhisperReady;

    public bool CanLoadQwen =>
        !IsManagingQwen && !IsQwenLoaded;

    public bool CanUnloadQwen => !IsManagingQwen && IsQwenLoaded;

    public string QwenPrimaryButtonText => IsManagingQwen
        ? "Preparing…"
        : IsQwenInstalled
            ? "Load"
            : "Install · 21.5 GB";

    public string WhisperDownloadButtonText => IsWhisperReady
        ? "Installed"
        : IsDownloadingWhisper
            ? $"Downloading {WhisperDownloadProgress:P0}"
            : "Download and verify";

    public bool CanDownloadKokoro => !IsDownloadingKokoro && !IsKokoroReady;

    public bool CanTestMicrophone => !IsTestingMicrophone;

    public string TestMicrophoneButtonText => IsTestingMicrophone
        ? "Listening…"
        : "Test for 4 seconds";

    public bool CanTestOutput => !IsTestingOutput;

    public string TestOutputButtonText => IsTestingOutput
        ? "Playing…"
        : "Test sound";

    public string KokoroDownloadButtonText => IsKokoroReady
        ? "Installed"
        : IsDownloadingKokoro
            ? $"Downloading {KokoroDownloadProgress:P0}"
            : "Download and verify";

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await _languageProviders.LoadAsync().ConfigureAwait(true);
            SelectedInterfaceLanguageIndex = FindLanguageIndex(
                _languages.AvailableInterfaceLanguages.Select(item => item.Id),
                _languages.InterfaceLanguageId);
            SelectedDialogLanguageIndex = FindLanguageIndex(
                _languages.AvailableDialogLanguages.Select(item => item.Id),
                _languages.DialogLanguage.Id);
            SelectedLanguageProviderIndex = _languageProviders.Choices
                .Select((choice, index) => (choice, index))
                .Single(item => string.Equals(
                    item.choice.ProviderId,
                    _languageProviders.ProviderId,
                    StringComparison.Ordinal))
                .index;
            UpdateLanguageProviderDescription();
            LanguageProviderStatus = FormatUsingProvider(
                _languageProviders.ProviderId);
            ProxyStatus = await GetStatusAsync(
                ProxySecretKey,
                BuddyProxyLanguageProvider.EnvironmentVariable,
                _proxyConfiguration.HasIncludedKey
                    ? "Included release key · limited quota"
                    : null).ConfigureAwait(true);
            DeepSeekStatus = await GetStatusAsync(
                DeepSeekSecretKey,
                "DEEPSEEK_API_KEY").ConfigureAwait(true);
            KimiStatus = await GetStatusAsync(
                KimiSecretKey,
                "MOONSHOT_API_KEY").ConfigureAwait(true);
            OpenAiStatus = await GetStatusAsync(
                OpenAiSecretKey,
                "OPENAI_API_KEY").ConfigureAwait(true);
            await LoadMicrophonesAsync().ConfigureAwait(true);
            await LoadOutputsAsync().ConfigureAwait(true);
            await LoadModelStatusesAsync().ConfigureAwait(true);
            await RefreshQwenStatusAsync(CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedLanguageProviderIndexChanged(int value)
    {
        UpdateLanguageProviderDescription();
    }

    partial void OnSelectedInterfaceLanguageIndexChanged(int value)
    {
        if (value < 0 || value >= _languages.AvailableInterfaceLanguages.Count)
        {
            return;
        }

        _ = _languages.SetInterfaceLanguageAsync(
            _languages.AvailableInterfaceLanguages[value].Id);
    }

    partial void OnSelectedDialogLanguageIndexChanged(int value)
    {
        if (value < 0 || value >= _languages.AvailableDialogLanguages.Count)
        {
            return;
        }

        _ = _languages.SetDialogLanguageAsync(
            _languages.AvailableDialogLanguages[value].Id);
    }

    private void OnLocalizationChanged(object? sender, EventArgs eventArgs)
    {
        RefreshLanguageNames();
        UpdateLanguageProviderDescription();
        if (SelectedLanguageProviderIndex >= 0)
        {
            LanguageProviderStatus = FormatUsingProvider(
                _languageProviders.ProviderId);
        }
    }

    private void RefreshLanguageNames()
    {
        UpdateChoices(
            InterfaceLanguageNames,
            _languages.AvailableInterfaceLanguages
                .Select(language => language.NativeName)
                .ToArray());
        UpdateChoices(
            DialogLanguageNames,
            _languages.AvailableDialogLanguages
                .Select(language =>
                    _localization.Get(language.DisplayNameResourceKey))
                .ToArray());
        UpdateChoices(
            LanguageProviderNames,
            _languageProviders.Choices
                .Select(choice => GetProviderName(choice.ProviderId))
                .ToArray());
    }

    private static void UpdateChoices(
        ObservableCollection<string> target,
        string[] values)
    {
        if (target.Count != values.Length)
        {
            target.Clear();
            foreach (string value in values)
            {
                target.Add(value);
            }

            return;
        }

        for (int index = 0; index < values.Length; index++)
        {
            if (!string.Equals(target[index], values[index], StringComparison.Ordinal))
            {
                target[index] = values[index];
            }
        }
    }

    private static int FindLanguageIndex(IEnumerable<string> ids, string selectedId)
    {
        int index = 0;
        foreach (string id in ids)
        {
            if (string.Equals(id, selectedId, StringComparison.Ordinal))
            {
                return index;
            }

            index++;
        }

        return 0;
    }

    [RelayCommand]
    public async Task SaveLanguageProviderAsync()
    {
        if (SelectedLanguageProviderIndex < 0
            || SelectedLanguageProviderIndex >= _languageProviders.Choices.Count)
        {
            LanguageProviderStatus = _localization.Get("ChooseProviderFirst");
            return;
        }

        LanguageProviderChoice choice =
            _languageProviders.Choices[SelectedLanguageProviderIndex];
        await _languageProviders.SelectAsync(choice.ProviderId).ConfigureAwait(true);
        LanguageProviderStatus = FormatUsingProvider(choice.ProviderId);
        if (string.Equals(
                choice.ProviderId,
                QwenLanguageProvider.ProviderIdValue,
                StringComparison.Ordinal)
            && LoadQwenCommand.CanExecute(null))
        {
            LoadQwenCommand.Execute(null);
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadQwen), IncludeCancelCommand = true)]
    public async Task LoadQwenAsync(CancellationToken cancellationToken)
    {
        IsManagingQwen = true;
        QwenModelStatus = "Loading Qwen 3.6 27B into memory…";
        try
        {
            await _localSetup
                .EnsureQwenAsync(cancellationToken)
                .ConfigureAwait(true);
            await _qwenRuntime
                .EnsureLoadedAsync(cancellationToken)
                .ConfigureAwait(true);
            await RefreshQwenStatusAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            QwenModelStatus = "Qwen loading was cancelled.";
        }
        catch (Exception error) when (
            error is IOException
                or HttpRequestException
                or InvalidDataException
                or InvalidOperationException
                or TimeoutException
                or UnauthorizedAccessException)
        {
            QwenModelStatus = error.Message;
        }
        finally
        {
            IsManagingQwen = false;
            await RefreshQwenStatusAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUnloadQwen))]
    public async Task UnloadQwenAsync()
    {
        IsManagingQwen = true;
        try
        {
            await _qwenRuntime.UnloadAsync().ConfigureAwait(true);
        }
        finally
        {
            IsManagingQwen = false;
            await RefreshQwenStatusAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task RefreshMicrophonesAsync()
    {
        await LoadMicrophonesAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RefreshOutputsAsync()
    {
        await LoadOutputsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task SaveMicrophoneAsync()
    {
        string? deviceId = GetSelectedMicrophoneId();
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            await _settings
                .RemoveAsync(BuddySettings.InputDeviceId)
                .ConfigureAwait(true);
            MicrophoneStatus =
                "Using the Windows communications default; changes are picked up automatically.";
            return;
        }

        await _settings
            .SetAsync(BuddySettings.InputDeviceId, deviceId)
            .ConfigureAwait(true);
        MicrophoneStatus =
            $"Saved · {_microphones[SelectedMicrophoneIndex - 1].DisplayName}";
    }

    [RelayCommand]
    public async Task SaveOutputAsync()
    {
        string? deviceId = GetSelectedOutputId();
        try
        {
            await _playback.SetOutputDeviceAsync(deviceId).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                await _settings
                    .RemoveAsync(BuddySettings.OutputDeviceId)
                    .ConfigureAwait(true);
                OutputStatus = $"Windows default · {_playback.OutputDeviceName}";
                return;
            }

            await _settings
                .SetAsync(BuddySettings.OutputDeviceId, deviceId)
                .ConfigureAwait(true);
            OutputStatus =
                $"Saved · {_outputs[SelectedOutputIndex - 1].DisplayName}";
        }
        catch (Exception error) when (
            error is InvalidOperationException
                or COMException
                or UnauthorizedAccessException)
        {
            OutputStatus = $"Speaker could not be changed · {error.Message}";
        }
    }

    [RelayCommand(
        CanExecute = nameof(CanTestMicrophone),
        IncludeCancelCommand = true)]
    public async Task TestMicrophoneAsync(CancellationToken cancellationToken)
    {
        if (_recordingCoordinator.IsRecording)
        {
            MicrophoneStatus = "Stop the current recording before testing the microphone.";
            return;
        }

        if (_microphones.Count == 0)
        {
            MicrophoneStatus =
                "Windows did not report an active microphone. Check Privacy & security settings.";
            return;
        }

        IsTestingMicrophone = true;
        MicrophoneLevel = 0;
        MicrophoneStatus = "Speak normally while Buddy listens locally for four seconds.";
        Progress<float> progress = new(
            value => MicrophoneLevel = Math.Clamp(value, 0, 1));

        try
        {
            float peak = await _inputTest
                .TestAsync(
                    GetSelectedMicrophoneId(),
                    TimeSpan.FromSeconds(4),
                    progress,
                    cancellationToken)
                .ConfigureAwait(true);
            MicrophoneStatus = peak switch
            {
                < 0.005f => "No clear signal detected. Check the selected microphone and Windows permission.",
                < 0.04f => "Signal detected, but it is quiet. Move closer or raise the input level.",
                _ => $"Microphone is working · peak {peak:P0}",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MicrophoneStatus = "Microphone test stopped.";
        }
        catch (Exception error) when (
            error is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or NotSupportedException
                or COMException)
        {
            MicrophoneStatus = error switch
            {
                UnauthorizedAccessException =>
                    "Windows denied microphone access. Enable it in Privacy & security settings.",
                _ => $"Microphone test failed · {error.Message}",
            };
        }
        finally
        {
            MicrophoneLevel = 0;
            IsTestingMicrophone = false;
        }
    }

    [RelayCommand(
        CanExecute = nameof(CanTestOutput),
        IncludeCancelCommand = true)]
    public async Task TestOutputAsync(CancellationToken cancellationToken)
    {
        if (_recordingCoordinator.IsRecording)
        {
            OutputStatus =
                "Finish the current recording or dialog before testing a speaker.";
            return;
        }

        IsTestingOutput = true;
        string selectedName = GetSelectedOutputName();
        OutputStatus = $"Playing a two-note test through {selectedName}…";
        try
        {
            await _playback
                .TestOutputAsync(GetSelectedOutputId(), cancellationToken)
                .ConfigureAwait(true);
            OutputStatus = $"Test completed · {selectedName}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            OutputStatus = "Speaker test stopped.";
        }
        catch (Exception error) when (
            error is IOException
                or InvalidOperationException
                or NotSupportedException
                or COMException)
        {
            OutputStatus = $"Speaker test failed · {error.Message}";
        }
        finally
        {
            IsTestingOutput = false;
        }
    }

    [RelayCommand]
    public async Task SaveProviderKeysAsync()
    {
        IsBusy = true;
        SaveStatus = string.Empty;

        try
        {
            await SaveIfProvidedAsync(ProxySecretKey, ProxyApiKey).ConfigureAwait(true);
            await SaveIfProvidedAsync(DeepSeekSecretKey, DeepSeekApiKey).ConfigureAwait(true);
            await SaveIfProvidedAsync(KimiSecretKey, KimiApiKey).ConfigureAwait(true);
            await SaveIfProvidedAsync(OpenAiSecretKey, OpenAiApiKey).ConfigureAwait(true);

            ProxyApiKey = string.Empty;
            DeepSeekApiKey = string.Empty;
            KimiApiKey = string.Empty;
            OpenAiApiKey = string.Empty;
            SaveStatus = "Keys saved with Windows user protection.";
            await LoadAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RemoveProxyKeyAsync()
    {
        await _secrets.RemoveAsync(ProxySecretKey).ConfigureAwait(true);
        ProxyStatus = await GetStatusAsync(
            ProxySecretKey,
            BuddyProxyLanguageProvider.EnvironmentVariable,
            _proxyConfiguration.HasIncludedKey
                ? "Included release key · limited quota"
                : null).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RemoveDeepSeekKeyAsync()
    {
        await _secrets.RemoveAsync(DeepSeekSecretKey).ConfigureAwait(true);
        DeepSeekStatus = await GetStatusAsync(DeepSeekSecretKey, "DEEPSEEK_API_KEY").ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RemoveKimiKeyAsync()
    {
        await _secrets.RemoveAsync(KimiSecretKey).ConfigureAwait(true);
        KimiStatus = await GetStatusAsync(KimiSecretKey, "MOONSHOT_API_KEY").ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RemoveOpenAiKeyAsync()
    {
        await _secrets.RemoveAsync(OpenAiSecretKey).ConfigureAwait(true);
        OpenAiStatus = await GetStatusAsync(OpenAiSecretKey, "OPENAI_API_KEY").ConfigureAwait(true);
    }

    [RelayCommand(
        CanExecute = nameof(CanDownloadWhisper),
        IncludeCancelCommand = true)]
    public async Task DownloadWhisperAsync(CancellationToken cancellationToken)
    {
        IsDownloadingWhisper = true;
        SaveStatus = string.Empty;
        Progress<double> progress = new(
            value =>
            {
                WhisperDownloadProgress = Math.Clamp(value, 0, 1);
                WhisperModelStatus =
                    $"Downloading and verifying · {WhisperDownloadProgress:P0}";
                OnPropertyChanged(nameof(WhisperDownloadButtonText));
            });

        try
        {
            await _models.EnsureInstalledAsync(
                    LocalSpeechModels.WhisperLargeV3Turbo,
                    progress,
                    cancellationToken)
                .ConfigureAwait(true);
            await LoadModelStatusesAsync().ConfigureAwait(true);
            await _speechProcessing
                .QueuePendingTranscriptionsAsync(cancellationToken)
                .ConfigureAwait(true);
            SaveStatus = "Whisper is ready; pending recordings were queued.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WhisperModelStatus = "Download paused · resume whenever you are ready";
            SaveStatus = "The partial model download was kept safely.";
        }
        catch (Exception error) when (
            error is HttpRequestException
                or IOException
                or InvalidDataException
                or UnauthorizedAccessException)
        {
            WhisperModelStatus = "Model installation needs attention";
            SaveStatus = error.Message;
        }
        finally
        {
            IsDownloadingWhisper = false;
            await LoadModelStatusesAsync().ConfigureAwait(true);
        }
    }

    private async Task<string> GetStatusAsync(
        string secretKey,
        string environmentVariable,
        string? fallbackStatus = null)
    {
        string? stored = await _secrets.GetAsync(secretKey).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            return "Stored securely";
        }

        if (!string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(environmentVariable)))
        {
            return "Available from environment";
        }

        return fallbackStatus ?? "Not configured";
    }

    private void UpdateLanguageProviderDescription()
    {
        LanguageProviderDescription = SelectedLanguageProviderIndex >= 0
            && SelectedLanguageProviderIndex < _languageProviders.Choices.Count
            ? GetProviderDescription(
                _languageProviders.Choices[SelectedLanguageProviderIndex].ProviderId)
            : _localization.Get("ProviderChooseDescription");
    }

    private string GetProviderName(string providerId) => providerId switch
    {
        BuddyProxyLanguageProvider.ProviderIdValue =>
            _localization.Get("ProviderProxyName"),
        DeepSeekLanguageProvider.ProviderIdValue =>
            _localization.Get("ProviderDeepSeekName"),
        QwenLanguageProvider.ProviderIdValue =>
            _localization.Get("ProviderQwenName"),
        _ => providerId,
    };

    private string GetProviderDescription(string providerId) => providerId switch
    {
        BuddyProxyLanguageProvider.ProviderIdValue =>
            _localization.Get("ProviderProxyDescription"),
        DeepSeekLanguageProvider.ProviderIdValue =>
            _localization.Get("ProviderDeepSeekDescription"),
        QwenLanguageProvider.ProviderIdValue =>
            _localization.Get("ProviderQwenDescription"),
        _ => _localization.Get("ProviderChooseDescription"),
    };

    private string FormatUsingProvider(string providerId) => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        _localization.Get("UsingProviderFormat"),
        GetProviderName(providerId));

    private async Task RefreshQwenStatusAsync(
        CancellationToken cancellationToken = default)
    {
        QwenInstallStatus installation = await _qwenInstaller
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(true);
        if (installation.State != QwenInstallState.Ready)
        {
            QwenModelStatus = installation.Message;
            IsQwenInstalled = false;
            IsQwenLoaded = false;
            return;
        }

        QwenRuntimeStatus status = await _qwenRuntime
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(true);
        QwenModelStatus = status.Message;
        IsQwenInstalled = status.State != QwenRuntimeState.Missing;
        IsQwenLoaded = status.State == QwenRuntimeState.Loaded;
    }

    private async Task SaveIfProvidedAsync(string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            await _secrets.SetAsync(key, value.Trim()).ConfigureAwait(false);
        }
    }

    [RelayCommand(
        CanExecute = nameof(CanDownloadKokoro),
        IncludeCancelCommand = true)]
    public async Task DownloadKokoroAsync(CancellationToken cancellationToken)
    {
        IsDownloadingKokoro = true;
        SaveStatus = string.Empty;
        Progress<double> progress = new(
            value =>
            {
                KokoroDownloadProgress = Math.Clamp(value, 0, 1);
                KokoroModelStatus =
                    $"Downloading and verifying · {KokoroDownloadProgress:P0}";
                OnPropertyChanged(nameof(KokoroDownloadButtonText));
            });

        try
        {
            await _models.EnsureInstalledAsync(
                    LocalSpeechModels.KokoroEnglishV1,
                    progress,
                    cancellationToken)
                .ConfigureAwait(true);
            await LoadModelStatusesAsync().ConfigureAwait(true);
            SaveStatus = "Kokoro is ready for private, local speech generation.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KokoroModelStatus = "Download paused · resume whenever you are ready";
            SaveStatus = "The partial model download was kept safely.";
        }
        catch (Exception error) when (
            error is HttpRequestException
                or IOException
                or InvalidDataException
                or UnauthorizedAccessException)
        {
            KokoroModelStatus = "Model installation needs attention";
            SaveStatus = error.Message;
        }
        finally
        {
            IsDownloadingKokoro = false;
            await LoadModelStatusesAsync().ConfigureAwait(true);
        }
    }

    private async Task LoadModelStatusesAsync()
    {
        IReadOnlyList<LocalModelInfo> models = await _models
            .GetModelsAsync()
            .ConfigureAwait(true);
        LocalModelInfo? whisper = models.FirstOrDefault(
            model => model.Id == LocalSpeechModels.WhisperLargeV3Turbo);
        LocalModelInfo? vad = models.FirstOrDefault(
            model => model.Id == LocalSpeechModels.SileroVad);
        LocalModelInfo? kokoro = models.FirstOrDefault(
            model => model.Id == LocalSpeechModels.KokoroEnglishV1);

        IsWhisperReady = whisper?.Status == LocalModelStatus.Ready;
        WhisperDownloadProgress = IsWhisperReady ? 1 : WhisperDownloadProgress;
        IsKokoroReady = kokoro?.Status == LocalModelStatus.Ready;
        KokoroDownloadProgress = IsKokoroReady ? 1 : KokoroDownloadProgress;
        WhisperModelStatus = FormatModelStatus(
            whisper,
            "1.51 GB · CUDA preferred, CPU fallback");
        VadModelStatus = FormatModelStatus(
            vad,
            "864 KB · installed automatically when needed");
        KokoroModelStatus = FormatModelStatus(
            kokoro,
            "310 MB · private local English voice");
    }

    private async Task LoadMicrophonesAsync()
    {
        string? savedDeviceId = await _settings
            .GetAsync(BuddySettings.InputDeviceId)
            .ConfigureAwait(true);
        IReadOnlyList<AudioInputDevice> devices;
        try
        {
            devices = await _capture.GetInputDevicesAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (
            error is InvalidOperationException
                or COMException
                or UnauthorizedAccessException)
        {
            _microphones = [];
            SelectedMicrophoneIndex = -1;
            MicrophoneNames.Clear();
            MicrophoneNames.Add("Windows communications default");
            SelectedMicrophoneIndex = 0;
            MicrophoneStatus = $"Microphones could not be listed · {error.Message}";
            return;
        }

        _microphones = devices;
        SelectedMicrophoneIndex = -1;
        MicrophoneNames.Clear();
        MicrophoneNames.Add("Windows communications default (recommended)");
        foreach (AudioInputDevice device in devices)
        {
            MicrophoneNames.Add(
                device.IsDefault
                    ? $"{device.DisplayName} · current default"
                    : device.DisplayName);
        }

        int savedIndex = string.IsNullOrWhiteSpace(savedDeviceId)
            ? -1
            : devices
                .Select((device, index) => (device, index))
                .Where(item => string.Equals(
                    item.device.Id,
                    savedDeviceId,
                    StringComparison.Ordinal))
                .Select(item => item.index)
                .DefaultIfEmpty(-1)
                .First();
        SelectedMicrophoneIndex = savedIndex + 1;

        if (devices.Count == 0)
        {
            MicrophoneStatus =
                "No active microphone found. Buddy will keep checking when you open Settings.";
        }
        else if (savedIndex >= 0)
        {
            MicrophoneStatus = $"Saved · {devices[savedIndex].DisplayName}";
        }
        else if (!string.IsNullOrWhiteSpace(savedDeviceId))
        {
            MicrophoneStatus =
                "The saved microphone is unavailable; recordings will use the Windows default.";
        }
        else
        {
            AudioInputDevice defaultDevice =
                devices.FirstOrDefault(device => device.IsDefault) ?? devices[0];
            MicrophoneStatus = $"Windows default · {defaultDevice.DisplayName}";
        }
    }

    private async Task LoadOutputsAsync()
    {
        string? savedDeviceId = await _settings
            .GetAsync(BuddySettings.OutputDeviceId)
            .ConfigureAwait(true);
        IReadOnlyList<AudioOutputDevice> devices;
        try
        {
            devices = await _playback.GetOutputDevicesAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (
            error is InvalidOperationException
                or COMException
                or UnauthorizedAccessException)
        {
            _outputs = [];
            SelectedOutputIndex = -1;
            OutputNames.Clear();
            OutputNames.Add("Windows multimedia default");
            SelectedOutputIndex = 0;
            OutputStatus = $"Speakers could not be listed · {error.Message}";
            return;
        }

        _outputs = devices;
        SelectedOutputIndex = -1;
        OutputNames.Clear();
        OutputNames.Add("Windows multimedia default (recommended)");
        foreach (AudioOutputDevice device in devices)
        {
            OutputNames.Add(
                device.IsDefault
                    ? $"{device.DisplayName} · current default"
                    : device.DisplayName);
        }

        int savedIndex = string.IsNullOrWhiteSpace(savedDeviceId)
            ? -1
            : devices
                .Select((device, index) => (device, index))
                .Where(item => string.Equals(
                    item.device.Id,
                    savedDeviceId,
                    StringComparison.Ordinal))
                .Select(item => item.index)
                .DefaultIfEmpty(-1)
                .First();
        SelectedOutputIndex = savedIndex + 1;

        if (devices.Count == 0)
        {
            OutputStatus = "No active speaker found. Check Windows sound settings.";
            return;
        }

        string? appliedDeviceId = savedIndex >= 0 ? devices[savedIndex].Id : null;
        try
        {
            await _playback
                .SetOutputDeviceAsync(appliedDeviceId)
                .ConfigureAwait(true);
        }
        catch (Exception error) when (error is InvalidOperationException or COMException)
        {
            OutputStatus = $"Speaker could not be opened · {error.Message}";
            return;
        }

        if (savedIndex >= 0)
        {
            OutputStatus = $"Saved · {devices[savedIndex].DisplayName}";
        }
        else if (!string.IsNullOrWhiteSpace(savedDeviceId))
        {
            OutputStatus =
                $"Saved speaker unavailable · using {_playback.OutputDeviceName}";
        }
        else
        {
            OutputStatus = $"Windows default · {_playback.OutputDeviceName}";
        }
    }

    private string? GetSelectedMicrophoneId()
    {
        int deviceIndex = SelectedMicrophoneIndex - 1;
        return deviceIndex >= 0 && deviceIndex < _microphones.Count
            ? _microphones[deviceIndex].Id
            : null;
    }

    private string? GetSelectedOutputId()
    {
        int deviceIndex = SelectedOutputIndex - 1;
        return deviceIndex >= 0 && deviceIndex < _outputs.Count
            ? _outputs[deviceIndex].Id
            : null;
    }

    private string GetSelectedOutputName()
    {
        int deviceIndex = SelectedOutputIndex - 1;
        if (deviceIndex >= 0 && deviceIndex < _outputs.Count)
        {
            return _outputs[deviceIndex].DisplayName;
        }

        return _playback.OutputDeviceName ?? "the Windows default speaker";
    }

    private static string FormatModelStatus(
        LocalModelInfo? model,
        string detail)
    {
        string status = model?.Status switch
        {
            LocalModelStatus.Ready => "Ready and SHA-256 verified",
            LocalModelStatus.Downloading => "Partial download ready to resume",
            LocalModelStatus.Invalid => "Present but not verified",
            _ => "Not installed",
        };
        return $"{status} · {detail}";
    }
}
