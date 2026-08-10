using System.Text.RegularExpressions;
using Buddy.App.Controls;
using Buddy.Core.Domain;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;
using XamlFontWeights = Microsoft.UI.Text.FontWeights;
using XamlHorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using XamlKeyboardAccelerator = Microsoft.UI.Xaml.Input.KeyboardAccelerator;
using XamlSolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using XamlTextDecorations = Windows.UI.Text.TextDecorations;
using XamlThickness = Microsoft.UI.Xaml.Thickness;

namespace Buddy.App.Platforms.Windows;

public sealed partial class MarkdownMessageViewHandler
    : ViewHandler<MarkdownMessageView, RichTextBlock>
{
    private readonly Dictionary<Run, string> _wordRuns = [];
    private XamlKeyboardAccelerator? _copyAccelerator;
    private TappedEventHandler? _tappedHandler;

    public static readonly IPropertyMapper<
        MarkdownMessageView,
        MarkdownMessageViewHandler> Mapper =
        new PropertyMapper<MarkdownMessageView, MarkdownMessageViewHandler>(
            ViewMapper)
        {
            [nameof(MarkdownMessageView.Document)] = MapDocument,
        };

    public MarkdownMessageViewHandler()
        : base(Mapper)
    {
    }

    protected override RichTextBlock CreatePlatformView()
    {
        return new RichTextBlock
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = Brush("#282A38"),
            HorizontalAlignment = XamlHorizontalAlignment.Stretch,
            IsTextSelectionEnabled = true,
            SelectionHighlightColor = Brush("#C9C8FF"),
            TextWrapping = TextWrapping.Wrap,
        };
    }

    protected override void ConnectHandler(RichTextBlock platformView)
    {
        base.ConnectHandler(platformView);
        _copyAccelerator = new XamlKeyboardAccelerator
        {
            IsEnabled = false,
            Key = global::Windows.System.VirtualKey.C,
            Modifiers = global::Windows.System.VirtualKeyModifiers.Control,
        };
        _copyAccelerator.Invoked += HandleCopyAcceleratorInvoked;
        platformView.KeyboardAccelerators.Add(_copyAccelerator);
        platformView.Loaded += HandleLoaded;
        platformView.SelectionChanged += HandleSelectionChanged;
        _tappedHandler = HandleTapped;
        platformView.AddHandler(
            UIElement.TappedEvent,
            _tappedHandler,
            handledEventsToo: true);
        RebuildDocument();
    }

    protected override void DisconnectHandler(RichTextBlock platformView)
    {
        platformView.Loaded -= HandleLoaded;
        platformView.SelectionChanged -= HandleSelectionChanged;
        if (_copyAccelerator is not null)
        {
            _copyAccelerator.Invoked -= HandleCopyAcceleratorInvoked;
            platformView.KeyboardAccelerators.Remove(_copyAccelerator);
            _copyAccelerator = null;
        }

        if (_tappedHandler is not null)
        {
            platformView.RemoveHandler(UIElement.TappedEvent, _tappedHandler);
            _tappedHandler = null;
        }

        _wordRuns.Clear();
        base.DisconnectHandler(platformView);
    }

    public static void MapDocument(
        MarkdownMessageViewHandler handler,
        MarkdownMessageView view)
    {
        handler.RebuildDocument();
    }

    private void RebuildDocument()
    {
        if (PlatformView is null || VirtualView is null)
        {
            return;
        }

        _wordRuns.Clear();
        PlatformView.Blocks.Clear();
        IReadOnlyList<MarkdownContentBlock> blocks = VirtualView.Document.Blocks;
        for (int index = 0; index < blocks.Count; index++)
        {
            PlatformView.Blocks.Add(CreateParagraph(blocks[index], index));
        }
    }

    private Paragraph CreateParagraph(MarkdownContentBlock block, int index)
    {
        bool isHeading = block.Kind == MarkdownBlockKind.Heading;
        Paragraph paragraph = new()
        {
            FontFamily = new FontFamily(
                block.Kind == MarkdownBlockKind.Code ? "Consolas" : "Segoe UI"),
            FontSize = block.Kind == MarkdownBlockKind.Code
                ? 13
                : isHeading
                    ? Math.Max(15, 20 - ((Math.Max(1, block.Level) - 1) * 1.25))
                    : 14,
            FontWeight = isHeading
                ? XamlFontWeights.SemiBold
                : XamlFontWeights.Normal,
            Foreground = Brush(
                block.Kind == MarkdownBlockKind.Quote ? "#4A4D60" : "#282A38"),
            Margin = CreateMargin(block, index),
        };

        if (block.Kind == MarkdownBlockKind.HorizontalRule)
        {
            paragraph.Inlines.Add(
                new Run
                {
                    Text = "────────────────────────────────",
                    Foreground = Brush("#D7DAE4"),
                });
            return paragraph;
        }

        if (block.Kind == MarkdownBlockKind.Quote)
        {
            paragraph.Inlines.Add(
                new Run
                {
                    Text = "▎ ",
                    FontWeight = XamlFontWeights.Bold,
                    Foreground = Brush("#A9AAE9"),
                });
        }

        if (!string.IsNullOrWhiteSpace(block.Prefix))
        {
            paragraph.Inlines.Add(
                new Run
                {
                    Text = $"{block.Prefix} ",
                    FontWeight = XamlFontWeights.Bold,
                    Foreground = Brush("#5B5CE2"),
                });
        }

        foreach (MarkdownInlineRun run in block.Runs)
        {
            AppendRun(paragraph, run, isHeading);
        }

        return paragraph;
    }

    private void AppendRun(
        Paragraph paragraph,
        MarkdownInlineRun source,
        bool isHeading)
    {
        MatchCollection words = WordRegex().Matches(source.Text);
        int position = 0;
        foreach (Match word in words)
        {
            if (word.Index > position)
            {
                paragraph.Inlines.Add(
                    CreateRun(
                        source.Text[position..word.Index],
                        source,
                        isHeading));
            }

            Run wordRun = CreateRun(word.Value, source, isHeading);
            _wordRuns.Add(wordRun, word.Value);
            paragraph.Inlines.Add(wordRun);
            position = word.Index + word.Length;
        }

        if (position < source.Text.Length)
        {
            paragraph.Inlines.Add(
                CreateRun(source.Text[position..], source, isHeading));
        }
    }

    private void HandleTapped(object sender, TappedRoutedEventArgs e)
    {
        if (PlatformView is null
            || !string.IsNullOrEmpty(PlatformView.SelectedText))
        {
            return;
        }

        TextPointer position = PlatformView.GetPositionFromPoint(
            e.GetPosition(PlatformView));
        if (position.Parent is Run run
            && _wordRuns.TryGetValue(run, out string? word))
        {
            VirtualView?.ExecuteWordClick(word);
        }
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        if (_copyAccelerator is not null && PlatformView?.XamlRoot is not null)
        {
            _copyAccelerator.ScopeOwner = PlatformView.XamlRoot.Content;
        }
    }

    private void HandleSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_copyAccelerator is not null && PlatformView is not null)
        {
            _copyAccelerator.IsEnabled =
                !string.IsNullOrEmpty(PlatformView.SelectedText);
        }
    }

    private void HandleCopyAcceleratorInvoked(
        XamlKeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs e)
    {
        if (PlatformView is null
            || string.IsNullOrEmpty(PlatformView.SelectedText))
        {
            return;
        }

        PlatformView.CopySelectionToClipboard();
        e.Handled = true;
    }

    private static Run CreateRun(
        string text,
        MarkdownInlineRun source,
        bool isHeading)
    {
        bool bold = isHeading
            || source.Style.HasFlag(MarkdownInlineStyle.Bold);
        Run run = new()
        {
            Text = text,
            FontFamily = new FontFamily(
                source.Style.HasFlag(MarkdownInlineStyle.Code)
                    ? "Consolas"
                    : "Segoe UI"),
            FontStyle = source.Style.HasFlag(MarkdownInlineStyle.Italic)
                ? FontStyle.Italic
                : FontStyle.Normal,
            FontWeight = bold ? XamlFontWeights.Bold : XamlFontWeights.Normal,
            Foreground = Brush(
                source.Style.HasFlag(MarkdownInlineStyle.Link)
                    ? "#4F50C8"
                    : "#282A38"),
            TextDecorations = source.Style.HasFlag(
                MarkdownInlineStyle.Strikethrough)
                    ? XamlTextDecorations.Strikethrough
                    : source.Style.HasFlag(MarkdownInlineStyle.Link)
                        ? XamlTextDecorations.Underline
                        : XamlTextDecorations.None,
        };
        return run;
    }

    private static XamlThickness CreateMargin(
        MarkdownContentBlock block,
        int index)
    {
        double left = block.Kind switch
        {
            MarkdownBlockKind.OrderedListItem
                or MarkdownBlockKind.UnorderedListItem =>
                12 + (Math.Max(0, block.Level) * 16),
            MarkdownBlockKind.Quote => 3,
            _ => 0,
        };
        double top = index == 0 ? 0 : 3;
        double bottom = block.Kind == MarkdownBlockKind.Heading ? 2 : 3;
        return new XamlThickness(left, top, 0, bottom);
    }

    private static XamlSolidColorBrush Brush(string color)
    {
        Microsoft.Maui.Graphics.Color source =
            Microsoft.Maui.Graphics.Color.FromArgb(color);
        global::Windows.UI.Color value = global::Windows.UI.Color.FromArgb(
            (byte)Math.Round(source.Alpha * byte.MaxValue),
            (byte)Math.Round(source.Red * byte.MaxValue),
            (byte)Math.Round(source.Green * byte.MaxValue),
            (byte)Math.Round(source.Blue * byte.MaxValue));
        return new XamlSolidColorBrush(value);
    }

    [GeneratedRegex(
        @"[\p{L}\p{M}\p{N}]+(?:['’\-‐‑][\p{L}\p{M}\p{N}]+)*",
        RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
