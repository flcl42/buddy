using System.Globalization;

namespace Buddy.Core.Domain;

public static class RecordingTitles
{
    public static string CreateFallback(RecordingKind kind, DateTimeOffset createdAt)
    {
        string prefix = kind switch
        {
            RecordingKind.Meeting => "Meeting",
            RecordingKind.Trainer => "Practice",
            RecordingKind.Dialog => "AI Dialog",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown recording kind."),
        };
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix} · {createdAt:dd MMM · HH:mm}");
    }
}
