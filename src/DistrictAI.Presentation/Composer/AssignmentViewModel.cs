using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Composer;

/// <summary>
/// Who owns the conversation, in its header, copied from the core's
/// <see cref="AssignmentView"/>: a picker of "Unassigned" and the members the
/// core offers for a member who may assign, or the owner's line for one who
/// may not. A choice is sent once and the picker waits, switched off, until
/// the core has the service's answer.
/// </summary>
public sealed partial class AssignmentViewModel : ObservableObject
{
    private PageContext? _context;
    private bool _writing;
    private AssigneeChoice? _shown;

    /// <summary>Whether the owner shows at all: a conversation is open.</summary>
    [ObservableProperty]
    public partial bool Visible { get; set; }

    /// <summary>The picker's label: "Assigned to".</summary>
    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    /// <summary>The owner in one line: "Assigned to ada@example.com", or "Unassigned".</summary>
    [ObservableProperty]
    public partial string Line { get; set; } = string.Empty;

    /// <summary>Whether the picker shows: the member may assign.</summary>
    [ObservableProperty]
    public partial bool ShowPicker { get; set; }

    /// <summary>Whether the owner's line shows instead of the picker.</summary>
    [ObservableProperty]
    public partial bool ShowLine { get; set; }

    /// <summary>The picker's choices, "Unassigned" first.</summary>
    public ObservableCollection<AssigneeChoice> Options { get; } = [];

    /// <summary>The choice showing.</summary>
    [ObservableProperty]
    public partial AssigneeChoice? SelectedOption { get; set; }

    /// <summary>Whether the picker works now.</summary>
    [ObservableProperty]
    public partial bool CanAssign { get; set; }

    /// <summary>Whether a change is on its way.</summary>
    [ObservableProperty]
    public partial bool Saving { get; set; }

    /// <summary>Why the last change failed, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    internal void Attach(PageContext context) => _context = context;

    /// <summary>Shows <paramref name="view"/>, or hides the owner when there is none.</summary>
    internal void Show(AssignmentView? view)
    {
        Visible = view is not null;
        if (view is null)
        {
            CanAssign = false;
            return;
        }
        _writing = true;
        try
        {
            Label = view.Label;
            Line = view.Line;
            ShowPicker = view.ShowPicker;
            ShowLine = !view.ShowPicker;
            Display.Sync(Options, [.. view.Options.Select(option => new AssigneeChoice(option.Email, option.Label))]);
            var selected = (int)view.Selected;
            _shown = selected < Options.Count ? Options[selected] : null;
            SelectedOption = _shown;
            CanAssign = view.CanAssign;
            Saving = view.Saving;
            Failure = Display.Failure(view.Failure);
            HasFailure = view.Failure is not null;
        }
        finally
        {
            _writing = false;
        }
    }

    partial void OnSelectedOptionChanged(AssigneeChoice? value)
    {
        if (_writing || value is null || _context is null || !CanAssign || value == _shown)
        {
            return;
        }
        CanAssign = false;
        _shown = value;
        _context.Send(new UiEvent.Composer(new ComposerAction.Assign(value.Email)));
    }

    /// <summary>Puts the failure line away.</summary>
    [RelayCommand]
    private void DismissFailure()
    {
        HasFailure = false;
        _context?.Send(new UiEvent.Composer(new ComposerAction.DismissAssignFailure()));
    }
}

/// <summary>One choice of the owner picker.</summary>
/// <param name="Email">The member's address, or null for "Unassigned".</param>
/// <param name="Label">What the choice reads.</param>
public sealed record AssigneeChoice(string? Email, string Label)
{
    /// <inheritdoc/>
    public override string ToString() => Label;
}
