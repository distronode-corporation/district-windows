using DistrictAI.Core.Ffi;
using DistrictAI.Platform;
using DistrictAI.ViewModels.Marketplace;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DistrictAI.Views.Marketplace;

/// <summary>
/// The phone numbers screen's Registrations tab, over the page's
/// <see cref="RegistrationsViewModel"/>: the form, what the country asks for,
/// and the workspace's filings. A draft's document is picked in the Windows
/// file chooser (a PDF, PNG or JPEG, <see cref="DistrictFfi.RegistrationDocumentPick"/>)
/// and its type read from its first bytes by the core's boundary.
/// </summary>
public sealed partial class RegistrationsPanel : UserControl
{
    /// <summary>A panel with nothing shown yet.</summary>
    public RegistrationsPanel()
    {
        InitializeComponent();
    }

    /// <summary>What the panel shows, and its actions: the page's.</summary>
    public RegistrationsViewModel Registrations { get; private set; } = new();

    /// <summary>Shows <paramref name="registrations"/>, the page's.</summary>
    internal void Attach(RegistrationsViewModel registrations)
    {
        Registrations = registrations;
        ListStatus.Attach(registrations.ListLoad);
        Bindings.Update();
    }

    /// <summary>Enter in the country asks what it requires, as the Linux app's does.</summary>
    private void OnCountryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && Registrations.CheckRequirementsCommand.CanExecute(null))
        {
            Registrations.CheckRequirementsCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Opens the file chooser over the window for a draft's document, and sends the file picked.</summary>
    private async void OnUploadClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not UploadItem upload
            || !RegistrationsViewModel.MayPick(upload)
            || XamlRoot?.ContentIslandEnvironment is not { } island)
        {
            return;
        }
        var picker = new FilePicker(Win32Interop.GetWindowFromWindowId(island.AppWindowId));
        var picked = await picker.PickAsync(DistrictFfi.RegistrationDocumentPick()).ConfigureAwait(true);
        Registrations.Picked(upload, picked);
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FilingDocumentItem document)
        {
            Registrations.Remove(document);
        }
    }

    private void OnFileClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FilingItem filing)
        {
            Registrations.AskFile(filing);
        }
    }
}
