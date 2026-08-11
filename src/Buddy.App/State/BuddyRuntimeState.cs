using Buddy.App.Services;
using Buddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Buddy.App.State;

public enum BuddyRuntimeMode
{
    Idle = 0,
    Recording = 1,
    Processing = 2,
    Attention = 3,
}

public sealed partial class BuddyRuntimeState : ObservableObject
{
    private readonly UiLocalizationService _localization;

    public BuddyRuntimeState(UiLocalizationService localization)
    {
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));
        _localization.Changed += OnLocalizationChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayToolTip))]
    [NotifyPropertyChangedFor(nameof(IsRecording))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial BuddyRuntimeMode Mode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayToolTip))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial TimeSpan RecordingElapsed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayToolTip))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? AttentionMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayToolTip))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial RecordingKind? ActiveRecordingKind { get; set; }

    [ObservableProperty]
    public partial string? RecordingDeviceName { get; set; }

    [ObservableProperty]
    public partial float RecordingPeak { get; set; }

    public bool IsRecording => Mode == BuddyRuntimeMode.Recording;

    public string TrayToolTip => Mode switch
    {
        BuddyRuntimeMode.Recording when ActiveRecordingKind == RecordingKind.Dialog =>
            FormatTrayStatus("RuntimeDialogFormat"),
        BuddyRuntimeMode.Recording =>
            FormatTrayStatus("RuntimeRecordingFormat"),
        BuddyRuntimeMode.Processing =>
            $"{_localization.Get("AppName")} · {_localization.Get("RuntimeProcessing")}",
        BuddyRuntimeMode.Attention =>
            $"{_localization.Get("AppName")} · "
                + (AttentionMessage ?? _localization.Get("PhaseNeedsAttention")),
        _ => $"{_localization.Get("AppName")} · "
            + _localization.Get("PhaseReady"),
    };

    public string StatusText => Mode switch
    {
        BuddyRuntimeMode.Recording when ActiveRecordingKind == RecordingKind.Dialog =>
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _localization.Get("RuntimeDialogFormat"),
                FormatDuration(RecordingElapsed)),
        BuddyRuntimeMode.Recording => string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            _localization.Get("RuntimeRecordingFormat"),
            FormatDuration(RecordingElapsed)),
        BuddyRuntimeMode.Processing => _localization.Get("RuntimeProcessing"),
        BuddyRuntimeMode.Attention =>
            AttentionMessage ?? _localization.Get("PhaseNeedsAttention"),
        _ => _localization.Get("PhaseReady"),
    };

    private string FormatTrayStatus(string resourceKey) =>
        $"{_localization.Get("AppName")} · "
        + string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            _localization.Get(resourceKey),
            FormatDuration(RecordingElapsed));

    private void OnLocalizationChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(TrayToolTip));
        OnPropertyChanged(nameof(StatusText));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : duration.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}
