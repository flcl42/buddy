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
            $"Buddy · AI Dialog · {FormatDuration(RecordingElapsed)}",
        BuddyRuntimeMode.Recording => $"Buddy · Recording · {FormatDuration(RecordingElapsed)}",
        BuddyRuntimeMode.Processing => "Buddy · Processing locally",
        BuddyRuntimeMode.Attention => $"Buddy · {AttentionMessage ?? "Needs attention"}",
        _ => "Buddy · Ready",
    };

    public string StatusText => Mode switch
    {
        BuddyRuntimeMode.Recording when ActiveRecordingKind == RecordingKind.Dialog =>
            $"AI Dialog {FormatDuration(RecordingElapsed)}",
        BuddyRuntimeMode.Recording => $"Recording {FormatDuration(RecordingElapsed)}",
        BuddyRuntimeMode.Processing => "Processing locally",
        BuddyRuntimeMode.Attention => AttentionMessage ?? "Needs attention",
        _ => "Ready",
    };

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : duration.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}
