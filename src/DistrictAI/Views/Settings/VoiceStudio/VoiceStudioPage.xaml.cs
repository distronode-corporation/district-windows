using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.VoiceStudio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.VoiceStudio;

/// <summary>
/// Voice Studio, District Studio's Voice: the recipes, the signal chain and the
/// editor of its open leg, the time to first word, where the call is processed,
/// and the save. Leaving with edits not saved is asked about by the window.
/// </summary>
public sealed partial class VoiceStudioPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public VoiceStudioPage()
    {
        InitializeComponent();
        StudioStatus.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its actions.</summary>
    public VoiceStudioViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(VoiceStudioView view) => ViewModel.Show(view);

    private void OnRecipeClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is StudioRecipeItem recipe)
        {
            ViewModel.ApplyRecipe(recipe);
        }
    }

    private void OnBlockClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is StudioBlockItem block)
        {
            ViewModel.SelectLeg(block);
        }
    }

    private void OnDefaultClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: StudioTuningItem item } box)
        {
            item.UseDefault = box.IsChecked == true;
        }
    }

    private void OnNoticeClosed(InfoBar sender, object args) => ViewModel.DismissNoticeCommand.Execute(null);
}
