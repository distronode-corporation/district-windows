using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>One contact: their details and the AI dossier. Read-only in this build.</summary>
public sealed partial class ContactDetailViewModel : ObservableObject
{
    /// <summary>Loading and failure.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>Phone number, email address, company and the rest, then when the contact was added and last changed.</summary>
    public ObservableCollection<FactItem> Facts { get; } = [];

    /// <summary>What is known about the phone number: location, line type, carrier.</summary>
    public ObservableCollection<FactItem> NumberFacts { get; } = [];

    /// <summary>The contact, as the core names it.</summary>
    public string ContactId { get; private set; } = string.Empty;

    /// <summary>What a report is about.</summary>
    public ReportAvailability Report { get; private set; }

    /// <summary>Their name.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>The number and address on one line, under the name.</summary>
    [ObservableProperty]
    public partial string Reach { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Reach"/>.</summary>
    [ObservableProperty]
    public partial bool HasReach { get; set; }

    /// <summary>Whether anything is known about the phone number.</summary>
    [ObservableProperty]
    public partial bool HasNumberFacts { get; set; }

    /// <summary>Where the web research stands, or empty.</summary>
    [ObservableProperty]
    public partial string ResearchStatus { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ResearchStatus"/>.</summary>
    [ObservableProperty]
    public partial bool HasResearchStatus { get; set; }

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
        Reach = view.Reach;
        HasReach = Reach.Length > 0;
        List<FactItem> facts = [.. view.Facts.Select(FactItem.From)];
        AddWhen(facts, "Added", view.CreatedAt);
        AddWhen(facts, "Last changed", view.UpdatedAt);
        Display.Sync(Facts, facts);
        Display.Sync(NumberFacts, [.. view.NumberFacts.Select(FactItem.From)]);
        HasNumberFacts = view.NumberFacts.Length > 0;
        ResearchStatus = view.ResearchStatus ?? string.Empty;
        HasResearchStatus = ResearchStatus.Length > 0;
        HasDossier = view.Dossier is not null;
        DossierLabel = view.Dossier?.Label ?? string.Empty;
        DossierText = view.Dossier?.Text ?? string.Empty;
        HasFailure = view.Failure is not null;
        FailureMessage = Display.Failure(view.Failure);
        ReportLabel = Display.ReportLabel(view.Report);
        ReportVisible = ReportLabel.Length > 0;
        ReportEnabled = !reportSending;
    }

    private static void AddWhen(List<FactItem> facts, string label, string? iso)
    {
        var when = Display.When(iso);
        if (when.Length > 0)
        {
            facts.Add(new FactItem(label, when));
        }
    }

    /// <summary>What a report about this contact is about.</summary>
    internal ReportTarget Target() => new ReportTarget.Contact(ContactId);
}
