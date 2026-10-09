using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Blocked;

/// <summary>
/// One blocked caller, as the list shows them: the name, then the number when
/// it is not the name, and when they were blocked.
/// </summary>
public sealed record BlockedRowItem(string ContactId, string Name, string Detail, bool Unblocking, bool CanUnblock)
{
    /// <summary>Whether there is a <see cref="Detail"/>.</summary>
    public bool HasDetail => Detail.Length > 0;

    /// <summary>Whether "Unblock" shows for this caller: the member may unblock, and theirs is not on its way.</summary>
    public bool ShowUnblock => CanUnblock && !Unblocking;

    /// <summary>The Unblock button's name for screen readers: which caller it unblocks.</summary>
    public string UnblockName => "Unblock " + Name;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => string.Join(
        ", ",
        new[] { Name, Detail, Unblocking ? "unblocking" : string.Empty }.Where(part => part.Length > 0));

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;

    internal static BlockedRowItem From(BlockedRowView row, bool canUnblock)
    {
        var when = Display.When(row.BlockedAt);
        var detail = string.Join(
            " · ",
            new[] { row.PhoneNumber ?? string.Empty, when.Length > 0 ? "Blocked " + when : string.Empty }
                .Where(part => part.Length > 0));
        return new BlockedRowItem(row.ContactId, row.Name, detail, row.Unblocking, canUnblock);
    }
}

/// <summary>
/// The callers the workspace has blocked, and unblocking one after the core's
/// question, one request per caller at a time.
/// </summary>
public sealed partial class BlockedViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>Loading, failure and the empty list.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The blocked callers.</summary>
    public ObservableCollection<BlockedRowItem> Rows { get; } = [];

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial BlockedView? View { get; set; }

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "Blocked callers";

    /// <summary>Why the last unblock failed, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>Whether the core is asking before an unblock.</summary>
    public bool Confirming { get; private set; }

    /// <summary>The caller asked about, for the question's heading.</summary>
    public string ConfirmName { get; private set; } = string.Empty;

    /// <summary>The question, in the core's words.</summary>
    public string ConfirmQuestion { get; private set; } = string.Empty;

    /// <summary>The button that answers yes, in the core's words.</summary>
    public string ConfirmAction { get; private set; } = string.Empty;

    /// <summary>The caller whose Unblock was pressed and not yet answered, so a double click asks once.</summary>
    private string? _asked;

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
    }

    internal void Show(BlockedView view)
    {
        View = view;
        Title = view.Title;
        Load.Show(view.Status, view.Rows.Length > 0, view.Empty, refreshing: false, refreshFailure: null);
        Display.Sync(Rows, [.. view.Rows.Select(row => BlockedRowItem.From(row, view.CanUnblock))]);
        Failure = Display.Failure(view.Failure);
        HasFailure = Failure.Length > 0;
        Confirming = view.Confirming is not null;
        ConfirmName = view.Confirming?.Name ?? string.Empty;
        ConfirmQuestion = view.Confirming?.Question ?? string.Empty;
        ConfirmAction = view.Confirming?.Action ?? string.Empty;
        _asked = null;
    }

    /// <summary>"Unblock" on <paramref name="row"/>: the core asks first.</summary>
    internal void AskUnblock(BlockedRowItem row)
    {
        if (!row.ShowUnblock || _asked is not null)
        {
            return;
        }
        _asked = row.ContactId;
        Send(new BlockedAction.AskUnblock(row.ContactId));
    }

    /// <summary>The answer to the core's question.</summary>
    internal void Answer(bool confirmed) =>
        Send(confirmed ? new BlockedAction.ConfirmUnblock() : new BlockedAction.CancelUnblock());

    /// <summary>Puts the last unblock's failure away.</summary>
    [RelayCommand]
    private void DismissFailure() => Send(new BlockedAction.DismissFailure());

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(BlockedAction action) => _context?.Send(new UiEvent.Blocked(action));
}
