using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>
/// Reporting AI-generated content (a call summary, a dossier): an optional
/// note, then Send. A member whose role cannot send support requests is sent
/// to the support page on the web instead.
/// </summary>
public sealed partial class ReportDialog : ContentDialog
{
    /// <summary>Where a member who cannot report in the app reports instead.</summary>
    internal const string SupportUrl = "https://www.distronode.com/support";

    /// <summary>An empty dialog.</summary>
    public ReportDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// What a Report button does for <paramref name="availability"/>: asks for
    /// a note and sends the report about <paramref name="target"/>, or opens
    /// the support page in the browser. One press sends at most one report.
    /// </summary>
    internal static async Task OfferAsync(XamlRoot root, PageContext context, ReportAvailability availability, ReportTarget target)
    {
        switch (availability)
        {
            case ReportAvailability.OnWeb:
                await context.OpenAsync(SupportUrl).ConfigureAwait(true);
                return;
            case ReportAvailability.InApp:
                break;
            case ReportAvailability.Hidden:
            default:
                return;
        }
        if (context.ReportSending || context.DialogOpen)
        {
            return;
        }
        context.DialogOpen = true;
        try
        {
            var dialog = new ReportDialog { XamlRoot = root };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                context.Send(new UiEvent.Report(target, dialog.Note.Text.Trim()));
            }
        }
        finally
        {
            context.DialogOpen = false;
        }
    }
}
