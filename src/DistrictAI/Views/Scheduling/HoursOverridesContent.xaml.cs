using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The overrides' editor: the days still to come, each with Delete (asked
/// once more), Add override, and the add form in place of the list.
/// </summary>
public sealed partial class HoursOverridesContent : UserControl
{
    private SnapshotFeed<OverrideEntry>? _rows;

    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public HoursOverridesContent(HoursOverridesViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
        // The list shows snapshots of the rows, each set after layout, never
        // the live collection: an ItemsRepeater handling a change inside the
        // core's render, while its dialog opened, failed with E_FAIL and ended
        // the app. Once the dialog closes, its list follows nothing.
        Loaded += (_, _) =>
        {
            _rows?.Dispose();
            _rows = new SnapshotFeed<OverrideEntry>(
                ViewModel.Rows,
                work => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => work()),
                rows => List.ItemsSource = rows);
        };
        Unloaded += (_, _) =>
        {
            _rows?.Dispose();
            _rows = null;
            List.ItemsSource = null;
        };
    }

    /// <summary>The dialog's fields.</summary>
    public HoursOverridesViewModel ViewModel { get; }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        // An ItemsRepeater whose template uses x:Bind gives what it makes no
        // DataContext, so each Delete carries its row's key in Tag.
        if ((sender as FrameworkElement)?.Tag is string key && ViewModel.Rows.FirstOrDefault(entry => entry.Key == key) is { } row)
        {
            ViewModel.AskDelete(row);
        }
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e) => ViewModel.PickKind(KindChooser.SelectedIndex);
}
