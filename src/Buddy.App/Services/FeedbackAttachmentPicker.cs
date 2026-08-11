namespace Buddy.App.Services;

public sealed class FeedbackAttachmentPicker
{
    private readonly IFilePicker _filePicker;

    public FeedbackAttachmentPicker()
        : this(FilePicker.Default)
    {
    }

    internal FeedbackAttachmentPicker(IFilePicker filePicker)
    {
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
    }

    public async Task<FeedbackAttachment?> PickAsync(
        CancellationToken cancellationToken = default)
    {
        FileResult? result = await _filePicker.PickAsync(
            new PickOptions
            {
                PickerTitle = "Choose a screenshot",
                FileTypes = FilePickerFileType.Images,
            });
        if (result is null)
        {
            return null;
        }

        await using Stream input = await result.OpenReadAsync();
        using MemoryStream output = new();
        byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > BuddyFeedbackClient.MaximumScreenshotBytes)
            {
                throw new FeedbackAttachmentException(
                    FeedbackAttachmentFailure.TooLarge);
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        byte[] content = output.ToArray();
        string? contentType = DetectContentType(content);
        if (contentType is null)
        {
            throw new FeedbackAttachmentException(
                FeedbackAttachmentFailure.UnsupportedFormat);
        }

        return new FeedbackAttachment(
            Path.GetFileName(result.FileName),
            contentType,
            content);
    }

    internal static string? DetectContentType(ReadOnlySpan<byte> bytes)
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

public enum FeedbackAttachmentFailure
{
    TooLarge = 0,
    UnsupportedFormat = 1,
}

public sealed class FeedbackAttachmentException(
    FeedbackAttachmentFailure failure) : Exception(failure.ToString())
{
    public FeedbackAttachmentFailure Failure { get; } = failure;
}
