using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>The booking assistant: on or off, and its extra instructions.</summary>
public sealed partial class SettingsAssistantContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public SettingsAssistantContent(SettingsAssistantViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public SettingsAssistantViewModel ViewModel { get; }
}
