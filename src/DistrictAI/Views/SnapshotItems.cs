using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using DistrictAI.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>
/// <c>views:SnapshotItems.Source="{x:Bind ViewModel.Rows}"</c> in place of an
/// ItemsRepeater's ItemsSource, in a dialog's content that is made again each
/// time the dialog opens: the repeater shows snapshots of the collection, set
/// at low priority once layout is done (<see cref="SnapshotFeed"/>), from when
/// it loads until it unloads, never the live collection. A repeater handling a
/// change synchronously inside the core's render, while its dialog opened,
/// failed with E_FAIL and ended the app; and a closed dialog's repeater kept
/// hearing of every change. A source that is not observable is shown as it is.
/// </summary>
public static class SnapshotItems
{
    /// <summary>The collection the repeater shows snapshots of.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(object), typeof(SnapshotItems), new PropertyMetadata(null, OnSourceChanged));

    private static readonly ConditionalWeakTable<ItemsRepeater, Follower> _followers = new();

    /// <summary>The collection <paramref name="repeater"/> shows snapshots of.</summary>
    public static object? GetSource(ItemsRepeater repeater)
    {
        ArgumentNullException.ThrowIfNull(repeater);
        return repeater.GetValue(SourceProperty);
    }

    /// <summary>Has <paramref name="repeater"/> show snapshots of <paramref name="value"/>.</summary>
    public static void SetSource(ItemsRepeater repeater, object? value)
    {
        ArgumentNullException.ThrowIfNull(repeater);
        repeater.SetValue(SourceProperty, value);
    }

    private static void OnSourceChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ItemsRepeater repeater)
        {
            _followers.GetValue(repeater, created => new Follower(created)).Follow(e.NewValue);
        }
    }

    /// <summary>One repeater's feed: started on load and on a new source while loaded, stopped on unload.</summary>
    private sealed class Follower
    {
        private readonly ItemsRepeater _repeater;
        private object? _source;
        private SnapshotFeed? _feed;
        private bool _loaded;

        internal Follower(ItemsRepeater repeater)
        {
            _repeater = repeater;
            _loaded = repeater.IsLoaded;
            repeater.Loaded += (_, _) =>
            {
                _loaded = true;
                Start();
            };
            repeater.Unloaded += (_, _) =>
            {
                _loaded = false;
                Stop();
                _repeater.ItemsSource = null;
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
                    work => _repeater.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => work()),
                    items => _repeater.ItemsSource = items);
            }
            else
            {
                _repeater.ItemsSource = _source;
            }
        }

        private void Stop()
        {
            _feed?.Stop();
            _feed = null;
        }
    }
}
