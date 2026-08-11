using Buddy.Core.Abstractions;

namespace Buddy.Speech;

public sealed class KokoroPhoneticTranscriptionService :
    IPhoneticTranscriptionService
{
    public Task<string> TranscribeAsync(
        string text,
        string locale = "en-US",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(string.Empty);
    }
}

public sealed class KokoroSpeechSynthesisService :
    ISpeechSynthesisService,
    IDisposable
{
    public const string SynthesisVersion = "buddy.maccatalyst-system-speech.v1";

    public KokoroSpeechSynthesisService(ILocalModelManager models)
    {
        ArgumentNullException.ThrowIfNull(models);
    }

    public Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<SpeechVoice>>([]);
    }

    public Task<SpeechSynthesisResult> SynthesizeAsync(
        string text,
        string outputPath,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException(
            "Kokoro is not available in the Mac Catalyst beta. "
            + "Choose a macOS system voice instead.");
    }

    public void Dispose()
    {
    }
}
