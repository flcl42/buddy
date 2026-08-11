using Buddy.Core.Abstractions;
using Buddy.Language;
using Buddy.Speech;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Buddy.App.Services;

public sealed partial class LocalSetupCoordinator : ObservableObject, IAsyncDisposable
{
    private readonly ILocalModelManager _models;
    private readonly IQwenModelInstaller _qwen;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _activeCancellation;
    private long _noticeVersion;

    public LocalSetupCoordinator(
        ILocalModelManager models,
        IQwenModelInstaller qwen)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _qwen = qwen ?? throw new ArgumentNullException(nameof(qwen));
    }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "Preparing local AI";

    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    public bool CanCancel => IsActive;

    public Task EnsureSpeechRecognitionAsync(
        CancellationToken cancellationToken = default) =>
        RunAsync(
            "Preparing private speech recognition",
            async (report, token) =>
            {
                IReadOnlyList<LocalModelInfo> models = await _models
                    .GetModelsAsync(token)
                    .ConfigureAwait(false);
                LocalModelInfo whisper = GetModel(
                    models,
                    LocalSpeechModels.WhisperLargeV3Turbo);
                LocalModelInfo vad = GetModel(models, LocalSpeechModels.SileroVad);
                long total = checked(whisper.DownloadBytes + vad.DownloadBytes);
                await _models
                    .EnsureInstalledAsync(
                        whisper.Id,
                        new Progress<double>(
                            value => report(
                                Math.Clamp(value, 0, 1)
                                    * whisper.DownloadBytes / total,
                                $"Whisper large-v3-turbo · {value:P0}")),
                        token)
                    .ConfigureAwait(false);
                await _models
                    .EnsureInstalledAsync(
                        vad.Id,
                        new Progress<double>(
                            value => report(
                                (whisper.DownloadBytes
                                    + Math.Clamp(value, 0, 1) * vad.DownloadBytes)
                                    / total,
                                $"Silero voice detection · {value:P0}")),
                        token)
                    .ConfigureAwait(false);
            },
            "Private speech recognition is ready",
            cancellationToken);

    public Task EnsureSpeechSynthesisAsync(
        CancellationToken cancellationToken = default) =>
        RunAsync(
            "Preparing the local Buddy voice",
            async (report, token) =>
            {
                await _models
                    .EnsureInstalledAsync(
                        LocalSpeechModels.KokoroEnglishV1,
                        new Progress<double>(
                            value => report(
                                value,
                                $"Kokoro multilingual voice · {value:P0}")),
                        token)
                    .ConfigureAwait(false);
            },
            "The local Buddy voice is ready",
            cancellationToken);

    public Task EnsureQwenAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            "Installing Qwen 3.6 27B locally",
            async (report, token) =>
            {
                await _qwen
                    .EnsureInstalledAsync(
                        new Progress<QwenInstallProgress>(
                            value => report(value.Fraction, value.Stage)),
                        token)
                    .ConfigureAwait(false);
            },
            "Qwen local AI is installed and verified",
            cancellationToken);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _activeCancellation?.Cancel();
    }

    private async Task RunAsync(
        string title,
        Func<Action<double, string>, CancellationToken, Task> operation,
        string completionMessage,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCancellation = linked;
            long version = Interlocked.Increment(ref _noticeVersion);
            UpdateUi(
                () =>
                {
                    Title = title;
                    Detail = "Checking existing files…";
                    Progress = 0;
                    IsVisible = true;
                    IsActive = true;
                });
            try
            {
                await operation(
                        (fraction, detail) => UpdateUi(
                            () =>
                            {
                                Progress = Math.Clamp(fraction, 0, 1);
                                Detail = detail;
                            }),
                        linked.Token)
                    .ConfigureAwait(false);
                UpdateUi(
                    () =>
                    {
                        Progress = 1;
                        Detail = completionMessage;
                    });
                _ = HideNoticeLaterAsync(version);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                UpdateUi(
                    () =>
                    {
                        Detail = "Setup paused. The partial download was kept and will resume.";
                    });
                _ = HideNoticeLaterAsync(version);
                throw;
            }
            catch (Exception error) when (
                error is HttpRequestException
                    or IOException
                    or InvalidDataException
                    or UnauthorizedAccessException)
            {
                UpdateUi(
                    () =>
                    {
                        Detail = $"Setup needs attention · {error.Message}";
                    });
                throw;
            }
            finally
            {
                _activeCancellation = null;
                UpdateUi(() => IsActive = false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task HideNoticeLaterAsync(long version)
    {
        await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        if (Interlocked.Read(ref _noticeVersion) == version && !IsActive)
        {
            UpdateUi(() => IsVisible = false);
        }
    }

    private static LocalModelInfo GetModel(
        IReadOnlyList<LocalModelInfo> models,
        string id)
    {
        return models.FirstOrDefault(model => model.Id == id)
            ?? throw new InvalidOperationException($"Local model '{id}' is unavailable.");
    }

    private static void UpdateUi(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(action);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _activeCancellation?.Cancel();
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
