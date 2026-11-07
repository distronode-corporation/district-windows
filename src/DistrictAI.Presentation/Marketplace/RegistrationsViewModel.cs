using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.L10n;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Marketplace;

/// <summary>A line of countries on the Registrations tab: "Ready to buy today", "Your approvals".</summary>
/// <param name="Label">What the countries are.</param>
/// <param name="Countries">The countries, comma-separated.</param>
public sealed record GateItem(string Label, string Countries);

/// <summary>One thing a regulation asks for.</summary>
/// <param name="Name">Its name, as the carrier writes it.</param>
/// <param name="Line">Its kind and the fields it asks for.</param>
public sealed record RequirementItem(string Name, string Line);

/// <summary>One document on a filing.</summary>
/// <param name="BundleId">The filing.</param>
/// <param name="Id">The document.</param>
/// <param name="Name">The requirement it meets.</param>
/// <param name="State">Where it is: filed, stored, or recorded with no file yet.</param>
/// <param name="RemoveAction">"Remove", on a draft's document, or empty.</param>
/// <param name="Armed">Whether Remove works now.</param>
public sealed record FilingDocumentItem(string BundleId, string Id, string Name, string State, string RemoveAction, bool Armed)
{
    /// <summary>Whether Remove is offered: on a draft.</summary>
    public bool Removable => RemoveAction.Length > 0;
}

/// <summary>One document a draft's regulation asks for.</summary>
/// <param name="BundleId">The draft.</param>
/// <param name="RequirementName">The requirement the upload names.</param>
/// <param name="Label">"Upload Proof of address".</param>
/// <param name="Armed">Whether it works now.</param>
public sealed record UploadItem(string BundleId, string RequirementName, string Label, bool Armed);

/// <summary>
/// One end-user detail a draft's regulation asks for, as typed. A change goes
/// to the core with the draft's other details, and the core's copy is never
/// written back over the typing.
/// </summary>
public sealed partial class DetailItem : ObservableObject
{
    private readonly Action<DetailItem>? _typed;

    internal DetailItem(string bundleId, string field, string value, Action<DetailItem>? typed)
    {
        BundleId = bundleId;
        Field = field;
        _typed = null;
        Value = value;
        _typed = typed;
    }

    /// <summary>The draft.</summary>
    public string BundleId { get; }

    /// <summary>The field's name, as the regulation lists it: its label too.</summary>
    public string Field { get; }

    /// <summary>What is typed.</summary>
    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    partial void OnValueChanged(string value) => _typed?.Invoke(this);
}

/// <summary>
/// One filing, copied from the core's <see cref="FilingView"/>. Two items are
/// equal when the core says the same of the filing, whatever was typed in its
/// fields since, so the list keeps a draft's fields (and the keyboard in
/// them) across the core's snapshots.
/// </summary>
public sealed class FilingItem : IEquatable<FilingItem>
{
    private readonly string _key;

    private FilingItem(FilingView view, IReadOnlyList<DetailItem> details)
    {
        Id = view.Id;
        Title = view.Title;
        FriendlyName = view.FriendlyName ?? string.Empty;
        Status = view.Status;
        Tone = view.Tone;
        ReasonsEyebrow = view.ReasonsEyebrow ?? string.Empty;
        Reasons = string.Join(Environment.NewLine, view.Reasons);
        DocumentsEyebrow = view.DocumentsEyebrow ?? string.Empty;
        Documents = [.. view.Documents.Select(document =>
            new FilingDocumentItem(view.Id, document.Id, document.Name, document.State, document.RemoveAction ?? string.Empty, view.Armed))];
        Details = details;
        Uploads = [.. view.Uploads.Select(upload => new UploadItem(view.Id, upload.RequirementName, upload.Label, view.Armed))];
        FileAction = view.FileAction ?? string.Empty;
        CanFile = view.CanFile;
        _key = string.Join(
            "\u001f",
            new[]
            {
                view.Id, view.Title, FriendlyName, view.Status, view.Tone.ToString(), Reasons,
                string.Join("\u001e", Documents.Select(document => document.ToString())),
                string.Join("\u001e", details.Select(detail => detail.Field)),
                string.Join("\u001e", Uploads.Select(upload => upload.ToString())),
                FileAction, view.Armed.ToString(), view.CanFile.ToString(),
            });
    }

