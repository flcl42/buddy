using Buddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Buddy.App.ViewModels;

public sealed partial class RecordingCardViewModel : ObservableObject
{
    public RecordingCardViewModel(
        Recording recording,
        AudioArtifact? playbackArtifact = null,
        AudioWaveform? waveform = null)
    {
        ArgumentNullException.ThrowIfNull(recording);

        Id = recording.Id;
        Title = recording.DisplayTitle;
        DateText = recording.CaptureStartedAt.ToString(
            "ddd, d MMM · HH:mm",
            System.Globalization.CultureInfo.CurrentCulture);
        SourceText = recording.Kind switch
        {
            RecordingKind.Meeting => "Meeting",
            RecordingKind.Trainer => "Monologue",
            RecordingKind.Dialog => "Dialog",
            _ => "Recording",
        };
        IsDialog = recording.Kind == RecordingKind.Dialog;
        DurationText = FormatDuration(recording.WallDuration);
        SpeechDurationText = recording.SpeechDuration > TimeSpan.Zero
            ? $"{FormatDuration(recording.SpeechDuration)} speech"
            : recording.Status is RecordingStatus.Ready
                or RecordingStatus.NeedsAttention
                ? "No speech detected"
                : "Analyzing speech";
        StatusText = HumanizeStatus(recording.Status);
        HasWarning = recording.Status == RecordingStatus.NeedsAttention;
        IsProcessing = recording.Status is not RecordingStatus.Ready
            and not RecordingStatus.ReadyForPlayback
            and not RecordingStatus.NeedsAttention;
        CanPlay = recording.Status is RecordingStatus.ReadyForPlayback
            or RecordingStatus.DetectingSpeech
            or RecordingStatus.BuildingCompactAudio
            or RecordingStatus.Transcribing
            or RecordingStatus.Titling
            or RecordingStatus.Ready
            or RecordingStatus.NeedsAttention;
        PlaybackArtifactId = playbackArtifact?.Id;
        PlaybackArtifactRelativePath = playbackArtifact?.RelativePath;
        PlaybackDuration = playbackArtifact?.Duration ?? recording.WallDuration;
        WaveformSamples = waveform?.Normalize() ?? CreatePlaceholderWaveform();
    }

    public Guid Id { get; }

    public string Title { get; }

    public string DateText { get; }

    public string SourceText { get; }

    public bool IsDialog { get; }

    public string DurationText { get; }

    public string SpeechDurationText { get; }

    public string StatusText { get; }

    public bool HasWarning { get; }

    public bool IsProcessing { get; }

    public bool CanPlay { get; }

    public Guid? PlaybackArtifactId { get; }

    public string? PlaybackArtifactRelativePath { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayButtonText))]
    [NotifyPropertyChangedFor(nameof(PlayIcon))]
    public partial bool IsPlaying { get; set; }

    public string PlayButtonText => IsPlaying ? "Pause" : "Play";

    public string PlayIcon => IsPlaying ? "Ⅱ" : "▶";

    [ObservableProperty]
    public partial IReadOnlyList<float> WaveformSamples { get; set; }

    [ObservableProperty]
    public partial double PlaybackProgress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimelineText))]
    public partial TimeSpan PlaybackPosition { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimelineText))]
    public partial TimeSpan PlaybackDuration { get; set; }

    public string TimelineText =>
        $"{FormatDuration(PlaybackPosition)} / {FormatDuration(PlaybackDuration)}";

    private static float[] CreatePlaceholderWaveform()
    {
        float[] samples = new float[AudioWaveform.DefaultSampleCount];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = 0.12f + (index % 5) * 0.025f;
        }

        return samples;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string HumanizeStatus(RecordingStatus status)
    {
        return status switch
        {
            RecordingStatus.FinalizingSource => "Saving source",
            RecordingStatus.ReadyForPlayback => "Ready to play",
            RecordingStatus.DetectingSpeech => "Finding speech",
            RecordingStatus.BuildingCompactAudio => "Removing pauses",
            RecordingStatus.Transcribing => "Transcribing",
            RecordingStatus.Titling => "Creating title",
            RecordingStatus.NeedsAttention => "Needs attention",
            RecordingStatus.Interrupted => "Recovery needed",
            RecordingStatus.Recovering => "Recovering",
            RecordingStatus.Capturing => "Recording",
            RecordingStatus.Ready => "Ready",
            RecordingStatus.Deleted => "Deleted",
            _ => status.ToString(),
        };
    }
}

public sealed record WaveformSeekRequest(
    RecordingCardViewModel Recording,
    double Fraction);
