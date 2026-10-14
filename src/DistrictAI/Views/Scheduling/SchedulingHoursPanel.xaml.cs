using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The working hours: the week, every day Monday first, and the dated
/// overrides still to come, and their two editors (the week's and the
/// overrides'), each a dialog the core holds open (<see cref="SchedulingDialogHost"/>).
/// </summary>
public sealed partial class SchedulingHoursPanel : UserControl
{
    private readonly SchedulingDialogHost _week = new("HoursWeekDialog");
    private readonly SchedulingDialogHost _overrides = new("HoursOverridesDialog");
    private PageContext? _context;

    /// <summary>A panel showing nothing until <see cref="Attach"/>.</summary>
    public SchedulingHoursPanel()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Loaded += (_, _) => SyncDialogs();
    }

    /// <summary>What the panel shows, and its actions.</summary>
    public SchedulingHoursViewModel ViewModel { get; private set; } = new();

    /// <summary>Shows <paramref name="model"/>, the page's own, from now on.</summary>
    internal void Attach(SchedulingHoursViewModel model, PageContext context)
    {
        ViewModel = model;
        _context = context;
        Status.Attach(model.Load);
        Bindings.Update();
    }

    /// <summary>Opens the editor the core holds open, and closes the one it does not.</summary>
    internal void SyncDialogs()
    {
        _week.Sync(this, _context, ViewModel.WeekEditor.Dialog, () => new HoursWeekContent(ViewModel.WeekEditor));
        _overrides.Sync(this, _context, ViewModel.OverridesEditor.Dialog, () => new HoursOverridesContent(ViewModel.OverridesEditor));
    }

    /// <summary>Closes both editors without asking the core: the page left the screen.</summary>
    internal void CloseDialogs()
    {
        _week.Close();
        _overrides.Close();
    }
}
