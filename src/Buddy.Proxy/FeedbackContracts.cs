using System.Text;

namespace Buddy.Proxy;

public static class FeedbackLimits
{
    public const int MaximumMessageCharacters = 3_000;

    public const int MaximumMetadataCharacters = 100;

    public const int MaximumScreenshotBytes = 8 * 1024 * 1024;

    public const long MaximumRequestBytes = MaximumScreenshotBytes + 64 * 1024;

    public static string NormalizeMetadata(string? value, string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);
        StringBuilder normalized = new(MaximumMetadataCharacters);
        bool separatorPending = false;
        foreach (char character in value ?? string.Empty)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                separatorPending = normalized.Length > 0;
                continue;
            }

            if (separatorPending && normalized.Length < MaximumMetadataCharacters - 1)
            {
                normalized.Append(' ');
            }

            separatorPending = false;
            normalized.Append(character);
            if (normalized.Length == MaximumMetadataCharacters)
            {
                break;
            }
        }

        if (normalized.Length == 0)
        {
            return fallback;
        }

        return normalized.ToString();
    }

    public static string? DetectImageContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8
            && bytes[..8].SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (bytes.Length >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}

public sealed record FeedbackScreenshot(
    byte[] Content,
    string ContentType);

public sealed record FeedbackSubmission(
    string Id,
    string Message,
    string AppVersion,
    string InterfaceLanguage,
    string DialogLanguage,
    DateTimeOffset SubmittedUtc,
    FeedbackScreenshot? Screenshot);

public sealed record FeedbackDeliveryResult(
    string FeedbackId,
    bool ScreenshotDelivered);
