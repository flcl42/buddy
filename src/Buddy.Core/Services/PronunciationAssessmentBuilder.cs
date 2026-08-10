using System.Text;
using Buddy.Core.Abstractions;
using Buddy.Core.Domain;

namespace Buddy.Core.Services;

public static class PronunciationAssessmentBuilder
{
    public const string SchemaVersion = "buddy.pronunciation.v2";

    public static PronunciationAssessment? Build(
        Guid recordingId,
        string transcript,
        string model,
        DateTimeOffset createdAt,
        IReadOnlyList<TranscriptionToken> tokens,
        string phoneticTranscript = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcript);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(tokens);

        IReadOnlyList<PronunciationWord> words = BuildWords(recordingId, tokens);
        return words.Count == 0
            ? null
            : new PronunciationAssessment(
                recordingId,
                transcript.Trim(),
                phoneticTranscript.Trim(),
                createdAt,
                model.Trim(),
                SchemaVersion,
                words);
    }

    public static IReadOnlyList<PronunciationWord> BuildWords(
        Guid sourceId,
        IReadOnlyList<TranscriptionToken> tokens)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "A pronunciation source identifier is required.",
                nameof(sourceId));
        }

        ArgumentNullException.ThrowIfNull(tokens);
        List<PronunciationWord> words = [];
        WordAccumulator? current = null;
        foreach (TranscriptionToken token in tokens)
        {
            string piece = token.Text;
            if (string.IsNullOrWhiteSpace(piece)
                || IsSpecialToken(piece))
            {
                continue;
            }

            bool hasWordCharacter = piece.Any(char.IsLetterOrDigit);
            bool startsNewWord = char.IsWhiteSpace(piece[0])
                && hasWordCharacter;
            string trimmed = piece.Trim();
            if (startsNewWord && current is not null)
            {
                words.Add(current.ToWord(sourceId, words.Count));
                current = null;
            }

            if (!hasWordCharacter)
            {
                current?.AppendPunctuation(trimmed);
                continue;
            }

            current ??= new WordAccumulator();
            current.AppendSpeechPiece(trimmed, token);
        }

        if (current is not null)
        {
            words.Add(current.ToWord(sourceId, words.Count));
        }

        return words;
    }

    private static bool IsSpecialToken(string text)
    {
        string trimmed = text.Trim();
        return trimmed.StartsWith("[_", StringComparison.Ordinal)
            && trimmed.EndsWith(']');
    }

    private sealed class WordAccumulator
    {
        private readonly StringBuilder _text = new();
        private double _weightedConfidence;
        private int _confidenceWeight;
        private TimeSpan? _start;
        private TimeSpan _end;

        public void AppendSpeechPiece(
            string piece,
            TranscriptionToken token)
        {
            _text.Append(piece);
            int weight = Math.Max(1, piece.Count(char.IsLetterOrDigit));
            float confidence = float.IsFinite(token.Confidence)
                ? Math.Clamp(token.Confidence, 0, 1)
                : 0;
            _weightedConfidence += confidence * weight;
            _confidenceWeight += weight;
            _start ??= token.Start;
            if (token.End > _end)
            {
                _end = token.End;
            }
        }

        public void AppendPunctuation(string punctuation)
        {
            _text.Append(punctuation);
        }

        public PronunciationWord ToWord(
            Guid sourceId,
            int sequence)
        {
            TimeSpan start = _start ?? TimeSpan.Zero;
            TimeSpan end = _end < start ? start : _end;
            float confidence = _confidenceWeight == 0
                ? 0
                : (float)(_weightedConfidence / _confidenceWeight);
            return new PronunciationWord(
                sourceId,
                sequence,
                _text.ToString(),
                start,
                end,
                confidence);
        }
    }
}
