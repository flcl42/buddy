using System.Text.RegularExpressions;

namespace Buddy.Speech;

internal static partial class KokoroSynthesisTextPreprocessor
{
    private static readonly Dictionary<string, string>
        Contractions = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["ain't"] = "is not",
            ["aren't"] = "are not",
            ["can't"] = "cannot",
            ["can't've"] = "cannot have",
            ["couldn't"] = "could not",
            ["couldn't've"] = "could not have",
            ["daren't"] = "dare not",
            ["didn't"] = "did not",
            ["doesn't"] = "does not",
            ["don't"] = "do not",
            ["hadn't"] = "had not",
            ["hasn't"] = "has not",
            ["haven't"] = "have not",
            ["isn't"] = "is not",
            ["mightn't"] = "might not",
            ["mightn't've"] = "might not have",
            ["mustn't"] = "must not",
            ["mustn't've"] = "must not have",
            ["needn't"] = "need not",
            ["oughtn't"] = "ought not",
            ["shan't"] = "shall not",
            ["shouldn't"] = "should not",
            ["shouldn't've"] = "should not have",
            ["wasn't"] = "was not",
            ["weren't"] = "were not",
            ["won't"] = "will not",
            ["won't've"] = "will not have",
            ["wouldn't"] = "would not",
            ["wouldn't've"] = "would not have",
        };

    public static string ApplyEnglishContractionSpeechForms(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return SpeechTokenRegex().Replace(
            text,
            ReplaceToken);
    }

    private static string ReplaceToken(Match match)
    {
        if (match.Groups["literal"].Success)
        {
            return match.Value;
        }

        string written = match.Groups["contraction"].Value;
        string canonical = written
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'')
            .Replace('\u02BC', '\'')
            .Replace('\uFF07', '\'');
        if (!Contractions.TryGetValue(
                canonical,
                out string? speechForm))
        {
            return match.Value;
        }

        return char.IsUpper(written[0])
            ? char.ToUpperInvariant(speechForm[0]) + speechForm[1..]
            : speechForm;
    }

    [GeneratedRegex(
        @"(?<literal>\[[^\]\r\n]+\]\(/[^\r\n()]+/\))|(?<![\p{L}\p{N}_])(?<contraction>[\p{L}]+(?:['\u2018\u2019\u02BC\uFF07][\p{L}]+)+)(?![\p{L}\p{N}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex SpeechTokenRegex();

}
