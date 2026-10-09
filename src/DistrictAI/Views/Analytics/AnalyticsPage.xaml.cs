using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Analytics;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Analytics;

/// <summary>
/// Call analytics and metered usage: the period chooser, the calls over the
/// period with their charts, this month's usage and the last few months of it.
/// </summary>
public sealed partial class AnalyticsPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public AnalyticsPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
        ReportStatus.Attach(ViewModel.ReportLoad);
        UsageStatus.Attach(ViewModel.UsageLoad);
        HistoryStatus.Attach(ViewModel.HistoryLoad);
    }

    /// <summary>What the page shows, and its actions.</summary>
    public AnalyticsViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(AnalyticsView view) => ViewModel.Show(view);

    // Only the member's own pick is sent: the chooser also moves when the core
    // says which period is selected, and that is not a pick.
    private void OnPeriodChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = PeriodChooser.SelectedIndex;
        if (index != ViewModel.SelectedPeriod)
        {
            ViewModel.SelectPeriod(index);
        }
    }
}
