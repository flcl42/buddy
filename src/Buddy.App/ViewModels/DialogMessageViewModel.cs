using System.Collections.ObjectModel;
using Buddy.Core.Domain;
using Buddy.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Buddy.App.ViewModels;

public enum AudioTransportState
{
    Idle = 0,
    Preparing = 1,
    Playing = 2,
    Paused = 3,
}

public sealed partial class DialogMessageViewModel : ObservableObject
{
    private DateTimeOffset? _pronunciationCreatedAt;
    private string _phoneticTranscriptText = string.Empty;
    private string _pronunciationSummary = string.Empty;
    private bool _hasPronunciation;
    private bool _hasPronunciationWords;
    private bool _isWordLookupVisible;
    private bool _isWordPhoneticLoading;
    private bool _isWordDefinitionLoading;
    private string _selectedWord = string.Empty;
    private string _wordPhoneticText = string.Empty;
    private string _wordPartOfSpeechText = string.Empty;
    private string _wordDefinitionText = string.Empty;
    private string _wordLookupError = string.Empty;
    private readonly Dictionary<string, CachedWordLookup> _wordLookupCache =
        new(StringComparer.OrdinalIgnoreCase);

    public DialogMessageViewModel(
        DialogMessage message,
        bool isPlaying,
        DialogPronunciationAssessment? pronunciation = null,
        Guid? recordingId = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        Id = message.Id;
        Role = message.Role;
        Text = message.Text;
        RenderedContent = MarkdownTextProcessor.Parse(Text);
        PlainText = RenderedContent.PlainText;
        TimeText = message.CreatedAt.ToString(
            "HH:mm",
            System.Globalization.CultureInfo.CurrentCulture);
        ProviderText = message.Role == DialogMessageRole.Assistant
            && !string.IsNullOrWhiteSpace(message.Model)
                ? message.Model
                : message.Role == DialogMessageRole.User
                    ? "Local Whisper"
                    : "Buddy";
        AudioArtifactId = message.AudioArtifactId;
        RecordingId = recordingId;
        MessageAudioState = isPlaying
            ? AudioTransportState.Playing
            : AudioTransportState.Idle;
        ApplyPronunciation(pronunciation);
    }

    public Guid Id { get; }

    public Guid? RecordingId { get; private set; }

    public DialogMessageRole Role { get; }

    public string Text { get; }

    public MarkdownContentDocument RenderedContent { get; }

    public string PlainText { get; }

    public string TimeText { get; }

