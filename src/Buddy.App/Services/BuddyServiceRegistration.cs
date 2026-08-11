using Buddy.App.State;
using Buddy.App.ViewModels;
using Buddy.Audio.Windows;
using Buddy.Core.Abstractions;
using Buddy.Language;
using Buddy.Persistence;
using Buddy.Speech;
using Microsoft.Extensions.DependencyInjection;

#if !WINDOWS
using Buddy.Audio.Portable;
#endif
#if MACCATALYST
using Buddy.App.Platforms.MacCatalyst;
#endif

namespace Buddy.App.Services;

public static class BuddyServiceRegistration
{
    public static IServiceCollection AddBuddyServices(
        IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        string localAppDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Buddy");
#if WINDOWS
        bool hasHeavyDataDrive = Directory.Exists(@"H:\");
        string dataRoot = Environment.GetEnvironmentVariable("BUDDY_DATA_ROOT")
            ?? (hasHeavyDataDrive ? @"H:\Buddy" : localAppDataRoot);
        string languageRoot = Environment.GetEnvironmentVariable("BUDDY_AI_ROOT")
            ?? (hasHeavyDataDrive
                ? @"H:\BuddyAI"
                : Path.Combine(dataRoot, "language-models"));
#else
        string dataRoot = Environment.GetEnvironmentVariable("BUDDY_DATA_ROOT")
            ?? localAppDataRoot;
        string languageRoot = Environment.GetEnvironmentVariable("BUDDY_AI_ROOT")
            ?? Path.Combine(dataRoot, "language-models");
#endif

        services.AddSingleton(new BuddyDataPaths(dataRoot));
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<BuddyDatabase>();
        services.AddSingleton<IBuddyDatabase>(
            provider => provider.GetRequiredService<BuddyDatabase>());
        services.AddSingleton<SqliteRecordingRepository>();
        services.AddSingleton<IRecordingRepository>(
            provider => provider.GetRequiredService<SqliteRecordingRepository>());
        services.AddSingleton<SqliteDialogRepository>();
        services.AddSingleton<IDialogRepository>(
            provider => provider.GetRequiredService<SqliteDialogRepository>());
        services.AddSingleton<SqliteBackgroundJobStore>();
        services.AddSingleton<IBackgroundJobStore>(
            provider => provider.GetRequiredService<SqliteBackgroundJobStore>());
        services.AddSingleton<SqliteAppSettingsStore>();
        services.AddSingleton<IAppSettingsStore>(
            provider => provider.GetRequiredService<SqliteAppSettingsStore>());
        services.AddSingleton<JsonCaptureJournalStore>();
        services.AddSingleton<ICaptureJournalStore>(
            provider => provider.GetRequiredService<JsonCaptureJournalStore>());

#if WINDOWS
        services.AddSingleton<WasapiAudioCaptureService>();
        services.AddSingleton<IAudioCaptureService>(
            provider => provider.GetRequiredService<WasapiAudioCaptureService>());
        services.AddSingleton<WasapiAudioInputTestService>();
        services.AddSingleton<IAudioInputTestService>(
            provider => provider.GetRequiredService<WasapiAudioInputTestService>());
        services.AddSingleton<NAudioPlaybackService>();
        services.AddSingleton<IAudioPlaybackService>(
            provider => provider.GetRequiredService<NAudioPlaybackService>());
#elif MACCATALYST
        services.AddSingleton<MacCatalystAudioCaptureService>();
        services.AddSingleton<IAudioCaptureService>(
            provider => provider.GetRequiredService<MacCatalystAudioCaptureService>());
        services.AddSingleton<MacCatalystAudioInputTestService>();
        services.AddSingleton<IAudioInputTestService>(
            provider => provider.GetRequiredService<MacCatalystAudioInputTestService>());
        services.AddSingleton<MacCatalystAudioPlaybackService>();
        services.AddSingleton<IAudioPlaybackService>(
            provider => provider.GetRequiredService<MacCatalystAudioPlaybackService>());
#else
        services.AddSingleton<MiniAudioCaptureService>();
        services.AddSingleton<IAudioCaptureService>(
            provider => provider.GetRequiredService<MiniAudioCaptureService>());
        services.AddSingleton<MiniAudioInputTestService>();
        services.AddSingleton<IAudioInputTestService>(
            provider => provider.GetRequiredService<MiniAudioInputTestService>());
        services.AddSingleton<MiniAudioPlaybackService>();
        services.AddSingleton<IAudioPlaybackService>(
            provider => provider.GetRequiredService<MiniAudioPlaybackService>());
#endif
        services.AddSingleton<OggOpusAudioArchiveService>();
        services.AddSingleton<IAudioArchiveService>(
            provider => provider.GetRequiredService<OggOpusAudioArchiveService>());
        services.AddSingleton<SpeechAudioPreparationService>();
        services.AddSingleton<IAudioPreparationService>(
            provider => provider.GetRequiredService<SpeechAudioPreparationService>());
        services.AddSingleton<AudioWaveformService>();
        services.AddSingleton<IAudioWaveformService>(
            provider => provider.GetRequiredService<AudioWaveformService>());

        services.AddSingleton(
            _ => new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan,
            });
        services.AddSingleton<BuddyProxyClientConfiguration>();
        services.AddSingleton<BuddyFeedbackClient>();
        services.AddSingleton<FeedbackAttachmentPicker>();
        services.AddSingleton<VerifiedLocalModelManager>(
            provider => new VerifiedLocalModelManager(
                provider.GetRequiredService<BuddyDataPaths>().Models,
                provider.GetRequiredService<HttpClient>()));
        services.AddSingleton<ILocalModelManager>(
            provider => provider.GetRequiredService<VerifiedLocalModelManager>());
        services.AddSingleton<WhisperVoiceActivityService>();
        services.AddSingleton<IVoiceActivityService>(
            provider => provider.GetRequiredService<WhisperVoiceActivityService>());
        services.AddSingleton<WhisperTranscriptionService>();
        services.AddSingleton<ITranscriptionService>(
            provider => provider.GetRequiredService<WhisperTranscriptionService>());
        services.AddSingleton<KokoroPhoneticTranscriptionService>();
        services.AddSingleton<IPhoneticTranscriptionService>(
            provider => provider
                .GetRequiredService<KokoroPhoneticTranscriptionService>());
        services.AddSingleton<KokoroSpeechSynthesisService>();
#if WINDOWS
        services.AddSingleton<WindowsSpeechSynthesisService>();
        services.AddSingleton<IPlatformSpeechSynthesisService>(
            provider => provider.GetRequiredService<WindowsSpeechSynthesisService>());
#elif MACCATALYST
        services.AddSingleton<MacOsSpeechSynthesisService>();
        services.AddSingleton<IPlatformSpeechSynthesisService>(
            provider => provider.GetRequiredService<MacOsSpeechSynthesisService>());
#endif
        services.AddSingleton<LocalSpeechSynthesisService>();
        services.AddSingleton<ISpeechSynthesisService>(
            provider => provider.GetRequiredService<LocalSpeechSynthesisService>());

        QwenRuntimeOptions qwenOptions = new(
            Path.Combine(languageRoot, "llama.cpp", "b10243"),
            Path.Combine(languageRoot, "models", "Qwen3.6-27B-Q4_K_M.gguf"),
            Path.Combine(languageRoot, "logs"),
            DraftModelPath: Path.Combine(
                languageRoot,
                "models",
                "dflash-Qwen3.6-27B-Q8_0.gguf"));
        services.AddSingleton(qwenOptions);
#if WINDOWS
        services.AddSingleton<QwenModelRuntime>();
        services.AddSingleton<IQwenModelRuntime>(
            provider => provider.GetRequiredService<QwenModelRuntime>());
        services.AddSingleton<QwenModelInstaller>();
        services.AddSingleton<IQwenModelInstaller>(
            provider => provider.GetRequiredService<QwenModelInstaller>());
#else
        services.AddSingleton<IQwenModelRuntime, UnsupportedQwenModelRuntime>();
        services.AddSingleton<IQwenModelInstaller, UnsupportedQwenModelInstaller>();
#endif
        services.AddSingleton<QwenLanguageProvider>();
        services.AddSingleton<DeepSeekLanguageProvider>();
        services.AddSingleton<BuddyProxyLanguageProvider>(
            provider =>
            {
                BuddyProxyClientConfiguration configuration = provider
                    .GetRequiredService<BuddyProxyClientConfiguration>();
                return new BuddyProxyLanguageProvider(
                    configuration.CreateHttpClient(),
                    provider.GetRequiredService<ISecretStore>(),
                    configuration.Endpoint,
                    configuration.IncludedApiKey);
            });
        services.AddSingleton<LanguageProviderRouter>();
        services.AddSingleton<ILanguageImprovementProvider>(
            provider => provider.GetRequiredService<LanguageProviderRouter>());
        services.AddSingleton<IConversationProvider>(
            provider => provider.GetRequiredService<LanguageProviderRouter>());
        services.AddSingleton<IWordDefinitionProvider>(
            provider => provider.GetRequiredService<LanguageProviderRouter>());

#if WINDOWS
        services.AddSingleton<ISecretStore, DpapiSecretStore>();
        services.AddSingleton<IWindowController, WindowController>();
#else
        services.AddSingleton<ISecretStore, MauiSecureSecretStore>();
        services.AddSingleton<IWindowController, PortableWindowController>();
        services.AddSingleton<IDesktopTrayService, NullDesktopTrayService>();
#endif
        services.AddSingleton<UiLocalizationService>();
        services.AddSingleton<LanguagePreferences>();
        services.AddSingleton<LocalSetupCoordinator>();
        services.AddSingleton<BuddyRuntimeState>();
        services.AddSingleton<SpeechProcessingCoordinator>();
        services.AddSingleton<RecordingCoordinator>();
        services.AddSingleton<DialogCoordinator>();
        services.AddSingleton<DialogSpeechCacheService>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<FeedbackViewModel>();
        services.AddSingleton<DialogViewModel>();
        services.AddSingleton<OnboardingViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainPage>();
        return services;
    }
}
