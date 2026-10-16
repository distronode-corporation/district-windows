using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using DistrictAI.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>
/// <c>views:SnapshotItems.Source="{x:Bind ViewModel.Rows}"</c> in place of the
/// ItemsSource of an ItemsRepeater, or of RadioButtons (an ItemsRepeater
/// inside), in a dialog's content that is made again each time the dialog
/// opens: the list shows snapshots of the collection, set at low priority once
/// layout is done (<see cref="SnapshotFeed"/>), from when it loads until it
/// unloads, never the live collection. A list handling a change synchronously
/// inside the core's render, while its dialog opened, failed with E_FAIL and
/// ended the app; and a closed dialog's list kept hearing of every change.
/// RadioButtons take their selection from <c>SnapshotItems.SelectedIndex</c>,
/// applied after each snapshot, since a new ItemsSource clears it. A source
/// that is not observable is shown as it is.
/// </summary>
public static class SnapshotItems
{
    /// <summary>The collection the list shows snapshots of.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(object), typeof(SnapshotItems), new PropertyMetadata(null, OnSourceChanged));

    /// <summary>For RadioButtons: the index chosen, in the collection's order, or -1.</summary>
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.RegisterAttached(
        "SelectedIndex", typeof(int), typeof(SnapshotItems), new PropertyMetadata(-1, OnSelectedIndexChanged));

    private static readonly ConditionalWeakTable<FrameworkElement, Follower> _followers = new();

    /// <summary>The collection <paramref name="list"/> shows snapshots of.</summary>
    public static object? GetSource(FrameworkElement list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return list.GetValue(SourceProperty);
    }

    /// <summary>Has <paramref name="list"/> show snapshots of <paramref name="value"/>.</summary>
    public static void SetSource(FrameworkElement list, object? value)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.SetValue(SourceProperty, value);
    }

    /// <summary>The index chosen in <paramref name="list"/>.</summary>
    public static int GetSelectedIndex(FrameworkElement list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return (int)list.GetValue(SelectedIndexProperty);
    }

    /// <summary>Chooses <paramref name="value"/> in <paramref name="list"/>, now or once its items are shown.</summary>
    public static void SetSelectedIndex(FrameworkElement list, int value)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.SetValue(SelectedIndexProperty, value);
    }

    private static void OnSourceChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ItemsRepeater or RadioButtons)
        {
            _followers.GetValue((FrameworkElement)target, created => new Follower(created)).Follow(e.NewValue);
        }
    }

    private static void OnSelectedIndexChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is RadioButtons radio)
        {
            Choose(radio);
        }
    }

    /// <summary>The index asked for, once the items shown have it.</summary>
    private static void Choose(RadioButtons radio)
    {
        var index = GetSelectedIndex(radio);
        var count = radio.ItemsSource is System.Collections.ICollection items ? items.Count : 0;
        if (index < count && radio.SelectedIndex != index)
        {
            radio.SelectedIndex = index;
        }
    }

    private static void Show(FrameworkElement list, object? items)
    {
        switch (list)
        {
            case ItemsRepeater repeater:
                repeater.ItemsSource = items;
                break;
            case RadioButtons radio:
                radio.ItemsSource = items;
                Choose(radio);
                break;
        }
    }

    /// <summary>One list's feed: started on load and on a new source while loaded, stopped on unload.</summary>
    private sealed class Follower
    {
        private readonly FrameworkElement _list;
        private object? _source;
        private SnapshotFeed? _feed;
        private bool _loaded;

        internal Follower(FrameworkElement list)
        {
            _list = list;
            _loaded = list.IsLoaded;
            list.Loaded += (_, _) =>
            {
                _loaded = true;
                Start();
            };
            list.Unloaded += (_, _) =>
            {
                _loaded = false;
                Stop();
                Show(_list, null);
            };
        }

        internal void Follow(object? source)
        {
            _source = source;
            if (_loaded)
            {
                Start();
            }
        }

        private void Start()
        {
            Stop();
            if (_source is INotifyCollectionChanged observable)
            {
                _feed = new SnapshotFeed(
                    observable,
                    work => _list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => work()),
                    items => Show(_list, items));
            }
            else
            {
                Show(_list, _source);
            }
        }

        private void Stop()
        {
            _feed?.Stop();
            _feed = null;
        }
    }
}
