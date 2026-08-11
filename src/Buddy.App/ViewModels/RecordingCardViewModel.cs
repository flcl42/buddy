using Buddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Buddy.App.ViewModels;

public sealed partial class RecordingCardViewModel : ObservableObject
{
    public RecordingCardViewModel(
        Recording recording,
        AudioArtifact? playbackArtifact = null,
        AudioWaveform? waveform = null,
        TranscriptRevision? sourceTranscript = null)
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
        TimeSpan canonicalDuration = playbackArtifact?.Duration
            ?? recording.WallDuration;
        bool isCompact = playbackArtifact?.Kind == AudioArtifactKind.Compact;
        DurationText = FormatDuration(canonicalDuration);
        SpeechDurationText = recording.SpeechDuration > TimeSpan.Zero
            ? isCompact
                ? $"{FormatDuration(recording.WallDuration)} captured · "
                    + $"{FormatDuration(recording.SpeechDuration)} speech"
                : $"{FormatDuration(recording.SpeechDuration)} speech"
            : recording.Status is RecordingStatus.Ready
                or RecordingStatus.NeedsAttention
                ? "No speech detected"
                : "Analyzing speech";
        StatusText = HumanizeStatus(recording.Status);
        HasWarning = recording.Status == RecordingStatus.NeedsAttention;
        IsProcessing = recording.Status is not RecordingStatus.Ready
            and not RecordingStatus.ReadyForPlayback
            and not RecordingStatus.NeedsAttention;
        IsTranscribing = recording.Status == RecordingStatus.Transcribing;
        CanPlay = recording.Status is RecordingStatus.ReadyForPlayback
            or RecordingStatus.DetectingSpeech
            or RecordingStatus.BuildingCompactAudio
            or RecordingStatus.Transcribing
            or RecordingStatus.Titling
            or RecordingStatus.Ready
            or RecordingStatus.NeedsAttention;
        PlaybackArtifactId = playbackArtifact?.Id;
        PlaybackArtifactRelativePath = playbackArtifact?.RelativePath;
        PlaybackDuration = canonicalDuration;
        WaveformSamples = waveform?.Normalize() ?? CreatePlaceholderWaveform();
        TranscriptRevisionId = sourceTranscript?.Id;
        TranscriptText = sourceTranscript?.Text ?? string.Empty;
        OriginalTranscriptText = TranscriptText;
        TranscriptProvenanceText = sourceTranscript is null
            ? string.Empty
            : CreateTranscriptProvenance(sourceTranscript);
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

    public bool IsTranscribing { get; }

    public bool CanPlay { get; }

    public Guid? PlaybackArtifactId { get; }

    public string? PlaybackArtifactRelativePath { get; }

    public Guid? TranscriptRevisionId { get; private set; }

    public string OriginalTranscriptText { get; private set; }

    public string TranscriptProvenanceText { get; private set; }

    [ObservableProperty]
    public partial bool IsTranscriptExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTranscript))]
    [NotifyPropertyChangedFor(nameof(NeedsTranscript))]
    [NotifyPropertyChangedFor(nameof(IsTranscriptDirty))]
    [NotifyPropertyChangedFor(nameof(CanSaveTranscript))]
    public partial string TranscriptText { get; set; }

    public bool HasTranscript => !string.IsNullOrWhiteSpace(TranscriptText);

    public bool NeedsTranscript => !HasTranscript;

    public bool IsTranscriptDirty => !string.Equals(
        TranscriptText.Trim(),
        OriginalTranscriptText,
        StringComparison.Ordinal);

    public bool CanSaveTranscript => HasTranscript && IsTranscriptDirty;

    public bool CanRequestTranscription => CanPlay && !IsTranscribing;

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

    public RecordingTranscriptUiState CaptureTranscriptUiState() => new(
        IsTranscriptExpanded,
        IsTranscriptDirty ? TranscriptText : null,
        TranscriptRevisionId);

    public void RestoreTranscriptUiState(RecordingTranscriptUiState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        IsTranscriptExpanded = state.IsExpanded;
        if (state.DraftText is not null
            && state.SourceRevisionId == TranscriptRevisionId)
        {
            TranscriptText = state.DraftText;
        }
    }

    public void AcceptSavedTranscript(TranscriptRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        TranscriptRevisionId = revision.Id;
        OriginalTranscriptText = revision.Text;
        TranscriptText = revision.Text;
        TranscriptProvenanceText = CreateTranscriptProvenance(revision);
        OnPropertyChanged(nameof(TranscriptRevisionId));
        OnPropertyChanged(nameof(TranscriptProvenanceText));
        OnPropertyChanged(nameof(IsTranscriptDirty));
        OnPropertyChanged(nameof(CanSaveTranscript));
    }

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

    private static string CreateTranscriptProvenance(
        TranscriptRevision revision)
    {
        string source = string.Join(
            " · ",
            new[] { revision.Provider, revision.Model }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        string timestamp = revision.CreatedAt.ToString(
            "d MMM · HH:mm",
            System.Globalization.CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(source)
            ? timestamp
            : $"{source} · {timestamp}";
    }
}

public sealed record RecordingTranscriptUiState(
    bool IsExpanded,
    string? DraftText,
    Guid? SourceRevisionId);

public sealed record WaveformSeekRequest(
    RecordingCardViewModel Recording,
    double Fraction);
