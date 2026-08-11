using Buddy.Core.Abstractions;

namespace Buddy.Speech;

public static class PlatformSpeechVoiceIds
{
    public const string WindowsPrefix = "windows:";
    public const string MacOsPrefix = "macos-say:";
    public const string LinuxPrefix = "linux-espeak:";

    public static bool IsPlatformVoice(string voiceId) =>
        !string.IsNullOrWhiteSpace(voiceId)
        && (voiceId.StartsWith(WindowsPrefix, StringComparison.Ordinal)
            || voiceId.StartsWith(MacOsPrefix, StringComparison.Ordinal)
            || voiceId.StartsWith(LinuxPrefix, StringComparison.Ordinal));
}

public interface IPlatformSpeechSynthesisService : ISpeechSynthesisService
{
    bool CanSynthesize(string voiceId);
}
