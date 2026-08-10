namespace Buddy.App.ViewModels;

public sealed record DialogWordLookupRequest(
    DialogMessageViewModel Message,
    string Word);
