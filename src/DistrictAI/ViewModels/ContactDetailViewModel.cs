using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>One contact: their details and the AI dossier. Read-only in this build.</summary>
public sealed partial class ContactDetailViewModel : ObservableObject
{
    /// <summary>Loading and failure.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>Phone number, email address, company and the rest.</summary>
    public ObservableCollection<FactItem> Facts { get; } = [];

    /// <summary>The contact, as the core names it.</summary>
    public string ContactId { get; private set; } = string.Empty;

    /// <summary>What a report is about.</summary>
    public ReportAvailability Report { get; private set; }

    /// <summary>Their name.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Whether there is an AI dossier.</summary>
    [ObservableProperty]
    public partial bool HasDossier { get; set; }

    /// <summary>The label over the dossier.</summary>
    [ObservableProperty]
    public partial string DossierLabel { get; set; } = string.Empty;

    /// <summary>The dossier.</summary>
    [ObservableProperty]
    public partial string DossierText { get; set; } = string.Empty;

    /// <summary>Whether something about the contact failed, over details already shown.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>What failed.</summary>
    [ObservableProperty]
    public partial string FailureMessage { get; set; } = string.Empty;

    /// <summary>The Report button's words.</summary>
    [ObservableProperty]
    public partial string ReportLabel { get; set; } = string.Empty;

    /// <summary>Whether there is a Report button.</summary>
    [ObservableProperty]
    public partial bool ReportVisible { get; set; }

    /// <summary>Whether the Report button can be pressed now.</summary>
    [ObservableProperty]
    public partial bool ReportEnabled { get; set; }

    internal void Attach(PageContext context) => Load.Attach(context);

    internal void Show(ContactDetailView view, bool reportSending)
    {
        ContactId = view.ContactId;
        Report = view.Report;
        Name = view.Name;
        Load.Show(view.Status, hasRows: true, empty: null, refreshing: false, refreshFailure: null);
        Display.Sync(Facts, [.. view.Facts.Select(FactItem.From)]);
        HasDossier = view.Dossier is not null;
        DossierLabel = view.Dossier?.Label ?? string.Empty;
        DossierText = view.Dossier?.Text ?? string.Empty;
        HasFailure = view.Failure is not null;
        FailureMessage = Display.Failure(view.Failure);
        ReportLabel = Display.ReportLabel(view.Report);
        ReportVisible = ReportLabel.Length > 0;
        ReportEnabled = !reportSending;
    }

    /// <summary>What a report about this contact is about.</summary>
    internal ReportTarget Target() => new ReportTarget.Contact(ContactId);
}
