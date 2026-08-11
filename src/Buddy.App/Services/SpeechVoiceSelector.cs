using Buddy.Core.Abstractions;
using Buddy.Speech;

namespace Buddy.App.Services;

public static class SpeechVoiceSelector
{
    public static SpeechVoice? FindPreferred(
        IReadOnlyList<SpeechVoice> voices,
        DialogLanguageOption language)
    {
        ArgumentNullException.ThrowIfNull(voices);
        ArgumentNullException.ThrowIfNull(language);
        if (voices.Count == 0)
        {
            return null;
        }

        string preferredId = language.Id switch
        {
            "en" => "af_heart",
            "es" => "ef_dora",
            "fr" => "ff_siwis",
            _ => string.Empty,
        };
        SpeechVoice? preferred = voices.FirstOrDefault(
            voice => string.Equals(
                voice.Id,
                preferredId,
                StringComparison.Ordinal));
        if (preferred is not null)
        {
            return preferred;
        }

        if (language.Id == "be")
        {
            preferred = FindPlatformVoiceByLanguage(voices, "be");
            preferred ??= FindPlatformVoiceByLanguage(voices, "ru");
            if (preferred is not null)
            {
                return preferred;
            }
        }

        preferred = voices.FirstOrDefault(
            voice => string.Equals(
                voice.Locale,
                language.Locale,
                StringComparison.OrdinalIgnoreCase));
        if (preferred is not null)
        {
            return preferred;
        }

        string languagePrefix = language.Locale.Split('-', 2)[0];
        preferred = voices.FirstOrDefault(
            voice => string.Equals(
                    voice.Locale,
                    languagePrefix,
                    StringComparison.OrdinalIgnoreCase)
                || voice.Locale.StartsWith(
                    languagePrefix + "-",
                    StringComparison.OrdinalIgnoreCase));
        return preferred;
    }

    public static bool RequiresKokoro(DialogLanguageOption language) =>
        language.Id is "en" or "es" or "fr";

    private static SpeechVoice? FindPlatformVoiceByLanguage(
        IReadOnlyList<SpeechVoice> voices,
        string languagePrefix) =>
        voices.FirstOrDefault(voice =>
            PlatformSpeechVoiceIds.IsPlatformVoice(voice.Id)
            && (string.Equals(
                    voice.Locale,
                    languagePrefix,
                    StringComparison.OrdinalIgnoreCase)
                || voice.Locale.StartsWith(
                    languagePrefix + "-",
                    StringComparison.OrdinalIgnoreCase)));
}
