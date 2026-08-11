using Buddy.Core.Abstractions;
using KokoroSharp.Processing;

namespace Buddy.Speech;

public sealed class KokoroPhoneticTranscriptionService :
    IPhoneticTranscriptionService
{
    private const int MaximumInputCharacters = 12_000;

    public async Task<string> TranscribeAsync(
        string text,
        string locale = "en-US",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > MaximumInputCharacters)
        {
            throw new ArgumentException(
                $"Phonetic input cannot exceed {MaximumInputCharacters:N0} characters.",
                nameof(text));
        }

        string language = locale.ToLowerInvariant() switch
        {
            "en-gb" => "en-gb",
            "de-de" => "de",
            "es-es" => "es",
            "fr-fr" => "fr-fr",
            "be-by" => "be",
            _ => "en-us",
        };
        await KokoroTokenizerGate.Instance.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        bool previousNativeEnglish = Tokenizer.UseNativeEnglish;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Tokenizer.eSpeakNGPath = Path.Combine(
                KokoroRuntimeAssets.GetRootPath(),
                "espeak");
            Tokenizer.UseNativeEnglish = false;
            string phonemes = Tokenizer.Phonemize(
                text.Trim(),
                language,
                preprocess: true);
            return phonemes.Trim();
        }
        finally
        {
            Tokenizer.UseNativeEnglish = previousNativeEnglish;
            KokoroTokenizerGate.Instance.Release();
        }
    }
}
