using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>One conversation, oldest first, read-only in this build.</summary>
public sealed partial class ThreadViewModel : ObservableObject
{
    private static readonly EmptyView _noMessages = new("No messages in this conversation yet.", string.Empty);

    private PageContext? _context;

    /// <summary>Loading, failure, the empty conversation and refresh.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The conversation, oldest first.</summary>
    public ObservableCollection<TimelineItem> Items { get; } = [];

    /// <summary>The conversation, as the core names it.</summary>
    public string ThreadKey { get; private set; } = string.Empty;

    /// <summary>Who the conversation is with.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>That replies are sent elsewhere.</summary>
    [ObservableProperty]
    public partial string ReadOnlyNote { get; set; } = string.Empty;

    /// <summary>Whether "Older messages" is offered.</summary>
    [ObservableProperty]
    public partial bool HasMore { get; set; }

    /// <summary>Whether older messages are on their way.</summary>
    [ObservableProperty]
    public partial bool LoadingOlder { get; set; }

    /// <summary>Whether "Older messages" can be pressed now.</summary>
    [ObservableProperty]
    public partial bool CanLoadOlder { get; set; }

    /// <summary>Whether the last older page failed.</summary>
    [ObservableProperty]
    public partial bool HasOlderFailure { get; set; }

    /// <summary>Why the last older page failed.</summary>
    [ObservableProperty]
    public partial string OlderFailure { get; set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
    }

    internal void Show(ThreadView view, bool reportSending)
    {
        ThreadKey = view.ThreadKey;
        Title = view.Title;
        ReadOnlyNote = view.ReadOnlyNote;
        Load.Show(view.Status, view.Items.Count > 0, _noMessages, view.Refreshing, view.RefreshFailure);
        Display.Sync(Items, [.. view.Items.Select(item => TimelineItem.From(item, !reportSending))]);
        HasMore = Load.Ready && view.HasMore;
        LoadingOlder = view.LoadingOlder;
        CanLoadOlder = HasMore && !view.LoadingOlder;
        HasOlderFailure = view.OlderFailure is not null;
        OlderFailure = Display.Failure(view.OlderFailure);
    }

    /// <summary>What a report about <paramref name="item"/> is about.</summary>
    internal ReportTarget TargetFor(TimelineItem item) => new ReportTarget.ThreadEvent(ThreadKey, item.Id);

    [RelayCommand]
    private void LoadOlder()
    {
        if (!CanLoadOlder)
        {
            return;
        }
        CanLoadOlder = false;
        _context?.Send(new UiEvent.LoadOlder());
    }
}
