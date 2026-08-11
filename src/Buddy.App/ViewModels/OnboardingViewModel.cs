using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Buddy.App.Services;
using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using Buddy.Language;
using Buddy.Speech;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Buddy.App.ViewModels;

public sealed partial class OnboardingViewModel : ObservableObject
{
    private static readonly Regex TrialCodePattern = new(
        "^[A-Z]{6}-[A-Z]{6}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IAppSettingsStore _settings;
    private readonly ISecretStore _secrets;
    private readonly ILocalModelManager _models;
    private readonly IQwenModelInstaller _qwenInstaller;
    private readonly IQwenModelRuntime _qwenRuntime;
    private readonly ISpeechSynthesisService _speechSynthesis;
    private readonly LanguageProviderRouter _providers;
    private readonly BuddyProxyClientConfiguration _proxyConfiguration;
    private readonly LanguagePreferences _languages;
    private readonly UiLocalizationService _localization;
    private readonly WelcomeSetupDownloadGate _downloadGate = new();
    private bool _updatingSelections;
    private bool _initialized;

    public OnboardingViewModel(
        IAppSettingsStore settings,
        ISecretStore secrets,
        ILocalModelManager models,
        ISpeechSynthesisService speechSynthesis,
        IQwenModelInstaller qwenInstaller,
        IQwenModelRuntime qwenRuntime,
        LanguageProviderRouter providers,
        BuddyProxyClientConfiguration proxyConfiguration,
        LanguagePreferences languages,
        UiLocalizationService localization)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _speechSynthesis = speechSynthesis
            ?? throw new ArgumentNullException(nameof(speechSynthesis));
        _qwenInstaller = qwenInstaller
            ?? throw new ArgumentNullException(nameof(qwenInstaller));
        _qwenRuntime = qwenRuntime
            ?? throw new ArgumentNullException(nameof(qwenRuntime));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _proxyConfiguration = proxyConfiguration
            ?? throw new ArgumentNullException(nameof(proxyConfiguration));
        _languages = languages ?? throw new ArgumentNullException(nameof(languages));
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));
        RefreshLocalizedChoices();
    }

    public ObservableCollection<string> InterfaceLanguageNames { get; } = [];

    public ObservableCollection<string> DialogLanguageNames { get; } = [];

    public ObservableCollection<string> ProviderNames { get; } = [];

    public event EventHandler? Completed;

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetupStep))]
    public partial bool IsCompletionStep { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsInstalling { get; set; }

    [ObservableProperty]
    public partial int SelectedInterfaceLanguageIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedDialogLanguageIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTrialSelected))]
    [NotifyPropertyChangedFor(nameof(IsDeepSeekSelected))]
    [NotifyPropertyChangedFor(nameof(IsQwenSelected))]
    [NotifyPropertyChangedFor(nameof(ShouldShowQwenProgress))]
    public partial int SelectedProviderIndex { get; set; }

    [ObservableProperty]
    public partial string TrialCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DeepSeekApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShouldShowQwenProgress))]
    public partial double WhisperProgress { get; set; }

    [ObservableProperty]
    public partial string WhisperStatus { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShouldShowQwenProgress))]
    public partial double VadProgress { get; set; }

    [ObservableProperty]
    public partial string VadStatus { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShouldShowQwenProgress))]
    public partial double VoiceProgress { get; set; }

    [ObservableProperty]
    public partial string VoiceStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double QwenProgress { get; set; }

    [ObservableProperty]
    public partial string QwenStatus { get; set; } = string.Empty;

    public bool IsSetupStep => !IsCompletionStep;

    public bool CanEdit => !IsInstalling;

    public bool IsTrialSelected => SelectedProviderIndex == 0;

    public bool IsDeepSeekSelected => SelectedProviderIndex == 1;

    public bool IsQwenSelected => SelectedProviderIndex == 2;

    public bool ShouldShowQwenProgress => IsQwenSelected
        && WhisperProgress >= 1
        && VadProgress >= 1
        && VoiceProgress >= 1;

    public bool HasIncludedTrialCode => _proxyConfiguration.HasIncludedKey;

    public string SetupButtonText => HasError
        ? _localization.Get("SetupRetry")
        : _localization.Get("SetupStart");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _updatingSelections = true;
        try
        {
            // The view model is created before persisted language preferences
            // are loaded. Refresh here so a returning, not-yet-onboarded user
            // sees every picker in the saved interface language immediately.
            RefreshLocalizedChoices();
            SelectedInterfaceLanguageIndex = FindInterfaceLanguageIndex(
                _languages.InterfaceLanguageId);
            SelectedDialogLanguageIndex = FindDialogLanguageIndex(
                _languages.DialogLanguage.Id);
            await _providers.LoadAsync(cancellationToken).ConfigureAwait(true);
            SelectedProviderIndex = FindProviderIndex(_providers.ProviderId);

            string? completed = await _settings
                .GetAsync(BuddySettings.OnboardingCompleted, cancellationToken)
                .ConfigureAwait(true);
            IsVisible = !string.Equals(completed, "true", StringComparison.Ordinal);
            if (IsVisible)
            {
                InitializeDependencyPreview();
            }

            _initialized = true;
        }
        finally
        {
            _updatingSelections = false;
        }
    }

    partial void OnSelectedInterfaceLanguageIndexChanged(int value)
    {
        if (_updatingSelections
            || value < 0
            || value >= _languages.AvailableInterfaceLanguages.Count)
        {
            return;
        }

        _ = ChangeInterfaceLanguageAsync(value);
    }

    partial void OnSelectedDialogLanguageIndexChanged(int value)
    {
        if (_updatingSelections
            || value < 0
            || value >= _languages.AvailableDialogLanguages.Count)
        {
            return;
        }

        _ = ChangeDialogLanguageAsync(value);
    }

    partial void OnSelectedProviderIndexChanged(int value)
    {
        _ = value;
        OnPropertyChanged(nameof(SetupButtonText));
        if (!_downloadGate.HasUserRequestedSetup)
        {
            QwenProgress = 0;
            QwenStatus = _localization.Get("SetupOnDemand");
        }
    }

    partial void OnTrialCodeChanged(string value)
    {
        string letters = new(
            value.Where(character =>
                    character is >= 'A' and <= 'Z'
                        or >= 'a' and <= 'z')
                .Select(char.ToUpperInvariant)
                .Take(12)
                .ToArray());
        string formatted = letters.Length <= 6
            ? letters
            : letters[..6] + "-" + letters[6..];
        if (!string.Equals(value, formatted, StringComparison.Ordinal))
        {
            TrialCode = formatted;
        }
    }

    partial void OnHasErrorChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(SetupButtonText));
    }

    [RelayCommand(CanExecute = nameof(CanRunSetup))]
    public async Task RunSetupAsync(CancellationToken cancellationToken)
    {
        // Keep the user's provider choice stable for the whole setup run. The
        // native Windows Picker can briefly publish another SelectedIndex when
        // its localized items refresh and the control becomes disabled. That
        // transient UI event must never start a 21 GB Qwen download.
        int setupProviderIndex = SelectedProviderIndex;
        HasError = false;
        StatusMessage = _localization.Get("SetupPreparing");
        if (!ValidateProvider(out string validationMessage))
        {
            HasError = true;
            StatusMessage = validationMessage;
            return;
        }

        _downloadGate.AuthorizeUserRequestedSetup();
        IsInstalling = true;
        RunSetupCommand.NotifyCanExecuteChanged();
        try
        {
            await PersistSelectionsAsync(setupProviderIndex, cancellationToken)
                .ConfigureAwait(true);
            await InstallSpeechModelAsync(
                    LocalSpeechModels.WhisperLargeV3Turbo,
                    value =>
                    {
                        WhisperProgress = value;
                        WhisperStatus = FormatProgress(value);
                    },
                    cancellationToken)
                .ConfigureAwait(true);
            WhisperProgress = 1;
            WhisperStatus = _localization.Get("SetupReady");

            await InstallSpeechModelAsync(
                    LocalSpeechModels.SileroVad,
                    value =>
                    {
                        VadProgress = value;
                        VadStatus = FormatProgress(value);
                    },
                    cancellationToken)
                .ConfigureAwait(true);
            VadProgress = 1;
            VadStatus = _localization.Get("SetupReady");

            if (SpeechVoiceSelector.RequiresKokoro(_languages.DialogLanguage))
            {
                await InstallSpeechModelAsync(
                        LocalSpeechModels.KokoroEnglishV1,
                        value =>
                        {
                            VoiceProgress = value;
                            VoiceStatus = FormatProgress(value);
                        },
                        cancellationToken)
                    .ConfigureAwait(true);
            }

            VoiceProgress = 1;
            await ApplyVoiceStatusAsync(
                    await _models.GetModelsAsync(cancellationToken)
                        .ConfigureAwait(true),
                    requireAvailableVoice: true,
                    cancellationToken)
                .ConfigureAwait(true);

            if (setupProviderIndex == 2)
            {
                _downloadGate.DemandUserRequestedSetup();
                QwenStatus = _localization.Get("SetupWaiting");
                Progress<QwenInstallProgress> qwenProgress = new(
                    progress =>
                    {
                        QwenProgress = Math.Clamp(progress.Fraction, 0, 1);
                        QwenStatus = $"{progress.Stage} · {QwenProgress:P0}";
                    });
                await _qwenInstaller
                    .EnsureInstalledAsync(qwenProgress, cancellationToken)
                    .ConfigureAwait(true);
                QwenStatus = _localization.Get("SetupLoadingQwen");
                await _qwenRuntime
                    .EnsureLoadedAsync(cancellationToken)
                    .ConfigureAwait(true);
                QwenProgress = 1;
                QwenStatus = _localization.Get("SetupReady");
            }

            ProviderHealth health = await _providers
                .CheckHealthAsync(cancellationToken)
                .ConfigureAwait(true);
            if (health.Status != ProviderHealthStatus.Available)
            {
                throw new InvalidOperationException(health.Message);
            }

            IsCompletionStep = true;
            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            HasError = true;
            StatusMessage = _localization.Get("SetupPaused");
        }
        catch (Exception error) when (
            error is IOException
                or HttpRequestException
                or InvalidDataException
                or InvalidOperationException
                or UnauthorizedAccessException
                or TimeoutException)
        {
            HasError = true;
            StatusMessage = error.Message;
        }
        finally
        {
            IsInstalling = false;
            RunSetupCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    public async Task FinishAsync()
    {
        await _settings
            .SetAsync(BuddySettings.OnboardingCompleted, "true")
            .ConfigureAwait(true);
        await _settings
            .SetAsync(
                BuddySettings.DialogAllowedPauseMilliseconds,
                "3000")
            .ConfigureAwait(true);
        IsVisible = false;
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private bool CanRunSetup() => !IsInstalling;

    private async Task ChangeInterfaceLanguageAsync(int index)
    {
        _updatingSelections = true;
        try
        {
            await _languages
                .SetInterfaceLanguageAsync(
                    _languages.AvailableInterfaceLanguages[index].Id)
                .ConfigureAwait(true);
            RefreshLocalizedChoices();
            if (SelectedInterfaceLanguageIndex != index)
            {
                SelectedInterfaceLanguageIndex = index;
            }
            OnPropertyChanged(nameof(SetupButtonText));
            if (IsVisible && IsSetupStep)
            {
                if (_downloadGate.HasUserRequestedSetup)
                {
                    await RefreshDependencyStatusAsync(CancellationToken.None)
                        .ConfigureAwait(true);
                }
                else
                {
                    InitializeDependencyPreview();
                }
            }
        }
        finally
        {
            _updatingSelections = false;
        }
    }

    private async Task ChangeDialogLanguageAsync(int index)
    {
        await _languages
            .SetDialogLanguageAsync(_languages.AvailableDialogLanguages[index].Id)
            .ConfigureAwait(true);
        if (!_downloadGate.HasUserRequestedSetup)
        {
            VoiceProgress = 0;
            VoiceStatus = _localization.Get("SetupOnDemand");
            return;
        }

        IReadOnlyList<LocalModelInfo> models = await _models
            .GetModelsAsync()
            .ConfigureAwait(true);
        await ApplyVoiceStatusAsync(models).ConfigureAwait(true);
    }

    private void RefreshLocalizedChoices()
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
            ProviderNames,
            [
                _localization.Get("PlatformTrial"),
                _localization.Get("PlatformDeepSeek"),
                _localization.Get("PlatformQwen"),
            ]);
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

    private async Task PersistSelectionsAsync(
        int providerIndex,
        CancellationToken cancellationToken)
    {
        await _languages
            .SetInterfaceLanguageAsync(
                _languages.AvailableInterfaceLanguages[
                    SelectedInterfaceLanguageIndex].Id,
                cancellationToken)
            .ConfigureAwait(true);
        await _languages
            .SetDialogLanguageAsync(
                _languages.AvailableDialogLanguages[
                    SelectedDialogLanguageIndex].Id,
                cancellationToken)
            .ConfigureAwait(true);

        // Reassert the captured selection after the language resources have
        // refreshed so the visible Picker and the persisted provider agree.
        SelectedProviderIndex = providerIndex;
        string providerId = providerIndex switch
        {
            1 => DeepSeekLanguageProvider.ProviderIdValue,
            2 => QwenLanguageProvider.ProviderIdValue,
            _ => BuddyProxyLanguageProvider.ProviderIdValue,
        };
        await _providers.SelectAsync(providerId, cancellationToken)
            .ConfigureAwait(true);

        if (providerIndex == 0)
        {
            if (string.IsNullOrWhiteSpace(TrialCode))
            {
                await _secrets
                    .RemoveAsync(
                        BuddyProxyLanguageProvider.SecretKey,
                        cancellationToken)
                    .ConfigureAwait(true);
            }
            else
            {
                await _secrets
                    .SetAsync(
                        BuddyProxyLanguageProvider.SecretKey,
                        TrialCode.Trim(),
                        cancellationToken)
                    .ConfigureAwait(true);
            }
        }
        else if (providerIndex == 1)
        {
            await _secrets
                .SetAsync(
                    DeepSeekLanguageProvider.SecretKey,
                    DeepSeekApiKey.Trim(),
                    cancellationToken)
                .ConfigureAwait(true);
        }
    }

    private bool ValidateProvider(out string message)
    {
        if (SelectedInterfaceLanguageIndex < 0
            || SelectedInterfaceLanguageIndex
                >= _languages.AvailableInterfaceLanguages.Count
            || SelectedDialogLanguageIndex < 0
            || SelectedDialogLanguageIndex
                >= _languages.AvailableDialogLanguages.Count)
        {
            message = _localization.Get("SetupChooseLanguages");
            return false;
        }

        if (IsTrialSelected
            && string.IsNullOrWhiteSpace(TrialCode)
            && !_proxyConfiguration.HasIncludedKey)
        {
            message = _localization.Get("SetupCodeHelp");
            return false;
        }

        if (IsTrialSelected
            && !string.IsNullOrWhiteSpace(TrialCode)
            && !TrialCodePattern.IsMatch(TrialCode.Trim()))
        {
            message = _localization.Get("SetupCodeHelp")
                + " " + _localization.Get("SetupCodeExample");
            return false;
        }

        if (IsDeepSeekSelected
            && (!DeepSeekApiKey.Trim().StartsWith("sk-", StringComparison.Ordinal)
                || DeepSeekApiKey.Trim().Length < 16))
        {
            message = _localization.Get("SetupDeepSeekKeyError");
            return false;
        }

        message = string.Empty;
        return true;
    }

    private async Task InstallSpeechModelAsync(
        string modelId,
        Action<double> update,
        CancellationToken cancellationToken)
    {
        _downloadGate.DemandUserRequestedSetup();
        Progress<double> progress = new(
            value => update(Math.Clamp(value, 0, 1)));
        await _models
            .EnsureInstalledAsync(modelId, progress, cancellationToken)
            .ConfigureAwait(true);
    }

    private void InitializeDependencyPreview()
    {
        string onDemand = _localization.Get("SetupOnDemand");
        WhisperProgress = 0;
        WhisperStatus = onDemand;
        VadProgress = 0;
        VadStatus = onDemand;
        VoiceProgress = 0;
        VoiceStatus = onDemand;
        QwenProgress = 0;
        QwenStatus = onDemand;
    }

    private async Task RefreshDependencyStatusAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalModelInfo> models = await _models
            .GetModelsAsync(cancellationToken)
            .ConfigureAwait(true);
        ApplyModelStatus(
            models,
            LocalSpeechModels.WhisperLargeV3Turbo,
            value => WhisperProgress = value,
            value => WhisperStatus = value);
        ApplyModelStatus(
            models,
            LocalSpeechModels.SileroVad,
            value => VadProgress = value,
            value => VadStatus = value);
        await ApplyVoiceStatusAsync(
                models,
                cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        QwenInstallStatus qwen = await _qwenInstaller
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(true);
        QwenProgress = qwen.State == QwenInstallState.Ready ? 1 : 0;
        QwenStatus = qwen.State == QwenInstallState.Ready
            ? _localization.Get("SetupReady")
            : _localization.Get("SetupWaiting");
    }

    private async Task ApplyVoiceStatusAsync(
        IReadOnlyList<LocalModelInfo> models,
        bool requireAvailableVoice = false,
        CancellationToken cancellationToken = default)
    {
        if (!SpeechVoiceSelector.RequiresKokoro(_languages.DialogLanguage))
        {
            IReadOnlyList<SpeechVoice> voices = await _speechSynthesis
                .GetVoicesAsync(cancellationToken)
                .ConfigureAwait(true);
            SpeechVoice? voice = SpeechVoiceSelector.FindPreferred(
                voices,
                _languages.DialogLanguage);
            if (voice is null)
            {
                VoiceProgress = 0;
                VoiceStatus = _localization.Get("SetupVoiceMissing");
                if (requireAvailableVoice)
                {
                    throw new InvalidOperationException(VoiceStatus);
                }

                return;
            }

            VoiceProgress = 1;
            VoiceStatus = _localization.Get("SetupReady")
                + " · " + voice.DisplayName;
            return;
        }

        ApplyModelStatus(
            models,
            LocalSpeechModels.KokoroEnglishV1,
            value => VoiceProgress = value,
            value => VoiceStatus = value);
    }

    private void ApplyModelStatus(
        IReadOnlyList<LocalModelInfo> models,
        string modelId,
        Action<double> setProgress,
        Action<string> setStatus)
    {
        LocalModelInfo? model = models.FirstOrDefault(
            candidate => candidate.Id == modelId);
        bool ready = model?.Status == LocalModelStatus.Ready;
        setProgress(ready ? 1 : 0);
        setStatus(_localization.Get(ready ? "SetupReady" : "SetupWaiting"));
    }

    private string FormatProgress(double value) => value >= 1
        ? _localization.Get("SetupReady")
        : $"{value:P0}";

    private int FindInterfaceLanguageIndex(string id) => Math.Max(
        0,
        _languages.AvailableInterfaceLanguages
            .Select((language, index) => (language, index))
            .FirstOrDefault(item => item.language.Id == id)
            .index);

    private int FindDialogLanguageIndex(string id) => Math.Max(
        0,
        _languages.AvailableDialogLanguages
            .Select((language, index) => (language, index))
            .FirstOrDefault(item => item.language.Id == id)
            .index);

    private static int FindProviderIndex(string id) => id switch
    {
        DeepSeekLanguageProvider.ProviderIdValue => 1,
        QwenLanguageProvider.ProviderIdValue => 2,
        _ => 0,
    };
}
