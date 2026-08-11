using Buddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Buddy.App.ViewModels;

public sealed partial class FeedbackViewModel : ObservableObject, IDisposable
{
    private readonly BuddyFeedbackClient _client;
    private readonly FeedbackAttachmentPicker _attachmentPicker;
    private readonly LanguagePreferences _languages;
    private readonly UiLocalizationService _localization;
    private CancellationTokenSource? _sendCancellation;
    private FeedbackAttachment? _screenshot;
    private int _attachmentSelectionGeneration;
    private int _disposeStarted;

    public FeedbackViewModel(
        BuddyFeedbackClient client,
        FeedbackAttachmentPicker attachmentPicker,
        LanguagePreferences languages,
        UiLocalizationService localization)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _attachmentPicker = attachmentPicker
            ?? throw new ArgumentNullException(nameof(attachmentPicker));
        _languages = languages ?? throw new ArgumentNullException(nameof(languages));
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));
        _localization.Changed += OnLocalizationChanged;
    }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(CharacterCountText))]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScreenshot))]
    public partial string ScreenshotSummary { get; set; } = string.Empty;

    public bool HasScreenshot => _screenshot is not null;

    public bool CanRemoveScreenshot => HasScreenshot && !IsBusy;

    public bool CanEdit => !IsBusy;

    public bool CanSend => !IsBusy
        && !string.IsNullOrWhiteSpace(Message)
        && Message.Length <= BuddyFeedbackClient.MaximumMessageCharacters;

    public string CharacterCountText => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        _localization.Get("FeedbackCharacterCountFormat"),
        Message.Length,
        BuddyFeedbackClient.MaximumMessageCharacters);

    [RelayCommand]
    public void Open()
    {
        StatusMessage = string.Empty;
        IsOpen = true;
    }

    [RelayCommand]
    public void Close()
    {
        _attachmentSelectionGeneration++;
        _sendCancellation?.Cancel();
        ClearScreenshot();
        StatusMessage = string.Empty;
        IsOpen = false;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    public async Task AttachScreenshotAsync()
    {
        StatusMessage = string.Empty;
        int selectionGeneration = _attachmentSelectionGeneration;
        try
        {
            FeedbackAttachment? selected = await _attachmentPicker
                .PickAsync()
                .ConfigureAwait(true);
            if (selected is null)
            {
                return;
            }
            if (!IsOpen || selectionGeneration != _attachmentSelectionGeneration)
            {
                Array.Clear(selected.Content);
                return;
            }

            ClearScreenshot();
            _screenshot = selected;
            ScreenshotSummary = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _localization.Get("FeedbackScreenshotFormat"),
                selected.FileName,
                selected.Content.Length / 1024d / 1024d);
            OnPropertyChanged(nameof(HasScreenshot));
            RemoveScreenshotCommand.NotifyCanExecuteChanged();
        }
        catch (FeedbackAttachmentException error)
        {
            StatusMessage = _localization.Get(
                error.Failure == FeedbackAttachmentFailure.TooLarge
                    ? "FeedbackScreenshotTooLarge"
                    : "FeedbackScreenshotInvalid");
        }
        catch (Exception error) when (
            error is IOException
                or UnauthorizedAccessException
                or FeatureNotSupportedException
                or PermissionException)
        {
            StatusMessage = _localization.Get("FeedbackScreenshotOpenFailed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveScreenshot))]
    public void RemoveScreenshot()
    {
        ClearScreenshot();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task SendAsync()
    {
        _sendCancellation?.Cancel();
        _sendCancellation?.Dispose();
        _sendCancellation = new CancellationTokenSource();
        IsBusy = true;
        StatusMessage = _localization.Get("FeedbackSending");
        try
        {
            FeedbackSubmissionResult result = await _client.SendAsync(
                    Message,
                    _screenshot,
                    AppInfo.Current.VersionString,
                    _languages.InterfaceLanguageId,
                    _languages.DialogLanguage.Id,
                    _sendCancellation.Token)
                .ConfigureAwait(true);
            string statusKey = result.ScreenshotDelivered
                ? "FeedbackSentFormat"
                : "FeedbackSentWithoutScreenshotFormat";
            StatusMessage = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _localization.Get(statusKey),
                result.FeedbackId);
            Message = string.Empty;
            ClearScreenshot();
        }
        catch (OperationCanceledException)
        {
            // Closing the modal cancels quietly.
        }
        catch (FeedbackClientException error)
        {
            StatusMessage = _localization.Get(error.Code == "feedback_auth_missing"
                ? "FeedbackAuthMissing"
                : "FeedbackSendFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _attachmentSelectionGeneration++;
        _localization.Changed -= OnLocalizationChanged;
        _sendCancellation?.Cancel();
        _sendCancellation?.Dispose();
        ClearScreenshot();
    }

    private void ClearScreenshot()
    {
        if (_screenshot is not null)
        {
            Array.Clear(_screenshot.Content);
        }

        _screenshot = null;
        ScreenshotSummary = string.Empty;
        OnPropertyChanged(nameof(HasScreenshot));
        OnPropertyChanged(nameof(CanRemoveScreenshot));
        RemoveScreenshotCommand.NotifyCanExecuteChanged();
    }

    private void OnLocalizationChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(CharacterCountText));
        if (_screenshot is not null)
        {
            ScreenshotSummary = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _localization.Get("FeedbackScreenshotFormat"),
                _screenshot.FileName,
                _screenshot.Content.Length / 1024d / 1024d);
        }
    }

    partial void OnMessageChanged(string value)
    {
        SendCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        SendCommand.NotifyCanExecuteChanged();
        AttachScreenshotCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRemoveScreenshot));
        RemoveScreenshotCommand.NotifyCanExecuteChanged();
    }
}