    /// <summary>Its id.</summary>
    public string Id { get; }

    /// <summary>"DE · Local".</summary>
    public string Title { get; }

    /// <summary>The name it was given, or empty.</summary>
    public string FriendlyName { get; }

    /// <summary>Whether it has a <see cref="FriendlyName"/>.</summary>
    public bool HasFriendlyName => FriendlyName.Length > 0;

    /// <summary>Its status, in words.</summary>
    public string Status { get; }

    /// <summary>How the status reads.</summary>
    public FilingTone Tone { get; }

    /// <summary>Whether the status is a refusal.</summary>
    public bool Refused => Tone == FilingTone.Danger;

    /// <summary>Whether the status is an approval.</summary>
    public bool Approved => Tone == FilingTone.Success;

    /// <summary>"What the carrier objected to", or empty.</summary>
    public string ReasonsEyebrow { get; }

    /// <summary>The carrier's objections, one per line.</summary>
    public string Reasons { get; }

    /// <summary>Whether the carrier objected.</summary>
    public bool HasReasons => Reasons.Length > 0;

    /// <summary>"Documents", or empty.</summary>
    public string DocumentsEyebrow { get; }

    /// <summary>Its documents.</summary>
    public IReadOnlyList<FilingDocumentItem> Documents { get; }

    /// <summary>Whether it has documents.</summary>
    public bool HasDocuments => Documents.Count > 0;

    /// <summary>The end-user details its regulation asks for, on a draft of the country checked.</summary>
    public IReadOnlyList<DetailItem> Details { get; }

    /// <summary>Whether there are <see cref="Details"/>.</summary>
    public bool HasDetails => Details.Count > 0;

    /// <summary>The documents its regulation asks for, on a draft of the country checked.</summary>
    public IReadOnlyList<UploadItem> Uploads { get; }

    /// <summary>"File it", on a draft, or empty.</summary>
    public string FileAction { get; }

    /// <summary>Whether "File it" is offered: on a draft.</summary>
    public bool Fileable => FileAction.Length > 0;

    /// <summary>Whether "File it" works now.</summary>
    public bool CanFile { get; }

    internal static FilingItem From(FilingView view, FilingItem? shown, Action<DetailItem> typed)
    {
        // A draft's fields are the member's: kept from the item on screen, so
        // the core's copy of what was typed never writes over newer typing.
        IReadOnlyList<DetailItem> details = [.. view.Details.Select(field =>
            shown?.Details.FirstOrDefault(kept => kept.Field == field.Field)
            ?? new DetailItem(view.Id, field.Field, field.Value, typed))];
        return new FilingItem(view, details);
    }

    /// <inheritdoc />
    public bool Equals(FilingItem? other) => other is not null && other._key == _key;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FilingItem);

    /// <inheritdoc />
    public override int GetHashCode() => _key.GetHashCode(StringComparison.Ordinal);
}

/// <summary>
/// The phone numbers screen's Registrations tab, copied from the core's
/// <see cref="RegistrationsTabView"/>: what a country's regulator asks for,
/// starting a draft filing, and the workspace's filings, with a draft's
/// details, its documents and filing it (asked first by the core). What is
/// typed in the form goes to the core whole as it changes. Every word is the
/// core's.
/// </summary>
public sealed partial class RegistrationsViewModel : ObservableObject
{
    private readonly TextEcho _countryEcho = new();
    private readonly TextEcho _nameEcho = new();
    private PageContext? _context;
    private bool _writing;
    private string[] _numberTypeValues = [];
    private string[] _endUserValues = [];
    private RegistrationFormInput? _form;

    /// <summary>A tab with nothing shown yet.</summary>
    public RegistrationsViewModel()
    {
        ListLoad = new LoadStateViewModel
        {
            RetryEvent = () => new UiEvent.Marketplace(new MarketplaceAction.Numbers(new NumberAdminAction.ReloadRegistrations())),
        };
    }

