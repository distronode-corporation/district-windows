using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// One event type: its details, its hosts and its booking questions, and its
/// four writes (the editor, the state actions, the hosts and the questions),
/// each a dialog the core holds open (<see cref="SchedulingDialogHost"/>).
/// </summary>
public sealed partial class SchedulingEventTypePanel : UserControl
{
    private readonly SchedulingDialogHost _editor = new("EventTypeEditorDialog");
    private readonly SchedulingDialogHost _actions = new("EventTypeActionsDialog");
    private readonly SchedulingDialogHost _hosts = new("EventTypeHostsDialog");
    private readonly SchedulingDialogHost _questions = new("EventTypeQuestionsDialog");
    private PageContext? _context;

    /// <summary>A panel showing nothing until <see cref="Attach"/>.</summary>
    public SchedulingEventTypePanel()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Loaded += (_, _) => SyncDialogs();
    }

    /// <summary>What the panel shows, and its actions.</summary>
    public SchedulingEventTypeViewModel ViewModel { get; private set; } = new();

    /// <summary>Shows <paramref name="model"/>, the page's own, from now on.</summary>
    internal void Attach(SchedulingEventTypeViewModel model, PageContext context)
    {
        ViewModel = model;
        _context = context;
        Status.Attach(model.Load);
        Bindings.Update();
    }

    /// <summary>Opens the dialog the core holds open, and closes the ones it does not.</summary>
    internal void SyncDialogs()
    {
        _editor.Sync(this, _context, ViewModel.Editor.Dialog, () => new EventTypeEditorContent(ViewModel.Editor));
        _actions.Sync(this, _context, ViewModel.Actions.Dialog, () => new EventTypeActionsContent(ViewModel.Actions));
        _hosts.Sync(this, _context, ViewModel.HostsEditor.Dialog, () => new EventTypeHostsContent(ViewModel.HostsEditor));
        _questions.Sync(this, _context, ViewModel.QuestionsEditor.Dialog, () => new EventTypeQuestionsContent(ViewModel.QuestionsEditor));
    }

    /// <summary>Closes every dialog without asking the core: the page left the screen.</summary>
    internal void CloseDialogs()
    {
        _editor.Close();
        _actions.Close();
        _hosts.Close();
        _questions.Close();
    }
}
