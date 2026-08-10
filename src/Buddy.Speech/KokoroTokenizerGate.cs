namespace Buddy.Speech;

internal static class KokoroTokenizerGate
{
    internal static SemaphoreSlim Instance { get; } = new(1, 1);
}
