using System.Collections.ObjectModel;
using System.Collections.Specialized;
using DistrictAI.ViewModels;
using Xunit;

namespace DistrictAI.Presentation.Tests;

public sealed class SnapshotFeedTests
{
    /// <summary>A UI thread's queue the test runs by hand, or one that is gone.</summary>
    private sealed class Queue
    {
        private readonly List<Action> _posted = [];

        public bool Gone { get; set; }

        public int Count => _posted.Count;

        public bool Post(Action work)
        {
            if (Gone)
            {
                return false;
            }
            _posted.Add(work);
            return true;
        }

        public void Run()
        {
            var due = _posted.ToArray();
            _posted.Clear();
            foreach (var work in due)
            {
                work();
            }
        }
    }

    /// <summary>Change notifications with no items to read.</summary>
    private sealed class NotAList : INotifyCollectionChanged
    {
        public event NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add { }
            remove { }
        }
    }

    [Fact]
    public void TheViewGetsACopyNowAndOneSnapshotAfterEachRunOfChanges()
    {
        var rows = new ObservableCollection<string> { "a", "b" };
        var queue = new Queue();
        var shown = new List<object?[]>();
        _ = new SnapshotFeed(rows, queue.Post, shown.Add);
        Assert.Equal(["a", "b"], Assert.Single(shown));

        // Changes inside one render reach the view later, once, as they ended.
        rows.RemoveAt(0);
        rows.Add("c");
        rows[0] = "d";
        Assert.Single(shown);
        Assert.Equal(1, queue.Count);
        queue.Run();
        Assert.Equal(["d", "c"], shown[^1]);
        Assert.Equal(2, shown.Count);

        // The view's copy never changes under it.
        var held = shown[^1];
        rows.Clear();
        Assert.Equal(["d", "c"], held);
        queue.Run();
        Assert.Empty(shown[^1]);
    }

    [Fact]
    public void StoppedItFollowsNothingAndAPostedSnapshotShowsNothing()
    {
        var rows = new ObservableCollection<int> { 1 };
        var queue = new Queue();
        var shown = new List<object?[]>();
        var feed = new SnapshotFeed(rows, queue.Post, shown.Add);
        rows.Add(2);
        feed.Stop();
        queue.Run();
        rows.Add(3);
        Assert.Equal(0, queue.Count);
        Assert.Single(shown);
    }

    [Fact]
    public void AGoneUiThreadTakesNothingAndTheNextChangeTriesAgain()
    {
        var rows = new ObservableCollection<int>();
        var queue = new Queue { Gone = true };
        var shown = new List<object?[]>();
        _ = new SnapshotFeed(rows, queue.Post, shown.Add);
        rows.Add(1);
        Assert.Equal(0, queue.Count);
        queue.Gone = false;
        rows.Add(2);
        queue.Run();
        Assert.Equal([1, 2], shown[^1]);
        Assert.Throws<ArgumentNullException>(() => new SnapshotFeed(null!, queue.Post, shown.Add));
        Assert.Throws<ArgumentNullException>(() => new SnapshotFeed(rows, null!, shown.Add));
        Assert.Throws<ArgumentNullException>(() => new SnapshotFeed(rows, queue.Post, null!));
        Assert.Throws<ArgumentException>(() => new SnapshotFeed(new NotAList(), queue.Post, shown.Add));
    }
}
