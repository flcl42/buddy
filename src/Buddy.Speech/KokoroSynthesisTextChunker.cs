namespace Buddy.Speech;

internal static class KokoroSynthesisTextChunker
{
    internal const int DefaultMaximumCharacters = 320;
    private const int MinimumPreferredBreakCharacters = 96;

    public static IReadOnlyList<string> Split(
        string text,
        int maximumCharacters = DefaultMaximumCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (maximumCharacters < MinimumPreferredBreakCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCharacters),
                $"Speech chunks must allow at least "
                    + $"{MinimumPreferredBreakCharacters} characters.");
        }

        string remaining = text.Trim();
        List<string> chunks = [];
        while (remaining.Length > maximumCharacters)
        {
            int breakIndex = FindBreakIndex(remaining, maximumCharacters);
            string chunk = remaining[..breakIndex].Trim();
            if (chunk.Length == 0)
            {
                breakIndex = maximumCharacters;
                chunk = remaining[..breakIndex];
            }

            chunks.Add(chunk);
            remaining = remaining[breakIndex..].TrimStart();
        }

        if (remaining.Length > 0)
        {
            chunks.Add(remaining);
        }

        return chunks;
    }

    private static int FindBreakIndex(string text, int maximumCharacters)
    {
        int minimum = Math.Min(
            MinimumPreferredBreakCharacters,
            maximumCharacters / 2);

        int sentenceBreak = FindWhitespaceBoundary(
            text,
            maximumCharacters,
            minimum,
            IsSentenceEndingBoundary);
        if (sentenceBreak > 0)
        {
            return sentenceBreak;
        }

        int lineBreak = FindWhitespaceBoundary(
            text,
            maximumCharacters,
            minimum,
            static (value, index) => value[index] is '\r' or '\n');
        if (lineBreak > 0)
        {
            return lineBreak;
        }

        int clauseBreak = FindWhitespaceBoundary(
            text,
            maximumCharacters,
            minimum,
            IsClauseEndingBoundary);
        if (clauseBreak > 0)
        {
            return clauseBreak;
        }

        for (int index = maximumCharacters; index >= minimum; index--)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        return maximumCharacters;
    }

    private static int FindWhitespaceBoundary(
        string text,
        int maximumCharacters,
        int minimumCharacters,
        Func<string, int, bool> predicate)
    {
        for (int index = maximumCharacters; index >= minimumCharacters; index--)
        {
            if (char.IsWhiteSpace(text[index]) && predicate(text, index))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsSentenceEndingBoundary(string text, int whitespaceIndex)
    {
        int index = SkipClosingPunctuation(text, whitespaceIndex - 1);
        return index >= 0 && text[index] is '.' or '?' or '!';
    }

    private static bool IsClauseEndingBoundary(string text, int whitespaceIndex)
    {
        int index = SkipClosingPunctuation(text, whitespaceIndex - 1);
        return index >= 0 && text[index] is ',' or ';' or ':' or '—' or '–';
    }

    private static int SkipClosingPunctuation(string text, int index)
    {
        while (index >= 0
               && text[index] is '"' or '\'' or '”' or '’' or ')' or ']' or '}')
        {
            index--;
        }

        return index;
    }
}
