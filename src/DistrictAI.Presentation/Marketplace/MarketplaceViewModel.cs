using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Marketplace;

/// <summary>
/// The workspace's phone numbers and a search of the numbers for sale, read
/// only, copied from the core's <see cref="MarketplaceView"/>. Buying is on the
/// web: "Open the number marketplace on the web" asks the core, which opens it
/// in the browser for a role that could buy there.
/// </summary>
public sealed partial class MarketplaceViewModel : ObservableObject
{
    private readonly TextEcho _areaEcho = new();
    private readonly TextEcho _countryEcho = new();
    private PageContext? _context;
    private bool _writing;
    private string[] _typeValues = [];

    /// <summary>Loading, failure, the empty list and refresh, for the held numbers.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The numbers the workspace holds.</summary>
    public ObservableCollection<NumberRowItem> Owned { get; } = [];

    /// <summary>The numbers for sale the search found.</summary>
    public ObservableCollection<NumberRowItem> Found { get; } = [];

    /// <summary>The number types, by their labels.</summary>
    public ObservableCollection<string> TypeLabels { get; } = [];

    /// <summary>Which tab shows: 0 the held numbers, 1 the search.</summary>
    [ObservableProperty]
    public partial int TabIndex { get; set; }

    /// <summary>Whether the held numbers show.</summary>
    [ObservableProperty]
    public partial bool OnOwned { get; set; } = true;

    /// <summary>Whether the search shows.</summary>
    [ObservableProperty]
    public partial bool OnSearch { get; set; }

    /// <summary>The note saying the screen changes nothing, worded for the role.</summary>
    [ObservableProperty]
    public partial string ReadOnlyNote { get; set; } = string.Empty;

    /// <summary>Whether the web marketplace is offered.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenWebCommand))]
    public partial bool OffersWeb { get; set; }

    /// <summary>The web marketplace link's words.</summary>
    [ObservableProperty]
    public partial string WebAction { get; set; } = string.Empty;

    /// <summary>The caption under it.</summary>
    [ObservableProperty]
    public partial string WebCaption { get; set; } = string.Empty;

    /// <summary>A note that a carrier did not answer, or empty.</summary>
    [ObservableProperty]
    public partial string PartialNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="PartialNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasPartialNote { get; set; }

    /// <summary>The area code, as typed.</summary>
    [ObservableProperty]
    public partial string AreaCode { get; set; } = string.Empty;

    /// <summary>The country code, as typed.</summary>
    [ObservableProperty]
    public partial string Country { get; set; } = string.Empty;

    /// <summary>Which of <see cref="TypeLabels"/> is chosen.</summary>
    [ObservableProperty]
    public partial int TypeIndex { get; set; } = -1;

    /// <summary>Whether a search is waiting or on its way.</summary>
    [ObservableProperty]
    public partial bool Searching { get; set; }

    /// <summary>Whether nothing has been searched yet.</summary>
    [ObservableProperty]
    public partial bool SearchIdle { get; set; } = true;

    /// <summary>Whether the search found numbers.</summary>
    [ObservableProperty]
    public partial bool HasFound { get; set; }

    /// <summary>"Offered by" the carrier, over the numbers found.</summary>
    [ObservableProperty]
    public partial string ProviderLine { get; set; } = string.Empty;

    /// <summary>Whether the search ended in a status: none found, no carrier, or a failure.</summary>
    [ObservableProperty]
    public partial bool HasSearchStatus { get; set; }

    /// <summary>The status's heading.</summary>
    [ObservableProperty]
    public partial string SearchStatusTitle { get; set; } = string.Empty;

    /// <summary>The status's text.</summary>
    [ObservableProperty]
    public partial string SearchStatusBody { get; set; } = string.Empty;

    /// <summary>Whether "Try again" is offered for the search.</summary>
    [ObservableProperty]
    public partial bool CanRetrySearch { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
    }

