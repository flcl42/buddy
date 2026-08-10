namespace Buddy.Core.Domain;

public enum MarkdownBlockKind
{
    Paragraph = 0,
    Heading = 1,
    UnorderedListItem = 2,
    OrderedListItem = 3,
    Quote = 4,
    Code = 5,
    HorizontalRule = 6,
    TableRow = 7,
}

[Flags]
public enum MarkdownInlineStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Code = 4,
    Strikethrough = 8,
    Link = 16,
}

public sealed record MarkdownInlineRun(
    string Text,
    MarkdownInlineStyle Style = MarkdownInlineStyle.None,
    string? LinkTarget = null);

public sealed record MarkdownContentBlock(
    MarkdownBlockKind Kind,
    IReadOnlyList<MarkdownInlineRun> Runs,
    string? Prefix = null,
    int Level = 0);

public sealed record MarkdownContentDocument(
    IReadOnlyList<MarkdownContentBlock> Blocks,
    string PlainText);
