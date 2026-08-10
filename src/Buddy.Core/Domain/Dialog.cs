namespace Buddy.Core.Domain;

public enum DialogSessionStatus
{
    Active = 0,
    Completing = 1,
    Completed = 2,
    Interrupted = 3,
    NeedsAttention = 4,
}

public enum DialogMessageRole
{
    User = 0,
    Assistant = 1,
}

public sealed record DialogSession(
    Guid Id,
    Guid RecordingId,
    DialogSessionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string SystemInstruction,
    string? Provider,
    string? Model,
    string? LastError,
    long Version)
{
    public static DialogSession Start(
        Guid recordingId,
        DateTimeOffset startedAt,
        string systemInstruction,
        Guid? id = null)
    {
        if (recordingId == Guid.Empty)
        {
            throw new ArgumentException(
                "A dialog must belong to a recording.",
                nameof(recordingId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(systemInstruction);
        return new DialogSession(
            id ?? Guid.NewGuid(),
            recordingId,
            DialogSessionStatus.Active,
            startedAt,
            null,
            systemInstruction.Trim(),
            null,
            null,
            null,
            0);
    }

    public DialogSession BeginCompletion()
    {
        if (Status != DialogSessionStatus.Active)
        {
            throw new InvalidOperationException(
                $"A dialog cannot begin completion from {Status}.");
        }

        return this with
        {
            Status = DialogSessionStatus.Completing,
            Version = checked(Version + 1),
        };
    }

    public DialogSession Complete(DateTimeOffset endedAt)
    {
        if (Status is not (DialogSessionStatus.Active
            or DialogSessionStatus.Completing
            or DialogSessionStatus.Interrupted))
        {
            throw new InvalidOperationException(
                $"A dialog cannot complete from {Status}.");
        }

        if (endedAt < StartedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endedAt),
                endedAt,
                "Dialog end cannot precede its start.");
        }

        return this with
        {
            Status = DialogSessionStatus.Completed,
            EndedAt = endedAt,
            LastError = null,
            Version = checked(Version + 1),
        };
    }

    public DialogSession Interrupt(DateTimeOffset endedAt, string? error = null)
    {
        if (Status is DialogSessionStatus.Completed
            or DialogSessionStatus.NeedsAttention)
        {
            return this;
        }

        if (endedAt < StartedAt)
        {
            endedAt = StartedAt;
        }

        return this with
        {
            Status = DialogSessionStatus.Interrupted,
            EndedAt = endedAt,
            LastError = string.IsNullOrWhiteSpace(error) ? null : error.Trim(),
            Version = checked(Version + 1),
        };
    }

    public DialogSession WithProvider(string provider, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return this with
        {
            Provider = provider.Trim(),
            Model = model.Trim(),
            Version = checked(Version + 1),
        };
    }

    public DialogSession WithError(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return this with
        {
            Status = DialogSessionStatus.NeedsAttention,
            LastError = error.Trim(),
            Version = checked(Version + 1),
        };
    }
}

public sealed record DialogMessage(
    Guid Id,
    Guid SessionId,
    int Sequence,
    DialogMessageRole Role,
    string Text,
    DateTimeOffset CreatedAt,
    string? Provider,
    string? Model,
    TimeSpan? Latency,
    int? PromptTokens,
    int? CompletionTokens,
    Guid? AudioArtifactId)
{
    public DialogMessage WithAudioArtifact(Guid artifactId)
    {
        if (artifactId == Guid.Empty)
        {
            throw new ArgumentException(
                "An assistant audio artifact identifier is required.",
                nameof(artifactId));
        }

        return this with { AudioArtifactId = artifactId };
    }
}

public sealed record ConversationTurn(
    DialogMessageRole Role,
    string Content);

public sealed record ConversationRequest(
    string SystemInstruction,
    IReadOnlyList<ConversationTurn> Messages,
    string Locale,
    int MaximumOutputCharacters = 4_000);

public static class ConversationAnswerContract
{
    public const string SchemaVersion = "buddy.conversation-answer.v1";
}

public sealed record ConversationResult(
    string Answer,
    string SpokenAnswer,
    string Provider,
    string Model,
    TimeSpan Latency,
    int? PromptTokens,
    int? CompletionTokens);
