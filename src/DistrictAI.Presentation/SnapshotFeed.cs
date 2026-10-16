using System.Collections;
using System.Collections.Specialized;

namespace DistrictAI.ViewModels;

/// <summary>
/// A list a view shows, as snapshots of an observable collection rather than
/// the collection itself. A XAML list bound to the collection handles each
/// change synchronously, inside the core's render, and an ItemsRepeater in a
/// dialog can fail that (a COMException with E_FAIL, which ends the app) while
/// the dialog is opening or the list is being laid out. Here a change only
/// asks for the next snapshot, posted once until it runs (<c>post</c>: the UI
/// thread's queue, after layout), and the view gets a copy it alone holds.
/// Stopped, it follows the collection no more, so a closed dialog's list never
/// hears of a change again.
/// </summary>
internal sealed class SnapshotFeed
{
    private readonly IEnumerable _items;
    private readonly INotifyCollectionChanged _source;
    private readonly Func<Action, bool> _post;
    private readonly Action<object?[]> _show;
    private bool _posted;
    private bool _stopped;

    /// <summary>
    /// Shows <paramref name="source"/> (an observable collection) now, through
    /// <paramref name="show"/>, and again, posted with <paramref name="post"/>,
    /// after each run of changes.
    /// </summary>
    internal SnapshotFeed(INotifyCollectionChanged source, Func<Action, bool> post, Action<object?[]> show)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(show);
        _items = source as IEnumerable ?? throw new ArgumentException("an observable collection of items", nameof(source));
        _source = source;
        _post = post;
        _show = show;
        _source.CollectionChanged += OnChanged;
        _show(Snapshot());
    }

    /// <summary>Stops following the collection; a snapshot already posted shows nothing.</summary>
    internal void Stop()
    {
        _stopped = true;
        _source.CollectionChanged -= OnChanged;
    }

    private object?[] Snapshot() => [.. _items.Cast<object?>()];

    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_posted)
        {
            return;
        }
        _posted = true;
        if (!_post(ShowPosted))
        {
            // The UI thread is gone: nothing is left to show it on.
            _posted = false;
        }
    }

    private void ShowPosted()
    {
        _posted = false;
        if (!_stopped)
        {
            _show(Snapshot());
        }
    }
}