    public string ProviderText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAudio))]
    [NotifyPropertyChangedFor(nameof(HasMessageAudioControl))]
    [NotifyPropertyChangedFor(nameof(CanControlMessageAudio))]
    public partial Guid? AudioArtifactId { get; set; }

    public bool IsUser => Role == DialogMessageRole.User;

    public bool IsAssistant => Role == DialogMessageRole.Assistant;

    public bool HasAudio => AudioArtifactId.HasValue;

    public bool IsWordLookupVisible
    {
        get => _isWordLookupVisible;
        private set
        {
            if (SetProperty(ref _isWordLookupVisible, value))
            {
                OnPropertyChanged(nameof(CanControlWordAudio));
            }
        }
    }

    public bool IsWordPhoneticLoading
    {
        get => _isWordPhoneticLoading;
        private set
        {
            if (SetProperty(ref _isWordPhoneticLoading, value))
            {
                OnPropertyChanged(nameof(IsWordLookupLoading));
            }
        }
    }

    public bool IsWordDefinitionLoading
    {
        get => _isWordDefinitionLoading;
        private set
        {
            if (SetProperty(ref _isWordDefinitionLoading, value))
            {
                OnPropertyChanged(nameof(IsWordLookupLoading));
            }
        }
    }

    public bool IsWordLookupLoading =>
        IsWordPhoneticLoading || IsWordDefinitionLoading;

    public string SelectedWord
    {
        get => _selectedWord;
        private set
        {
            if (SetProperty(ref _selectedWord, value))
            {
                OnPropertyChanged(nameof(CanControlWordAudio));
                OnPropertyChanged(nameof(WordAudioSubject));
            }
        }
    }

    public string WordPhoneticText
    {
        get => _wordPhoneticText;
        private set => SetProperty(ref _wordPhoneticText, value);
    }

    public string WordPartOfSpeechText
    {
        get => _wordPartOfSpeechText;
        private set
        {
            if (SetProperty(ref _wordPartOfSpeechText, value))
            {
                OnPropertyChanged(nameof(HasWordPartOfSpeech));
            }
        }
    }

    public bool HasWordPartOfSpeech =>
        !string.IsNullOrWhiteSpace(WordPartOfSpeechText);

    public string WordDefinitionText
    {
        get => _wordDefinitionText;
        private set
        {
            if (SetProperty(ref _wordDefinitionText, value))
            {
                OnPropertyChanged(nameof(HasWordDefinition));
            }
        }
    }

    public bool HasWordDefinition =>
        !string.IsNullOrWhiteSpace(WordDefinitionText);

    public string WordLookupError
    {
        get => _wordLookupError;
        private set
        {
            if (SetProperty(ref _wordLookupError, value))
            {
                OnPropertyChanged(nameof(HasWordLookupError));
            }
        }
    }

    public bool HasWordLookupError =>
        !string.IsNullOrWhiteSpace(WordLookupError);

    public ObservableCollection<PronunciationWordViewModel> PronunciationWords
    {
        get;
    } = [];

    public string PhoneticTranscriptText
    {
        get => _phoneticTranscriptText;
        private set => SetProperty(ref _phoneticTranscriptText, value);
    }

    public string PronunciationSummary
    {
        get => _pronunciationSummary;
        private set => SetProperty(ref _pronunciationSummary, value);
    }

    public bool HasPronunciation
    {
        get => _hasPronunciation;
        private set => SetProperty(ref _hasPronunciation, value);
    }

    public bool HasPronunciationWords
    {
        get => _hasPronunciationWords;
        private set => SetProperty(ref _hasPronunciationWords, value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanControlMessageAudio))]
    public partial AudioTransportState MessageAudioState { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanControlWordAudio))]
    public partial AudioTransportState WordAudioState { get; set; }

    public bool HasMessageAudioControl => IsUser || HasAudio;

    public bool CanControlMessageAudio =>
        HasMessageAudioControl
        && MessageAudioState != AudioTransportState.Preparing;

    public string MessageAudioSubject => IsUser ? "your reply" : "AI answer";

    public bool CanControlWordAudio => IsWordLookupVisible
        && !string.IsNullOrWhiteSpace(SelectedWord)
        && WordAudioState != AudioTransportState.Preparing;

    public string WordAudioSubject => string.IsNullOrWhiteSpace(SelectedWord)
        ? "word"
        : $"word {SelectedWord}";

    public bool IsLookingUpWord(string word)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        return IsWordLookupVisible
            && IsWordLookupLoading
            && string.Equals(
                SelectedWord,
                word.Trim(),
                StringComparison.OrdinalIgnoreCase);
    }

    public bool TryShowCachedWordLookup(string word)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        string selectedWord = word.Trim();
        if (!_wordLookupCache.TryGetValue(
                NormalizeLookupKey(selectedWord),
                out CachedWordLookup? cached))
        {
            return false;
        }

        SelectedWord = selectedWord;
        WordPhoneticText = cached.PhoneticText;
        WordPartOfSpeechText = cached.PartOfSpeech;
        WordDefinitionText = cached.Definition;
        WordLookupError = string.Empty;
        IsWordPhoneticLoading = false;
        IsWordDefinitionLoading = false;
        IsWordLookupVisible = true;
        return true;
    }

    public void BeginWordLookup(string word)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        SelectedWord = word.Trim();
        WordPhoneticText = string.Empty;
        WordPartOfSpeechText = string.Empty;
        WordDefinitionText = string.Empty;
        WordLookupError = string.Empty;
        IsWordPhoneticLoading = true;
        IsWordDefinitionLoading = true;
        IsWordLookupVisible = true;
    }

    public void ApplyWordPhonetic(string word, string phonetic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        if (!IsCurrentWord(word))
        {
            return;
        }

        string normalized = phonetic.Trim().Trim('/');
        WordPhoneticText = normalized.Length == 0
            ? string.Empty
            : $"/{normalized}/";
        IsWordPhoneticLoading = false;
        CacheCompletedLookup();
    }

    public void ApplyWordDefinition(
        string word,
        WordDefinitionResult definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        ArgumentNullException.ThrowIfNull(definition);
        if (!IsCurrentWord(word))
        {
            return;
        }

        WordPartOfSpeechText = definition.PartOfSpeech?.Trim() ?? string.Empty;
        WordDefinitionText = definition.Definition.Trim();
        IsWordDefinitionLoading = false;
        CacheCompletedLookup();
    }

    public void ApplyWordPhoneticError(string word, string message)
    {
        ApplyWordLookupError(word, message, isPhonetic: true);
    }

    public void ApplyWordDefinitionError(string word, string message)
    {
        ApplyWordLookupError(word, message, isPhonetic: false);
    }

    public void CancelWordLookup()
    {
        IsWordPhoneticLoading = false;
        IsWordDefinitionLoading = false;
    }

    [RelayCommand]
    private void DismissWordLookup()
    {
        CancelWordLookup();
        IsWordLookupVisible = false;
    }

    public void UpdateFrom(
        DialogMessage message,
        DialogPronunciationAssessment? pronunciation = null,
        Guid? recordingId = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Id != Id)
        {
            throw new ArgumentException(
                "A dialog message view model cannot change identity.",
                nameof(message));
        }

        AudioArtifactId = message.AudioArtifactId;
        if (recordingId.HasValue)
        {
            RecordingId = recordingId;
        }

        ApplyPronunciation(pronunciation);
    }

    private void ApplyPronunciation(
        DialogPronunciationAssessment? pronunciation)
    {
        if (pronunciation?.CreatedAt == _pronunciationCreatedAt)
        {
            return;
        }

        _pronunciationCreatedAt = pronunciation?.CreatedAt;
        PronunciationWords.Clear();
        if (pronunciation is null)
        {
            PhoneticTranscriptText = string.Empty;
            PronunciationSummary = string.Empty;
            HasPronunciation = false;
            HasPronunciationWords = false;
            return;
        }

        foreach (PronunciationWord word in pronunciation.Words)
        {
            PronunciationWords.Add(new PronunciationWordViewModel(word));
        }

        PhoneticTranscriptText = string.IsNullOrWhiteSpace(
                pronunciation.PhoneticTranscript)
            ? string.Empty
            : $"/{pronunciation.PhoneticTranscript.Trim().Trim('/')}/";
        HasPronunciationWords = PronunciationWords.Count > 0;
        HasPronunciation = IsUser
            && (HasPronunciationWords
                || !string.IsNullOrWhiteSpace(PhoneticTranscriptText));
        PronunciationSummary = HasPronunciationWords
            ? CreateSummary(pronunciation)
            : "IPA guide · word clarity was not captured for this earlier turn";
    }

    private static string CreateSummary(
        DialogPronunciationAssessment assessment)
    {
        string confidence = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{assessment.OverallConfidence * 100:0}% average confidence");
        return $"{confidence} · "
            + $"{assessment.LikelyIssueWordCount} likely unclear · "
            + $"{assessment.ReviewWordCount} review";
    }

    private void ApplyWordLookupError(
        string word,
        string message,
        bool isPhonetic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!IsCurrentWord(word))
        {
            return;
        }

        WordLookupError = string.IsNullOrWhiteSpace(WordLookupError)
            ? message.Trim()
            : $"{WordLookupError} {message.Trim()}";
        if (isPhonetic)
        {
            IsWordPhoneticLoading = false;
        }
        else
        {
            IsWordDefinitionLoading = false;
        }
    }

    private bool IsCurrentWord(string word)
    {
        return IsWordLookupVisible
            && string.Equals(
                SelectedWord,
                word.Trim(),
                StringComparison.OrdinalIgnoreCase);
    }

    private void CacheCompletedLookup()
    {
        if (IsWordLookupLoading
            || string.IsNullOrWhiteSpace(WordPhoneticText)
            || string.IsNullOrWhiteSpace(WordDefinitionText))
        {
            return;
        }

        _wordLookupCache[NormalizeLookupKey(SelectedWord)] = new CachedWordLookup(
            WordPhoneticText,
            WordPartOfSpeechText,
            WordDefinitionText);
    }

    private static string NormalizeLookupKey(string word)
    {
        return word.Trim().ToUpperInvariant();
    }

    private sealed record CachedWordLookup(
        string PhoneticText,
        string PartOfSpeech,
        string Definition);
}
