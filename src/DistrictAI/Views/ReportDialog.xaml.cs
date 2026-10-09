using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>
/// Reporting AI-generated content (a call summary, a dossier): an optional
/// note, then Send. A member whose role cannot send support requests is sent
/// to the support page on the web instead. While a support request is being
/// written in Support, the dialog says so instead of sending, and keeps the
/// note.
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
            dialog.Note.Text = context.TakeReportNote(target);
            // A support request being written in Support shares the form a
            // report fills: the dialog says so and does not send, and keeps
            // the note for when the request is sent or discarded.
            var refusal = context.ReportRefusal;
            if (refusal is not null)
            {
                dialog.Refusal.Message = refusal;
                dialog.Refusal.IsOpen = true;
                dialog.IsPrimaryButtonEnabled = false;
            }
            var result = await dialog.ShowAsync();
            var note = dialog.Note.Text.Trim();
            if (refusal is null && result == ContentDialogResult.Primary)
            {
                context.Send(new UiEvent.Report(target, note));
            }
            else if (refusal is not null)
            {
                context.KeepReportNote(target, note);
            }
        }
        finally
        {
            context.DialogOpen = false;
        }
    }
}
