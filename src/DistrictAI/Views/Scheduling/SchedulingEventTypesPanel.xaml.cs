using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The event types: each one's name, how long it runs and where, its state
/// and its booking page, which a click opens, and "Create event type", whose
/// form is a dialog confirmed once (<see cref="SchedulingDialogHost"/>).
/// </summary>
public sealed partial class SchedulingEventTypesPanel : UserControl
{
    private readonly SchedulingDialogHost _create = new("EventTypeCreateDialog");
    private PageContext? _context;

    /// <summary>A panel showing nothing until <see cref="Attach"/>.</summary>
    public SchedulingEventTypesPanel()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Loaded += (_, _) => SyncDialogs();
    }

    /// <summary>What the panel shows, and its actions.</summary>
    public SchedulingEventTypesViewModel ViewModel { get; private set; } = new();

    /// <summary>Shows <paramref name="model"/>, the page's own, from now on.</summary>
    internal void Attach(SchedulingEventTypesViewModel model, PageContext context)
    {
        ViewModel = model;
        _context = context;
        Status.Attach(model.Load);
        Bindings.Update();
    }

    /// <summary>Opens the create form while the core holds it open, and closes it once it does not.</summary>
    internal void SyncDialogs() =>
        _create.Sync(this, _context, ViewModel.Create.Dialog, () => new EventTypeEditorContent(ViewModel.Create));

    /// <summary>Closes the dialog without asking the core: the page left the screen.</summary>
    internal void CloseDialogs() => _create.Close();

    private void OnEventTypeClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is EventTypeEntry row)
        {
            ViewModel.Open(row);
        }
    }
}