    /// <summary>Loading and failure for the filings; "Try again" reads them again.</summary>
    public LoadStateViewModel ListLoad { get; }

    /// <summary>The number types, by their labels.</summary>
    public ObservableCollection<string> NumberTypes { get; } = [];

    /// <summary>The end-user types, by their labels.</summary>
    public ObservableCollection<string> EndUserTypes { get; } = [];

    /// <summary>What the form's country asks for.</summary>
    public ObservableCollection<RequirementItem> Requirements { get; } = [];

    /// <summary>The countries open today, and the workspace's approvals.</summary>
    public ObservableCollection<GateItem> Gates { get; } = [];

    /// <summary>The filings, newest first.</summary>
    public ObservableCollection<FilingItem> Filings { get; } = [];

    /// <summary>Whether the member may file. A viewer sees the heading alone.</summary>
    [ObservableProperty]
    public partial bool Member { get; set; }

    /// <summary>"Regulatory registrations".</summary>
    [ObservableProperty]
    public partial string Eyebrow { get; set; } = string.Empty;

    /// <summary>"Start a registration".</summary>
    [ObservableProperty]
    public partial string StartTitle { get; set; } = string.Empty;

    /// <summary>The country field's label.</summary>
    [ObservableProperty]
    public partial string CountryLabel { get; set; } = string.Empty;

    /// <summary>The name field's label.</summary>
    [ObservableProperty]
    public partial string NameLabel { get; set; } = string.Empty;

    /// <summary>The country, as typed.</summary>
    [ObservableProperty]
    public partial string Country { get; set; } = string.Empty;

    /// <summary>The name, as typed.</summary>
    [ObservableProperty]
    public partial string FriendlyName { get; set; } = string.Empty;

    /// <summary>Which of <see cref="NumberTypes"/> is chosen.</summary>
    [ObservableProperty]
    public partial int NumberTypeIndex { get; set; } = -1;

    /// <summary>Which of <see cref="EndUserTypes"/> is chosen.</summary>
    [ObservableProperty]
    public partial int EndUserIndex { get; set; } = -1;

    /// <summary>"Check requirements".</summary>
    [ObservableProperty]
    public partial string CheckAction { get; set; } = string.Empty;

    /// <summary>Whether it works now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckRequirementsCommand))]
    public partial bool CanCheck { get; set; }

    /// <summary>"Start registration".</summary>
    [ObservableProperty]
    public partial string StartAction { get; set; } = string.Empty;

    /// <summary>Whether it works now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool CanStart { get; set; }

    /// <summary>What the last write said, and why it is not offered again, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/> that is news.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>Whether there is a <see cref="Notice"/> that is a failure.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>Whether a write is on its way.</summary>
    [ObservableProperty]
    public partial bool Working { get; set; }

    /// <summary>Whether the requirements show: once asked.</summary>
    [ObservableProperty]
    public partial bool ShowRequirements { get; set; }

    /// <summary>"What this country asks for".</summary>
    [ObservableProperty]
    public partial string RequirementsEyebrow { get; set; } = string.Empty;

    /// <summary>Whether the requirements are being asked.</summary>
    [ObservableProperty]
    public partial bool RequirementsLoading { get; set; }

    /// <summary>The regulation's name, or "DE · No registration required".</summary>
    [ObservableProperty]
    public partial string RequirementsHeading { get; set; } = string.Empty;

    /// <summary>Why there is nothing to file, or empty.</summary>
    [ObservableProperty]
    public partial string RequirementsBody { get; set; } = string.Empty;

    /// <summary>That numbers there cannot be bought through Distronode at all, or empty.</summary>
    [ObservableProperty]
    public partial string RequirementsNote { get; set; } = string.Empty;

    /// <summary>Why the lookup failed, or empty.</summary>
    [ObservableProperty]
    public partial string RequirementsFailure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="RequirementsHeading"/>.</summary>
    [ObservableProperty]
    public partial bool HasRequirementsHeading { get; set; }

    /// <summary>Whether there is a <see cref="RequirementsBody"/>.</summary>
    [ObservableProperty]
    public partial bool HasRequirementsBody { get; set; }

