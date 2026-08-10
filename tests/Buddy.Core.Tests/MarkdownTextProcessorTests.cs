using Buddy.Core.Domain;
using Buddy.Core.Services;

namespace Buddy.Core.Tests;

public sealed class MarkdownTextProcessorTests
{
    [Fact]
    public void ParsePreservesStructureAndInlineStylesWithoutDelimiters()
    {
        const string markdown = """
            # A **clear** answer

            Use *natural* speech with `local audio` and ~~remove filler~~.

            - First point
            - Read [Buddy docs](https://example.test/buddy)

            > Context still matters.

            | Mode | Result |
            | --- | --- |
            | Dialog | Complete |
            """;

        MarkdownContentDocument document = MarkdownTextProcessor.Parse(markdown);

        Assert.Contains(
            document.Blocks,
            block => block.Kind == MarkdownBlockKind.Heading
                && block.Runs.Any(
                    run => run.Text == "clear"
                        && run.Style.HasFlag(MarkdownInlineStyle.Bold)));
        Assert.Contains(
            document.Blocks,
            block => block.Runs.Any(
                run => run.Text == "natural"
                    && run.Style.HasFlag(MarkdownInlineStyle.Italic)));
        Assert.Contains(
            document.Blocks,
            block => block.Runs.Any(
                run => run.Text == "local audio"
                    && run.Style.HasFlag(MarkdownInlineStyle.Code)));
        Assert.Contains(
            document.Blocks,
            block => block.Runs.Any(
                run => run.Text == "remove filler"
                    && run.Style.HasFlag(MarkdownInlineStyle.Strikethrough)));
        Assert.Contains(
            document.Blocks,
            block => block.Kind == MarkdownBlockKind.UnorderedListItem);
        Assert.Contains(
            document.Blocks,
            block => block.Kind == MarkdownBlockKind.Quote);
        Assert.Contains(
            document.Blocks,
            block => block.Kind == MarkdownBlockKind.TableRow);
        Assert.Contains(
            document.Blocks,
            block => block.Runs.Any(
                run => run.Text == "Buddy docs"
                    && run.Style.HasFlag(MarkdownInlineStyle.Link)
                    && run.LinkTarget == "https://example.test/buddy"));
        Assert.DoesNotContain("**", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "https://example.test/buddy",
            document.PlainText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SpeechTextRemovesMarkdownButKeepsReadableContent()
    {
        const string markdown =
            "This is **important** and *clear*. See [Buddy](https://example.test).";

        string speech = MarkdownTextProcessor.ToSpeechText(markdown);

        Assert.Equal("This is important and clear. See Buddy.", speech);
        Assert.DoesNotContain('*', speech);
        Assert.DoesNotContain("https://", speech, StringComparison.Ordinal);
    }

    [Fact]
    public void SpeechTextPreservesOrdinaryAsterisksAndPhonemeOverrides()
    {
        const string markdown =
            "Two times three is 2 * 3, and [isn't](/ˈɪzənt/) stays explicit.";

        string speech = MarkdownTextProcessor.ToSpeechText(markdown);

        Assert.Equal(markdown, speech);
    }

    [Fact]
    public void SpeechTextOmitsFormattingOnlyContent()
    {
        Assert.Equal(string.Empty, MarkdownTextProcessor.ToSpeechText("---"));
    }
}
