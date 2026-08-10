namespace Buddy.Speech;

public sealed class LocalModelNotInstalledException : InvalidOperationException
{
    public LocalModelNotInstalledException(string modelId, string displayName)
        : base($"{displayName} is not installed.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ModelId = modelId;
    }

    public string ModelId { get; }
}
