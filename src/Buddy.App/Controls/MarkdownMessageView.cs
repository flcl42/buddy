using System.Windows.Input;
using Buddy.App.ViewModels;
using Buddy.Core.Domain;

namespace Buddy.App.Controls;

public sealed class MarkdownMessageView : Label
{
    private static readonly MarkdownContentDocument EmptyDocument =
        new([], string.Empty);

    public static readonly BindableProperty DocumentProperty =
        BindableProperty.Create(
            nameof(Document),
            typeof(MarkdownContentDocument),
            typeof(MarkdownMessageView),
            EmptyDocument,
            propertyChanged: OnDocumentChanged);

    public static readonly BindableProperty WordClickedCommandProperty =
        BindableProperty.Create(
            nameof(WordClickedCommand),
            typeof(ICommand),
            typeof(MarkdownMessageView));

    public static readonly BindableProperty CommandContextProperty =
        BindableProperty.Create(
            nameof(CommandContext),
            typeof(DialogMessageViewModel),
            typeof(MarkdownMessageView));

    public MarkdownContentDocument Document
    {
        get => (MarkdownContentDocument)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public ICommand? WordClickedCommand
    {
        get => (ICommand?)GetValue(WordClickedCommandProperty);
        set => SetValue(WordClickedCommandProperty, value);
    }

    public DialogMessageViewModel? CommandContext
    {
        get => (DialogMessageViewModel?)GetValue(CommandContextProperty);
        set => SetValue(CommandContextProperty, value);
    }

    internal void ExecuteWordClick(string word)
    {
        if (CommandContext is null || string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        DialogWordLookupRequest request = new(CommandContext, word);
        if (WordClickedCommand?.CanExecute(request) == true)
        {
            WordClickedCommand.Execute(request);
        }
    }

    private static void OnDocumentChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        if (bindable is MarkdownMessageView view)
        {
            view.Text = (newValue as MarkdownContentDocument)?.PlainText
                ?? string.Empty;
        }
    }
}
