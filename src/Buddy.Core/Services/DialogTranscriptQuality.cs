using System.Text;

namespace Buddy.Core.Services;

public static class DialogTranscriptQuality
{
    private const int MinimumRepeatedPhraseWords = 3;

    public static bool IsUsable(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return false;
        }

        int letterOrDigitCount = transcript.Count(char.IsLetterOrDigit);
        if (letterOrDigitCount < 2)
        {
            return false;
        }

        string[] words = Tokenize(transcript);
        return !HasRepeatedOuterWord(words)
            && !ContainsAdjacentRepeatedPhrase(words);
    }

    private static string[] Tokenize(string text)
    {
        List<string> words = [];
        StringBuilder current = new();
        foreach (char character in text)
        {
            if (char.IsLetterOrDigit(character) || character == '\'')
            {
                current.Append(char.ToLowerInvariant(character));
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words.ToArray();
    }

    private static bool ContainsAdjacentRepeatedPhrase(string[] words)
    {
        for (int phraseLength = MinimumRepeatedPhraseWords;
             phraseLength * 2 <= words.Length;
             phraseLength++)
        {
            for (int start = 0;
                 start + (phraseLength * 2) <= words.Length;
                 start++)
            {
                bool same = true;
                for (int offset = 0; offset < phraseLength; offset++)
                {
                    if (!string.Equals(
                            words[start + offset],
                            words[start + phraseLength + offset],
                            StringComparison.Ordinal))
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasRepeatedOuterWord(string[] words)
    {
        return words.Length is 3 or 4
            && string.Equals(
                words[0],
                words[^1],
                StringComparison.Ordinal)
            && words[1..^1].Any(
                word => word.Length > words[0].Length
                    && word.StartsWith(
                        words[0],
                        StringComparison.Ordinal));
    }
}
