using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace DistrictAI.ViewModels;

/// <summary>
/// A list a view shows, as snapshots of an <see cref="ObservableCollection{T}"/>
/// rather than the collection itself. A XAML list bound to the collection
/// handles each change synchronously, inside the core's render, and an
/// ItemsRepeater in a dialog can fail that (a COMException with E_FAIL, which
/// ends the app) while the dialog is opening or the list is being laid out.
/// Here a change only asks for the next snapshot, posted once until it runs
/// (<c>post</c>: the UI thread's queue, after layout), and the view gets a
/// copy it alone holds. Disposed, it stops following the collection, so a
/// closed dialog's list never hears of a change again.
/// </summary>
internal sealed class SnapshotFeed<T> : IDisposable
{
    private readonly ObservableCollection<T> _source;
    private readonly Func<Action, bool> _post;
    private readonly Action<IReadOnlyList<T>> _show;
    private bool _posted;
    private bool _disposed;

    /// <summary>
    /// Shows <paramref name="source"/> now, through <paramref name="show"/>,
    /// and again, posted with <paramref name="post"/>, after each run of changes.
    /// </summary>
    internal SnapshotFeed(ObservableCollection<T> source, Func<Action, bool> post, Action<IReadOnlyList<T>> show)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(show);
        _source = source;
        _post = post;
        _show = show;
        _source.CollectionChanged += OnChanged;
        _show([.. _source]);
    }

    /// <summary>Stops following the collection; a snapshot already posted shows nothing.</summary>
    public void Dispose()
    {
        _disposed = true;
        _source.CollectionChanged -= OnChanged;
    }

    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_posted)
        {
            return;
        }
        _posted = true;
        if (!_post(Snapshot))
        {
            // The UI thread is gone: nothing is left to show it on.
            _posted = false;
        }
    }

    private void Snapshot()
    {
        _posted = false;
        if (!_disposed)
        {
            _show([.. _source]);
        }
    }
}
