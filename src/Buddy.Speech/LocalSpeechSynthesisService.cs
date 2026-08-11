using Buddy.Core.Abstractions;

namespace Buddy.Speech;

public sealed class LocalSpeechSynthesisService : ISpeechSynthesisService
{
    public const string SynthesisVersion = "buddy.local-speech.multilingual.v1";

    private readonly KokoroSpeechSynthesisService _kokoro;
    private readonly IReadOnlyList<IPlatformSpeechSynthesisService> _platforms;

    public LocalSpeechSynthesisService(
        KokoroSpeechSynthesisService kokoro,
        IEnumerable<IPlatformSpeechSynthesisService> platforms)
    {
        _kokoro = kokoro ?? throw new ArgumentNullException(nameof(kokoro));
        ArgumentNullException.ThrowIfNull(platforms);
        _platforms = platforms.ToArray();
    }

    public async Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SpeechVoice> kokoro = await _kokoro
            .GetVoicesAsync(cancellationToken)
            .ConfigureAwait(false);
        List<SpeechVoice> voices = new(kokoro);
        foreach (IPlatformSpeechSynthesisService platform in _platforms)
        {
            voices.AddRange(
                await platform
                    .GetVoicesAsync(cancellationToken)
                    .ConfigureAwait(false));
        }

        return voices;
    }

    public Task<SpeechSynthesisResult> SynthesizeAsync(
        string text,
        string outputPath,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        IPlatformSpeechSynthesisService? platform = _platforms.FirstOrDefault(
            candidate => candidate.CanSynthesize(options.VoiceId));
        if (platform is not null)
        {
            return platform.SynthesizeAsync(
                text,
                outputPath,
                options,
                cancellationToken);
        }

        if (options.VoiceId.Contains(':', StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Voice '{options.VoiceId}' is not available on this platform.");
        }

        return _kokoro.SynthesizeAsync(
            text,
            outputPath,
            options,
            cancellationToken);
    }
}