    /// <summary>Whether there is a <see cref="RequirementsNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasRequirementsNote { get; set; }

    /// <summary>Whether there is a <see cref="RequirementsFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasRequirementsFailure { get; set; }

    /// <summary>Whether "Try again" is offered for the lookup.</summary>
    [ObservableProperty]
    public partial bool CanRetryRequirements { get; set; }

    /// <summary>Whether there are filings: the status caption shows over them.</summary>
    [ObservableProperty]
    public partial bool HasFilings { get; set; }

    /// <summary>That a status is read only when the list is, or empty.</summary>
    [ObservableProperty]
    public partial string StatusCaption { get; set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        _context = context;
        ListLoad.Attach(context);
    }

    internal void Show(RegistrationsTabView view)
    {
        Member = view.Member;
        Eyebrow = view.Eyebrow;
        StartTitle = view.StartTitle;
        CountryLabel = view.CountryLabel;
        NameLabel = view.NameLabel;
        CheckAction = view.CheckAction;
        CanCheck = view.CanCheck;
        StartAction = view.StartAction;
        CanStart = view.CanStart;
        var notice = string.Join(" ", new[] { view.Notice, view.Unrepeatable }.OfType<string>().Where(part => part.Length > 0));
        Notice = notice;
        HasFailure = notice.Length > 0 && view.Failed;
        HasNotice = notice.Length > 0 && !view.Failed;
        Working = view.Working;
        _form = view.Form;
        _writing = true;
        try
        {
            _numberTypeValues = [.. view.NumberTypes.Select(choice => choice.Value)];
            Display.Sync(NumberTypes, [.. view.NumberTypes.Select(choice => choice.Label)]);
            NumberTypeIndex = Array.IndexOf(_numberTypeValues, view.Form.NumberType);
            _endUserValues = [.. view.EndUserTypes.Select(choice => choice.Value)];
            Display.Sync(EndUserTypes, [.. view.EndUserTypes.Select(choice => choice.Label)]);
            EndUserIndex = Array.IndexOf(_endUserValues, view.Form.EndUserType);
            if (_countryEcho.Write(view.Form.Country, Country))
            {
                Country = view.Form.Country;
            }
            if (_nameEcho.Write(view.Form.FriendlyName, FriendlyName))
            {
                FriendlyName = view.Form.FriendlyName;
            }
        }
        finally
        {
            _writing = false;
        }
        ShowRequirementsOf(view.Requirements);
        ListLoad.Show(view.ListStatus, view.Filings.Length > 0, view.Empty, false, null);
        StatusCaption = view.StatusCaption ?? string.Empty;
        HasFilings = view.Filings.Length > 0;
        Display.Sync(Gates, [.. view.Gates.Select(gate => new GateItem(gate.Label, gate.Countries))]);
        var shown = Filings.ToDictionary(filing => filing.Id);
        Display.Sync(Filings, [.. view.Filings.Select(filing => FilingItem.From(filing, shown.GetValueOrDefault(filing.Id), Typed))]);
    }

    private void ShowRequirementsOf(RequirementsView requirements)
    {
        ShowRequirements = requirements is not RequirementsView.Idle;
        RequirementsLoading = requirements is RequirementsView.Loading;
        (RequirementsEyebrow, RequirementsHeading, RequirementsBody, RequirementsNote, RequirementsFailure, CanRetryRequirements) = requirements switch
        {
            RequirementsView.Loading loading => (loading.Eyebrow, string.Empty, string.Empty, string.Empty, string.Empty, false),
            RequirementsView.Failed failed => (failed.Eyebrow, string.Empty, string.Empty, string.Empty, Display.Failure(failed.Failure), failed.Failure.Retryable),
            RequirementsView.NoRegulation none => (none.Eyebrow, none.Heading, none.Body, none.NotPurchasable ?? string.Empty, string.Empty, false),
            RequirementsView.Ready ready => (ready.Eyebrow, ready.Name, string.Empty, ready.NotPurchasable ?? string.Empty, string.Empty, false),
            _ => (string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, false),
        };
        HasRequirementsHeading = RequirementsHeading.Length > 0;
        HasRequirementsBody = RequirementsBody.Length > 0;
        HasRequirementsNote = RequirementsNote.Length > 0;
        HasRequirementsFailure = RequirementsFailure.Length > 0;
        Display.Sync(
            Requirements,
            requirements is RequirementsView.Ready { Requirements: var rows }
                ? [.. rows.Select(row => new RequirementItem(row.Name, row.Line))]
                : []);
    }

