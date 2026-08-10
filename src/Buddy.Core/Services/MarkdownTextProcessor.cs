using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Buddy.Core.Domain;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Buddy.Core.Services;

public static partial class MarkdownTextProcessor
{
    public const string SpeechNormalizationVersion = "buddy.markdown-speech.v1";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static MarkdownContentDocument Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new MarkdownContentDocument([], string.Empty);
        }

        MarkdownDocument parsed = Markdown.Parse(markdown, Pipeline);
        List<MarkdownContentBlock> blocks = [];
        foreach (Block block in parsed)
        {
            AppendBlock(block, blocks, level: 0);
        }

        return new MarkdownContentDocument(blocks, CreatePlainText(blocks));
    }

    public static string ToSpeechText(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        List<string> pronunciationLiterals = [];
        string protectedText = PhonemeLiteralRegex().Replace(
            markdown,
            match =>
            {
                int index = pronunciationLiterals.Count;
                pronunciationLiterals.Add(match.Value);
                return $"BuddyPronunciationToken{index.ToString(CultureInfo.InvariantCulture)}End";
            });
        string speechText = Parse(protectedText).PlainText;
        for (int index = 0; index < pronunciationLiterals.Count; index++)
        {
            speechText = speechText.Replace(
                $"BuddyPronunciationToken{index.ToString(CultureInfo.InvariantCulture)}End",
                pronunciationLiterals[index],
                StringComparison.Ordinal);
        }

        return speechText;
    }

    private static void AppendBlock(
        Block block,
        List<MarkdownContentBlock> destination,
        int level)
    {
        switch (block)
        {
            case HeadingBlock heading:
                AddLeafBlock(
                    destination,
                    MarkdownBlockKind.Heading,
                    heading,
                    level: Math.Clamp(heading.Level, 1, 6));
                break;
            case ParagraphBlock paragraph:
                AddLeafBlock(
                    destination,
                    MarkdownBlockKind.Paragraph,
                    paragraph,
                    level: level);
                break;
            case ListBlock list:
                AppendList(list, destination, level);
                break;
            case QuoteBlock quote:
                AddContainerBlock(
                    destination,
                    MarkdownBlockKind.Quote,
                    quote,
                    level: level);
                break;
            case Table table:
                AppendTable(table, destination, level);
                break;
            case CodeBlock code:
                string codeText = code.Lines.ToString().TrimEnd('\r', '\n');
                if (codeText.Length > 0)
                {
                    destination.Add(new MarkdownContentBlock(
                        MarkdownBlockKind.Code,
                        [new MarkdownInlineRun(codeText, MarkdownInlineStyle.Code)],
                        Level: level));
                }

                break;
            case ThematicBreakBlock:
                destination.Add(new MarkdownContentBlock(
                    MarkdownBlockKind.HorizontalRule,
                    [],
                    Level: level));
                break;
            case LeafBlock leaf when leaf.Inline is not null:
                AddLeafBlock(
                    destination,
                    MarkdownBlockKind.Paragraph,
                    leaf,
                    level: level);
                break;
            case HtmlBlock html:
                string visible = HtmlTagRegex()
                    .Replace(html.Lines.ToString(), string.Empty)
                    .Trim();
                if (visible.Length > 0)
                {
                    destination.Add(new MarkdownContentBlock(
                        MarkdownBlockKind.Paragraph,
                        [new MarkdownInlineRun(visible)],
                        Level: level));
                }

                break;
            case ContainerBlock container:
                foreach (Block child in container)
                {
                    AppendBlock(child, destination, level);
                }

                break;
        }
    }

    private static void AppendList(
        ListBlock list,
        List<MarkdownContentBlock> destination,
        int level)
    {
        int ordinal = int.TryParse(
            list.OrderedStart,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int parsedStart)
                ? parsedStart
                : 1;
        foreach (ListItemBlock item in list.OfType<ListItemBlock>())
        {
            List<MarkdownInlineRun> runs = [];
            List<ListBlock> nestedLists = [];
            foreach (Block child in item)
            {
                if (child is ListBlock nested)
                {
                    nestedLists.Add(nested);
                    continue;
                }

                AppendRunsFromBlock(child, runs);
            }

            TrimRuns(runs);
            if (runs.Count > 0)
            {
                string prefix = list.IsOrdered
                    ? $"{ordinal.ToString(CultureInfo.InvariantCulture)}."
                    : "•";
                destination.Add(new MarkdownContentBlock(
                    list.IsOrdered
                        ? MarkdownBlockKind.OrderedListItem
                        : MarkdownBlockKind.UnorderedListItem,
                    runs,
                    prefix,
                    level));
            }

            ordinal++;
            foreach (ListBlock nested in nestedLists)
            {
                AppendList(nested, destination, checked(level + 1));
            }
        }
    }

    private static void AppendTable(
        Table table,
        List<MarkdownContentBlock> destination,
        int level)
    {
        foreach (TableRow row in table.OfType<TableRow>())
        {
            List<MarkdownInlineRun> runs = [];
            bool firstCell = true;
            foreach (TableCell cell in row.OfType<TableCell>())
            {
                List<MarkdownInlineRun> cellRuns = [];
                foreach (Block child in cell)
                {
                    AppendRunsFromBlock(child, cellRuns);
                }

                TrimRuns(cellRuns);
                if (cellRuns.Count == 0)
                {
                    continue;
                }

                if (!firstCell)
                {
                    AddRun(runs, "  ·  ", MarkdownInlineStyle.None, null);
                }

                foreach (MarkdownInlineRun run in cellRuns)
                {
                    AddRun(
                        runs,
                        run.Text,
                        row.IsHeader
                            ? run.Style | MarkdownInlineStyle.Bold
                            : run.Style,
                        run.LinkTarget);
                }

                firstCell = false;
            }

            if (runs.Count > 0)
            {
                destination.Add(new MarkdownContentBlock(
                    MarkdownBlockKind.TableRow,
                    runs,
                    Level: level));
            }
        }
    }

    private static void AddLeafBlock(
        List<MarkdownContentBlock> destination,
        MarkdownBlockKind kind,
        LeafBlock leaf,
        int level)
    {
        List<MarkdownInlineRun> runs = [];
        if (leaf.Inline is not null)
        {
            AppendInlineContainer(
                leaf.Inline,
                runs,
                MarkdownInlineStyle.None,
                null);
        }

        TrimRuns(runs);
        if (runs.Count > 0)
        {
            destination.Add(new MarkdownContentBlock(kind, runs, Level: level));
        }
    }

    private static void AddContainerBlock(
        List<MarkdownContentBlock> destination,
        MarkdownBlockKind kind,
        ContainerBlock container,
        int level)
    {
        List<MarkdownInlineRun> runs = [];
        foreach (Block child in container)
        {
            AppendRunsFromBlock(child, runs);
        }

        TrimRuns(runs);
        if (runs.Count > 0)
        {
            destination.Add(new MarkdownContentBlock(kind, runs, Level: level));
        }
    }

    private static void AppendRunsFromBlock(
        Block block,
        List<MarkdownInlineRun> destination)
    {
        if (destination.Count > 0)
        {
            AddRun(destination, " ", MarkdownInlineStyle.None, null);
        }

        switch (block)
        {
            case CodeBlock code:
                AddRun(
                    destination,
                    code.Lines.ToString().Trim(),
                    MarkdownInlineStyle.Code,
                    null);
                break;
            case LeafBlock leaf when leaf.Inline is not null:
                AppendInlineContainer(
                    leaf.Inline,
                    destination,
                    MarkdownInlineStyle.None,
                    null);
                break;
            case ContainerBlock container:
                foreach (Block child in container)
                {
                    AppendRunsFromBlock(child, destination);
                }

                break;
        }
    }

    private static void AppendInlineContainer(
        ContainerInline container,
        List<MarkdownInlineRun> destination,
        MarkdownInlineStyle inheritedStyle,
        string? inheritedLink)
    {
        Inline? current = container.FirstChild;
        while (current is not null)
        {
            AppendInline(
                current,
                destination,
                inheritedStyle,
                inheritedLink);
            current = current.NextSibling;
        }
    }

    private static void AppendInline(
        Inline inline,
        List<MarkdownInlineRun> destination,
        MarkdownInlineStyle inheritedStyle,
        string? inheritedLink)
    {
        switch (inline)
        {
            case LiteralInline literal:
                AddRun(
                    destination,
                    literal.Content.ToString(),
                    inheritedStyle,
                    inheritedLink);
                break;
            case CodeInline code:
                AddRun(
                    destination,
                    code.Content,
                    inheritedStyle | MarkdownInlineStyle.Code,
                    inheritedLink);
                break;
            case EmphasisInline emphasis:
                MarkdownInlineStyle emphasisStyle = emphasis.DelimiterChar == '~'
                    ? MarkdownInlineStyle.Strikethrough
                    : emphasis.DelimiterCount >= 2
                        ? MarkdownInlineStyle.Bold
                        : MarkdownInlineStyle.Italic;
                AppendInlineContainer(
                    emphasis,
                    destination,
                    inheritedStyle | emphasisStyle,
                    inheritedLink);
                break;
            case LinkInline link:
                AppendInlineContainer(
                    link,
                    destination,
                    inheritedStyle | MarkdownInlineStyle.Link,
                    link.Url);
                break;
            case AutolinkInline autoLink:
                AddRun(
                    destination,
                    autoLink.Url,
                    inheritedStyle | MarkdownInlineStyle.Link,
                    autoLink.Url);
                break;
            case HtmlEntityInline entity:
                AddRun(
                    destination,
                    entity.Transcoded.ToString(),
                    inheritedStyle,
                    inheritedLink);
                break;
            case LineBreakInline:
                AddRun(destination, "\n", inheritedStyle, inheritedLink);
                break;
            case TaskList task:
                AddRun(
                    destination,
                    task.Checked ? "☑ " : "☐ ",
                    inheritedStyle,
                    inheritedLink);
                break;
            case HtmlInline:
                break;
            case ContainerInline nested:
                AppendInlineContainer(
                    nested,
                    destination,
                    inheritedStyle,
                    inheritedLink);
                break;
        }
    }

    private static void AddRun(
        List<MarkdownInlineRun> destination,
        string text,
        MarkdownInlineStyle style,
        string? linkTarget)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (destination.Count > 0)
        {
            MarkdownInlineRun previous = destination[^1];
            if (previous.Style == style
                && string.Equals(
                    previous.LinkTarget,
                    linkTarget,
                    StringComparison.Ordinal))
            {
                destination[^1] = previous with { Text = previous.Text + text };
                return;
            }
        }

        destination.Add(new MarkdownInlineRun(text, style, linkTarget));
    }

    private static void TrimRuns(List<MarkdownInlineRun> runs)
    {
        while (runs.Count > 0 && string.IsNullOrWhiteSpace(runs[0].Text))
        {
            runs.RemoveAt(0);
        }

        while (runs.Count > 0 && string.IsNullOrWhiteSpace(runs[^1].Text))
        {
            runs.RemoveAt(runs.Count - 1);
        }

        if (runs.Count > 0)
        {
            runs[0] = runs[0] with { Text = runs[0].Text.TrimStart() };
            runs[^1] = runs[^1] with { Text = runs[^1].Text.TrimEnd() };
        }
    }

    private static string CreatePlainText(
        IReadOnlyList<MarkdownContentBlock> blocks)
    {
        StringBuilder text = new();
        foreach (MarkdownContentBlock block in blocks)
        {
            if (block.Kind == MarkdownBlockKind.HorizontalRule)
            {
                continue;
            }

            string content = string.Concat(block.Runs.Select(run => run.Text)).Trim();
            if (content.Length == 0)
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.AppendLine();
            }

            text.Append(content);
        }

        return text.ToString().Trim();
    }

    [GeneratedRegex(@"\[[^\]\r\n]+\]\(/[^\r\n()]+/\)")]
    private static partial Regex PhonemeLiteralRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();
}
