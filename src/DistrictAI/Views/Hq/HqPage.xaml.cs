using System.Collections.Specialized;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Hq;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace DistrictAI.Views.Hq;

/// <summary>
/// District HQ: the conversation, each answer drawn from the core's runs (no
/// markup is read here), the card in front of a proposed change, Report on
/// each answer, and the prompt box.
/// </summary>
public sealed partial class HqPage : UserControl
{
    /// <summary>The answer a <see cref="RichTextBlock"/> shows: setting it draws the answer's paragraphs.</summary>
    public static readonly DependencyProperty AnswerProperty = DependencyProperty.RegisterAttached(
        "Answer",
        typeof(HqMessageItem),
        typeof(HqPage),
        new PropertyMetadata(null, OnAnswerChanged));

    /// <summary>The fixed-width font of code, which Windows always has.</summary>
    private static readonly FontFamily CodeFont = new("Consolas");

    private PageContext? _context;

    /// <summary>A page with nothing shown yet.</summary>
    public HqPage()
    {
        InitializeComponent();
        ViewModel.Messages.CollectionChanged += OnMessagesChanged;
    }

    /// <summary>What the page shows, and its actions.</summary>
    public HqViewModel ViewModel { get; } = new();

    /// <summary>The answer <paramref name="element"/> shows.</summary>
    public static HqMessageItem? GetAnswer(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (HqMessageItem?)element.GetValue(AnswerProperty);
    }

    /// <summary>Shows <paramref name="value"/> in <paramref name="element"/>.</summary>
    public static void SetAnswer(DependencyObject element, HqMessageItem? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AnswerProperty, value);
    }

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(HqView view) => ViewModel.Show(view, _context?.ReportSending ?? false);

    /// <summary>
    /// Enter sends; Shift+Enter is a new line. Taken before the box sees the
    /// key, which would otherwise put a new line in.
    /// </summary>
    private void OnPromptPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter
            || InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down))
        {
            return;
        }
        e.Handled = true;
        if (ViewModel.AskCommand.CanExecute(null))
        {
            ViewModel.AskCommand.Execute(null);
        }
    }

    private async void OnReportClick(object sender, RoutedEventArgs e)
    {
        if (_context is null || XamlRoot is null || (sender as FrameworkElement)?.DataContext is not HqMessageItem item)
        {
            return;
        }
        await ReportDialog.OfferAsync(XamlRoot, _context, item.Report, HqViewModel.AnswerTarget).ConfigureAwait(true);
    }

    /// <summary>A new answer at the end of the conversation is read out by a screen reader, as a reply in a chat is.</summary>
    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add
            || !IsLoaded
            || e.NewItems is not { Count: > 0 } added
            || added[^1] is not HqMessageItem { IsAnswer: true } answer
            || e.NewStartingIndex + added.Count != ViewModel.Messages.Count)
        {
            return;
        }
        var peer = FrameworkElementAutomationPeer.FromElement(this) ?? FrameworkElementAutomationPeer.CreatePeerForElement(this);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.Other,
            AutomationNotificationProcessing.ImportantMostRecent,
            answer.AccessibleName,
            "DistrictHqAnswer");
    }

    private static void OnAnswerChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not RichTextBlock block)
        {
            return;
        }
        block.Blocks.Clear();
        if (e.NewValue is not HqMessageItem answer)
        {
            return;
        }
        foreach (var paragraph in answer.Paragraphs)
        {
            block.Blocks.Add(Draw(block, paragraph));
        }
    }

    /// <summary>One paragraph, built inline by inline from what the core decided; nothing in it is parsed.</summary>
    private static Paragraph Draw(RichTextBlock owner, RichParagraph source)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(source.Indent * 20, source.SpaceBefore ? 8 : 0, 0, 0),
        };
        if (source.Kind == RichParagraphKind.Heading)
        {
            paragraph.FontWeight = FontWeights.SemiBold;
        }
        if (source.Kind == RichParagraphKind.Code)
        {
            paragraph.FontFamily = CodeFont;
        }
        foreach (var inline in source.Inlines)
        {
            paragraph.Inlines.Add(Draw(owner, inline));
        }
        return paragraph;
    }

    private static Inline Draw(RichTextBlock owner, RichInline source)
    {
        if (source.IsLineBreak)
        {
            return new LineBreak();
        }
        var run = new Run { Text = source.Text };
        if (source.Bold)
        {
            run.FontWeight = FontWeights.SemiBold;
        }
        if (source.Italic)
        {
            run.FontStyle = Windows.UI.Text.FontStyle.Italic;
        }
        if (source.Code)
        {
            run.FontFamily = CodeFont;
        }
        if (source.Link is not { } url)
        {
            return run;
        }
        // No NavigateUri: the click goes to the core, which opens only a web page.
        var link = new Hyperlink();
        link.Inlines.Add(run);
        link.Click += (_, _) => PageOf(owner)?.ViewModel.OpenLink(url);
        return link;
    }

    /// <summary>The page <paramref name="element"/> is on.</summary>
    private static HqPage? PageOf(DependencyObject element)
    {
        for (var at = element; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (at is HqPage page)
            {
                return page;
            }
        }
        return null;
    }
}
