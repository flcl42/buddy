using Buddy.App.Controls;
using Buddy.App.Platforms.Windows;
using Buddy.App.Services;
using Buddy.App.State;
using Buddy.App.ViewModels;
using Buddy.App.WinUI;
using Buddy.Audio.Windows;
using Buddy.Core.Abstractions;
using Buddy.Language;
using Buddy.Persistence;
using Buddy.Speech;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;

namespace Buddy.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        StartupDiagnostics.Write("MauiProgram.CreateMauiApp building services");
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseNotifyIcon();
        builder.ConfigureMauiHandlers(
            handlers => handlers.AddHandler<
                MarkdownMessageView,
                MarkdownMessageViewHandler>());

#if DEBUG
        builder.Logging.AddDebug();
#endif

        string localAppDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Buddy");
        bool hasHeavyDataDrive = Directory.Exists(@"H:\");
        string dataRoot = Environment.GetEnvironmentVariable("BUDDY_DATA_ROOT")
            ?? (hasHeavyDataDrive ? @"H:\Buddy" : localAppDataRoot);
        string languageRoot = Environment.GetEnvironmentVariable("BUDDY_AI_ROOT")
            ?? (hasHeavyDataDrive
                ? @"H:\BuddyAI"
                : Path.Combine(dataRoot, "language-models"));
        builder.Services.AddSingleton(new BuddyDataPaths(dataRoot));
        builder.Services.AddSingleton<SqliteConnectionFactory>();
        builder.Services.AddSingleton<BuddyDatabase>();
        builder.Services.AddSingleton<IBuddyDatabase>(
            services => services.GetRequiredService<BuddyDatabase>());
        builder.Services.AddSingleton<SqliteRecordingRepository>();
        builder.Services.AddSingleton<IRecordingRepository>(
            services => services.GetRequiredService<SqliteRecordingRepository>());
        builder.Services.AddSingleton<SqliteDialogRepository>();
        builder.Services.AddSingleton<IDialogRepository>(
            services => services.GetRequiredService<SqliteDialogRepository>());
        builder.Services.AddSingleton<SqliteBackgroundJobStore>();
        builder.Services.AddSingleton<IBackgroundJobStore>(
            services => services.GetRequiredService<SqliteBackgroundJobStore>());
        builder.Services.AddSingleton<SqliteAppSettingsStore>();
        builder.Services.AddSingleton<IAppSettingsStore>(
            services => services.GetRequiredService<SqliteAppSettingsStore>());
        builder.Services.AddSingleton<JsonCaptureJournalStore>();
        builder.Services.AddSingleton<ICaptureJournalStore>(
            services => services.GetRequiredService<JsonCaptureJournalStore>());
        builder.Services.AddSingleton<WasapiAudioCaptureService>();
        builder.Services.AddSingleton<IAudioCaptureService>(
            services => services.GetRequiredService<WasapiAudioCaptureService>());
        builder.Services.AddSingleton<WasapiAudioInputTestService>();
        builder.Services.AddSingleton<IAudioInputTestService>(
            services => services.GetRequiredService<WasapiAudioInputTestService>());
        builder.Services.AddSingleton<OggOpusAudioArchiveService>();
        builder.Services.AddSingleton<IAudioArchiveService>(
            services => services.GetRequiredService<OggOpusAudioArchiveService>());
        builder.Services.AddSingleton<NAudioPlaybackService>();
        builder.Services.AddSingleton<IAudioPlaybackService>(
            services => services.GetRequiredService<NAudioPlaybackService>());
        builder.Services.AddSingleton<SpeechAudioPreparationService>();
        builder.Services.AddSingleton<IAudioPreparationService>(
            services => services.GetRequiredService<SpeechAudioPreparationService>());
        builder.Services.AddSingleton<AudioWaveformService>();
        builder.Services.AddSingleton<IAudioWaveformService>(
            services => services.GetRequiredService<AudioWaveformService>());
        builder.Services.AddSingleton(
            _ => new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan,
            });
        builder.Services.AddSingleton<BuddyProxyClientConfiguration>();
        builder.Services.AddSingleton<VerifiedLocalModelManager>(
            services => new VerifiedLocalModelManager(
                services.GetRequiredService<BuddyDataPaths>().Models,
                services.GetRequiredService<HttpClient>()));
        builder.Services.AddSingleton<ILocalModelManager>(
            services => services.GetRequiredService<VerifiedLocalModelManager>());
        builder.Services.AddSingleton<WhisperVoiceActivityService>();
        builder.Services.AddSingleton<IVoiceActivityService>(
            services => services.GetRequiredService<WhisperVoiceActivityService>());
        builder.Services.AddSingleton<WhisperTranscriptionService>();
        builder.Services.AddSingleton<ITranscriptionService>(
            services => services.GetRequiredService<WhisperTranscriptionService>());
        builder.Services.AddSingleton<KokoroPhoneticTranscriptionService>();
        builder.Services.AddSingleton<IPhoneticTranscriptionService>(
            services => services
                .GetRequiredService<KokoroPhoneticTranscriptionService>());
        builder.Services.AddSingleton<KokoroSpeechSynthesisService>();
        builder.Services.AddSingleton<WindowsSpeechSynthesisService>();
        builder.Services.AddSingleton<LocalSpeechSynthesisService>();
        builder.Services.AddSingleton<ISpeechSynthesisService>(
            services => services.GetRequiredService<LocalSpeechSynthesisService>());
        builder.Services.AddSingleton(
            new QwenRuntimeOptions(
                Path.Combine(languageRoot, "llama.cpp", "b10243"),
                Path.Combine(
                    languageRoot,
                    "models",
                    "Qwen3.6-27B-Q4_K_M.gguf"),
                Path.Combine(languageRoot, "logs"),
                DraftModelPath: Path.Combine(
                    languageRoot,
                    "models",
                    "dflash-Qwen3.6-27B-Q8_0.gguf")));
        builder.Services.AddSingleton<QwenModelRuntime>();
        builder.Services.AddSingleton<IQwenModelRuntime>(
            services => services.GetRequiredService<QwenModelRuntime>());
        builder.Services.AddSingleton<QwenModelInstaller>();
        builder.Services.AddSingleton<IQwenModelInstaller>(
            services => services.GetRequiredService<QwenModelInstaller>());
        builder.Services.AddSingleton<QwenLanguageProvider>();
        builder.Services.AddSingleton<DeepSeekLanguageProvider>();
        builder.Services.AddSingleton<BuddyProxyLanguageProvider>(
            services =>
            {
                BuddyProxyClientConfiguration configuration = services
                    .GetRequiredService<BuddyProxyClientConfiguration>();
                return new BuddyProxyLanguageProvider(
                    configuration.CreateHttpClient(),
                    services.GetRequiredService<ISecretStore>(),
                    configuration.Endpoint,
                    configuration.IncludedApiKey);
            });
        builder.Services.AddSingleton<LanguageProviderRouter>();
        builder.Services.AddSingleton<ILanguageImprovementProvider>(
            services => services.GetRequiredService<LanguageProviderRouter>());
        builder.Services.AddSingleton<IConversationProvider>(
            services => services.GetRequiredService<LanguageProviderRouter>());
        builder.Services.AddSingleton<IWordDefinitionProvider>(
            services => services.GetRequiredService<LanguageProviderRouter>());
        builder.Services.AddSingleton<ISecretStore, DpapiSecretStore>();
        builder.Services.AddSingleton<UiLocalizationService>();
        builder.Services.AddSingleton<LanguagePreferences>();
        builder.Services.AddSingleton<LocalSetupCoordinator>();
        builder.Services.AddSingleton<IWindowController, WindowController>();
        builder.Services.AddSingleton<BuddyRuntimeState>();
        builder.Services.AddSingleton<SpeechProcessingCoordinator>();
        builder.Services.AddSingleton<RecordingCoordinator>();
        builder.Services.AddSingleton<DialogCoordinator>();
        builder.Services.AddSingleton<DialogSpeechCacheService>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<DialogViewModel>();
        builder.Services.AddSingleton<OnboardingViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

        MauiApp app = builder.Build();
        StartupDiagnostics.Write("MauiProgram.CreateMauiApp complete");
        return app;
    }
}
