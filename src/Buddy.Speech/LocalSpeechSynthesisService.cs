using Buddy.Core.Abstractions;

namespace Buddy.Speech;

public sealed class LocalSpeechSynthesisService : ISpeechSynthesisService
{
    public const string SynthesisVersion = "buddy.local-speech.multilingual.v1";

    private readonly KokoroSpeechSynthesisService _kokoro;
    private readonly WindowsSpeechSynthesisService _windows;

    public LocalSpeechSynthesisService(
        KokoroSpeechSynthesisService kokoro,
        WindowsSpeechSynthesisService windows)
    {
        _kokoro = kokoro ?? throw new ArgumentNullException(nameof(kokoro));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
    }

    public async Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SpeechVoice> kokoro = await _kokoro
            .GetVoicesAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<SpeechVoice> windows = await _windows
            .GetVoicesAsync(cancellationToken)
            .ConfigureAwait(false);
        return kokoro.Concat(windows).ToArray();
    }

    public Task<SpeechSynthesisResult> SynthesizeAsync(
        string text,
        string outputPath,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.VoiceId.StartsWith(
            WindowsSpeechSynthesisService.VoiceIdPrefix,
            StringComparison.Ordinal)
            ? _windows.SynthesizeAsync(text, outputPath, options, cancellationToken)
            : _kokoro.SynthesizeAsync(text, outputPath, options, cancellationToken);
    }
}
