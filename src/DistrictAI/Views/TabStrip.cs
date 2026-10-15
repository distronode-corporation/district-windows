using System.Collections;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>One tab of a <see cref="TabStrip"/>: its text, and its UI Automation id and help text when it has them.</summary>
/// <param name="Text">What the tab says.</param>
/// <param name="AutomationId">Its UI Automation id, the same in every language, or null.</param>
/// <param name="HelpText">What Narrator adds after its name, or null.</param>
public sealed record TabLabel(string Text, string? AutomationId = null, string? HelpText = null);

/// <summary>
/// Views of one page switched in place, as WinUI's SelectorBar draws them
/// (a line under the tab showing), in a strip that scrolls sideways when the
/// tabs do not fit (French at 200 percent text) instead of wrapping into a
/// grid of radio buttons. The tabs are <see cref="ItemsSource"/>'s items,
/// each read as a <see cref="TabLabel"/>; <see cref="SelectedIndex"/> is the
/// view model's, and <see cref="Picked"/> says only what the member picked.
/// </summary>
public sealed partial class TabStrip : UserControl
{
    /// <summary>The <see cref="ItemsSource"/> property.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(object), typeof(TabStrip), new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>The <see cref="SelectedIndex"/> property.</summary>
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(TabStrip), new PropertyMetadata(-1, OnSelectedIndexChanged));

    private readonly SelectorBar _bar = new();
    private INotifyCollectionChanged? _watched;
    private bool _syncing;

    /// <summary>A strip with no tabs yet.</summary>
    public TabStrip()
    {
        IsTabStop = false;
        _bar.SelectionChanged += OnBarSelectionChanged;
        Content = new ScrollViewer
        {
            Content = _bar,
            IsTabStop = false,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    /// <summary>The tabs: <see cref="TabLabel"/>s, or items <see cref="Describe"/> reads, or strings.</summary>
    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Which tab shows, by its place in <see cref="ItemsSource"/>; -1 for none.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>How an item that is not a <see cref="TabLabel"/> reads; without it, its text.</summary>
    internal Func<object, TabLabel>? Describe
    {
        get;
        set
        {
            field = value;
            Rebuild();
        }
    }

    /// <summary>The member picked the tab at this place; never raised when <see cref="SelectedIndex"/> is set.</summary>
    internal event EventHandler<int>? Picked;

    private static void OnItemsSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var strip = (TabStrip)sender;
        if (strip._watched is not null)
        {
            strip._watched.CollectionChanged -= strip.OnItemsChanged;
        }
        strip._watched = e.NewValue as INotifyCollectionChanged;
        if (strip._watched is not null)
        {
            strip._watched.CollectionChanged += strip.OnItemsChanged;
        }
        strip.Rebuild();
    }

    private static void OnSelectedIndexChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((TabStrip)sender).Select();

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private TabLabel LabelOf(object? item) => item switch
    {
        TabLabel label => label,
        { } some when Describe is { } describe => describe(some),
        _ => new TabLabel(item?.ToString() ?? string.Empty),
    };

    // The bar's items brought in line with the source's, kept where they can
    // be so the tab showing does not flicker, then the selection set again.
    private void Rebuild()
    {
        List<TabLabel> labels = ItemsSource is IEnumerable items ? [.. items.Cast<object?>().Select(LabelOf)] : [];
        var was = _syncing;
        _syncing = true;
        try
        {
            while (_bar.Items.Count > labels.Count)
            {
                _bar.Items.RemoveAt(_bar.Items.Count - 1);
            }
            for (var index = 0; index < labels.Count; index++)
            {
                if (index == _bar.Items.Count)
                {
                    _bar.Items.Add(new SelectorBarItem());
                }
                var item = _bar.Items[index];
                var label = labels[index];
                item.Text = label.Text;
                AutomationProperties.SetName(item, label.Text);
                if (label.AutomationId is { } id)
                {
                    AutomationProperties.SetAutomationId(item, id);
                }
                if (label.HelpText is { } help)
                {
                    AutomationProperties.SetHelpText(item, help);
                }
            }
        }
        finally
        {
            _syncing = was;
        }
        Select();
    }

    private void Select()
    {
        var index = SelectedIndex;
        var wanted = index >= 0 && index < _bar.Items.Count ? _bar.Items[index] : null;
        if (ReferenceEquals(_bar.SelectedItem, wanted))
        {
            return;
        }
        var was = _syncing;
        _syncing = true;
        try
        {
            _bar.SelectedItem = wanted;
        }
        finally
        {
            _syncing = was;
        }
    }

    private void OnBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_syncing)
        {
            return;
        }
        var index = sender.SelectedItem is { } item ? sender.Items.IndexOf(item) : -1;
        if (index >= 0 && index != SelectedIndex)
        {
            Picked?.Invoke(this, index);
        }
    }
}
