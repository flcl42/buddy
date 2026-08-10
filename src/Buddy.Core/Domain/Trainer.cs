namespace Buddy.Core.Domain;

public enum ImprovementMode
{
    CorrectOnly = 0,
    Natural = 1,
    ClearAndConcise = 2,
}

public sealed record GlossaryTerm(
    string WrittenForm,
    string? Pronunciation,
    bool ProtectFromRewriting);

public sealed record TextChange(
    string Original,
    string Replacement,
    string Reason);

public sealed record TextAmbiguity(
    string SourceText,
    string Explanation,
    IReadOnlyList<string> Alternatives);

public sealed record ImprovementRequest(
    string Transcript,
    ImprovementMode Mode,
    string Locale,
    IReadOnlyList<GlossaryTerm> Glossary,
    string? Tone,
    int MaximumOutputCharacters = 12_000);

public sealed record ImprovementResult(
    string Corrected,
    string? Polished,
    IReadOnlyList<TextChange> Changes,
    IReadOnlyList<TextAmbiguity> Ambiguities,
    IReadOnlyList<string> ProtectedTermViolations,
    string Provider,
    string Model,
    TimeSpan Latency,
    int? PromptTokens,
    int? CompletionTokens);

public sealed record TitleRequest(
    string Transcript,
    RecordingKind Kind,
    string Locale,
    int MaximumCharacters = 72);

public sealed record TitleResult(
    string Title,
    string Provider,
    string Model,
    TimeSpan Latency);

public sealed record WordDefinitionRequest(
    string Word,
    string Context,
    string Locale);

public sealed record WordDefinitionResult(
    string Headword,
    string? PartOfSpeech,
    string Definition,
    string Provider,
    string Model,
    TimeSpan Latency);