    private static string Pick(string[] values, int index, string fallback) =>
        index >= 0 && index < values.Length ? values[index] : fallback;

    private void Edited()
    {
        Send(new NumberAdminAction.EditRegistration(new RegistrationFormInput(
            Country,
            Pick(_numberTypeValues, NumberTypeIndex, _form?.NumberType ?? string.Empty),
            Pick(_endUserValues, EndUserIndex, _form?.EndUserType ?? string.Empty),
            FriendlyName)));
    }

    partial void OnCountryChanged(string value)
    {
        if (!_writing)
        {
            _countryEcho.Typed(value);
            Edited();
        }
    }

    partial void OnFriendlyNameChanged(string value)
    {
        if (!_writing)
        {
            _nameEcho.Typed(value);
            Edited();
        }
    }

    partial void OnNumberTypeIndexChanged(int value)
    {
        if (!_writing && value >= 0)
        {
            Edited();
        }
    }

    partial void OnEndUserIndexChanged(int value)
    {
        if (!_writing && value >= 0)
        {
            Edited();
        }
    }

    /// <summary>A draft's detail was typed in: every field of that draft goes to the core.</summary>
    private void Typed(DetailItem detail)
    {
        var filing = Filings.FirstOrDefault(item => item.Id == detail.BundleId);
        if (filing is null)
        {
            return;
        }
        Send(new NumberAdminAction.EditRegistrationDetails(
            detail.BundleId,
            [.. filing.Details.Select(field => new DetailFieldView(field.Field, field.Value))]));
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private void CheckRequirements() => Send(new NumberAdminAction.CheckRequirements());

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => Send(new NumberAdminAction.StartRegistration());

    /// <summary>"Try again" after a failed lookup.</summary>
    [RelayCommand]
    private void RetryRequirements()
    {
        if (CanRetryRequirements)
        {
            Send(new NumberAdminAction.CheckRequirements());
        }
    }

    /// <summary>Whether <paramref name="upload"/>'s file chooser may open now.</summary>
    internal static bool MayPick(UploadItem upload) => upload.Armed;

    /// <summary>
    /// The file picked for <paramref name="upload"/>: sent to the core, which
    /// puts it on the draft; one that could not be read says so here, and
    /// nothing is sent.
    /// </summary>
    internal void Picked(UploadItem upload, IReadOnlyList<PickedFile> picked)
    {
        // The chooser was open a while: the draft as the core has it now
        // decides, never the button that opened it.
        var current = Filings.FirstOrDefault(filing => filing.Id == upload.BundleId)?.Uploads
            .FirstOrDefault(offered => offered.RequirementName == upload.RequirementName);
        if (current is not { Armed: true } || picked.Count == 0)
        {
            return;
        }
        if (picked[0].Data is not { } data)
        {
            Notice = S.KnowledgeFileUnreadable;
            HasFailure = true;
            HasNotice = false;
            return;
        }
        Send(new NumberAdminAction.UploadRegistrationDocument(upload.BundleId, upload.RequirementName, data));
    }

    /// <summary>"Remove" on a draft's document.</summary>
    internal void Remove(FilingDocumentItem document)
    {
        if (document.Removable && document.Armed)
        {
            Send(new NumberAdminAction.RemoveRegistrationDocument(document.BundleId, document.Id));
        }
    }

    /// <summary>"File it" on a draft: the core asks first.</summary>
    internal void AskFile(FilingItem filing)
    {
        if (filing.CanFile)
        {
            Send(new NumberAdminAction.AskSubmitRegistration(filing.Id));
        }
    }

    private void Send(NumberAdminAction action) => _context?.Send(new UiEvent.Marketplace(new MarketplaceAction.Numbers(action)));
}
