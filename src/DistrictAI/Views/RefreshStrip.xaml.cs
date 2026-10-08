using DistrictAI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>A thin strip over a page's content: a refresh under way, or why the last one failed.</summary>
public sealed partial class RefreshStrip : UserControl
{
    /// <summary>A strip showing nothing until <see cref="Attach"/>.</summary>
    public RefreshStrip()
    {
        InitializeComponent();
    }

    /// <summary>The state shown.</summary>
    public LoadStateViewModel State { get; private set; } = new();

    /// <summary>Shows <paramref name="state"/>, the page's own, from now on.</summary>
    internal void Attach(LoadStateViewModel state)
    {
        State = state;
        Bindings.Update();
    }
}
