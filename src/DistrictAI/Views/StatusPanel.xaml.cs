using System.ComponentModel;
using DistrictAI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>A page's loading, failed and empty states, shown where its content would be.</summary>
public sealed partial class StatusPanel : UserControl
{
    /// <summary>A panel showing nothing until <see cref="Attach"/>.</summary>
    public StatusPanel()
    {
        InitializeComponent();
        State.PropertyChanged += OnStateChanged;
        ShowWhatApplies();
    }

    /// <summary>The state shown.</summary>
    public LoadStateViewModel State { get; private set; } = new();

    /// <summary>Shows <paramref name="state"/>, the page's own, from now on.</summary>
    internal void Attach(LoadStateViewModel state)
    {
        State.PropertyChanged -= OnStateChanged;
        State = state;
        State.PropertyChanged += OnStateChanged;
        Bindings.Update();
        ShowWhatApplies();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => ShowWhatApplies();

    // Collapsed when there is nothing to say: loaded, with rows to show.
    private void ShowWhatApplies() =>
        Body.Visibility = State.Loading || State.Failed || State.ShowEmpty ? Visibility.Visible : Visibility.Collapsed;
}