    internal void Show(MarketplaceView view)
    {
        Load.Show(view.OwnedStatus, view.Owned.Length > 0, view.OwnedEmpty, view.Refreshing, null);
        Display.Sync(Owned, [.. view.Owned.Select(NumberRowItem.From)]);
        _writing = true;
        try
        {
            TabIndex = view.Tab == NumbersTab.Search ? 1 : 0;
            ShowForm(view.Form);
        }
        finally
        {
            _writing = false;
        }
        OnOwned = view.Tab != NumbersTab.Search;
        OnSearch = view.Tab == NumbersTab.Search;
        ReadOnlyNote = view.ReadOnlyNote;
        OffersWeb = view.OffersWeb;
        WebAction = view.WebAction;
        WebCaption = view.WebCaption;
        PartialNote = view.PartialNote ?? string.Empty;
        HasPartialNote = view.PartialNote is not null;
        ShowSearch(view.Search);
    }

    private void ShowForm(NumberFormView form)
    {
        _typeValues = [.. form.NumberTypes.Select(type => type.Value)];
        string[] labels = [.. form.NumberTypes.Select(type => type.Label)];
        Display.Sync(TypeLabels, labels);
        TypeIndex = Array.IndexOf(_typeValues, form.NumberType);
        if (_areaEcho.Write(form.AreaCode, AreaCode))
        {
            AreaCode = form.AreaCode;
        }
        if (_countryEcho.Write(form.Country, Country))
        {
            Country = form.Country;
        }
    }

    private void ShowSearch(NumberSearchView search)
    {
        Searching = search is NumberSearchView.Searching;
        SearchIdle = search is NumberSearchView.Idle;
        var found = search as NumberSearchView.Found;
        HasFound = found is not null;
        ProviderLine = found?.ProviderLine ?? string.Empty;
        Display.Sync(Found, found is null ? [] : [.. found.Numbers.Select(NumberRowItem.From)]);
        var status = search as NumberSearchView.Status;
        HasSearchStatus = status is not null;
        SearchStatusTitle = status?.Title ?? string.Empty;
        SearchStatusBody = status?.Body ?? string.Empty;
        CanRetrySearch = status?.Retry ?? false;
    }

    partial void OnTabIndexChanged(int value)
    {
        if (!_writing && value is 0 or 1)
        {
            Send(new MarketplaceAction.SelectTab(value == 1 ? NumbersTab.Search : NumbersTab.Owned));
        }
    }

    partial void OnAreaCodeChanged(string value)
    {
        if (!_writing)
        {
            _areaEcho.Typed(value);
            Edited();
        }
    }

    partial void OnCountryChanged(string value)
    {
        if (!_writing)
        {
            _countryEcho.Typed(value);
            Edited();
        }
    }

    partial void OnTypeIndexChanged(int value)
    {
        if (!_writing)
        {
            Edited();
        }
    }

    /// <summary>Sends the filters as they now read; the core searches once the typing stops.</summary>
    private void Edited()
    {
        var type = TypeIndex >= 0 && TypeIndex < _typeValues.Length ? _typeValues[TypeIndex] : string.Empty;
        Send(new MarketplaceAction.EditSearch(AreaCode, Country, type));
    }

    [RelayCommand]
    private void Search() => Send(new MarketplaceAction.Search());

    [RelayCommand(CanExecute = nameof(OffersWeb))]
    private void OpenWeb() => Send(new MarketplaceAction.OpenWeb());

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(MarketplaceAction action) => _context?.Send(new UiEvent.Marketplace(action));
}

/// <summary>A phone number in a list.</summary>
/// <param name="Number">The number, grouped to read.</param>
/// <param name="Line">What it is, on one line.</param>
/// <param name="Monthly">What the carrier charges a month, or empty when it quoted nothing.</param>
public sealed record NumberRowItem(string Number, string Line, string Monthly)
{
    /// <summary>Whether there is a <see cref="Monthly"/> charge to show.</summary>
    public bool HasMonthly => Monthly.Length > 0;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => string.Join(", ", new[] { Number, Line, Monthly }.Where(part => part.Length > 0));

    internal static NumberRowItem From(NumberRowView row) => new(row.Number, row.Line, row.Monthly ?? string.Empty);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}
