using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Calls;

/// <summary>
/// How the dialler formats an edit at once and reads its country list: the
/// core's own rules, through district-ffi. A seam, so the view model is tested
/// without the native library.
/// </summary>
public interface IDialRules
{
    /// <summary>The box went from <paramref name="previous"/> to <paramref name="next"/>, formatted as the core will format it.</summary>
    DialEntryFormat Format(string previous, string next, uint caret, bool forward, string? country);

    /// <summary>The box after <paramref name="iso"/> is picked, as the core will make it.</summary>
    DialEntryFormat Pick(string shown, string? country, string iso);

    /// <summary>The country list for <paramref name="query"/>.</summary>
    DialCountryList Countries(string query, string? home, string? current);
}

/// <summary>The core's rules, through district-ffi.</summary>
public sealed class FfiDialRules : IDialRules
{
    /// <inheritdoc/>
    public DialEntryFormat Format(string previous, string next, uint caret, bool forward, string? country) =>
        DistrictFfi.DialEntryFormat(previous, next, caret, forward, country);

    /// <inheritdoc/>
    public DialEntryFormat Pick(string shown, string? country, string iso) =>
        DistrictFfi.DialPickFormat(shown, country, iso);

    /// <inheritdoc/>
    public DialCountryList Countries(string query, string? home, string? current) =>
        DistrictFfi.DialCountryList(query, home, current);
}

/// <summary>A line of the country list: a group's heading, or a country.</summary>
/// <param name="Iso">The country (ISO 3166), or empty for a heading.</param>
/// <param name="Name">The country's name, or the heading.</param>
/// <param name="CallingCode"><c>+1</c>, or empty for a heading.</param>
/// <param name="Selected">Whether it is the country on the button.</param>
public sealed record CountryRowItem(string Iso, string Name, string CallingCode, bool Selected)
{
    /// <summary>Whether this line is a heading rather than a country.</summary>
    public bool IsHeading => Iso.Length == 0;
}

/// <summary>
/// The dialler's state, copied from the core's <see cref="DialerView"/>
/// (dialler design A): the country button and its list, the box in the local
/// form of that country, the line naming where the call rings, and Call.
/// </summary>
/// <remarks>
/// The box is formatted at once with the core's own rules
/// (<see cref="IDialRules"/>) and the same edit is sent to the core, which
/// answers with the same text: events reach the core on a later turn, so a box
/// written only from the core would lose a key typed in between. The button's
/// country is kept here for the same reason until the core's catches up.
/// Call works only when the core says a call can be placed.
/// </remarks>
public sealed partial class DialerViewModel : ObservableObject
{
    private readonly TextEcho _echo = new();
    private readonly IDialRules _rules;
    private PageContext? _context;
    private bool _writing;
    private string _shown = string.Empty;
    private string? _country;
    private string? _home;

    /// <summary>A dialler on the core's rules.</summary>
    public DialerViewModel()
        : this(new FfiDialRules())
    {
    }

    /// <summary>A dialler on <paramref name="rules"/>.</summary>
    public DialerViewModel(IDialRules rules)
    {
        _rules = rules;
    }

    /// <summary>The box was written: where its cursor goes, in characters, for the page to set.</summary>
    public event EventHandler<int>? CaretMoved;

    /// <summary>Where the page's box has its cursor now, in characters.</summary>
    public Func<int>? CaretSource { get; set; }

    /// <summary>Whether the next single-character delete is Delete (forward) rather than Backspace.</summary>
    public bool NextDeleteForward { get; set; }

    /// <summary>The number in the box, in the local form of the button's country.</summary>
    [ObservableProperty]
    public partial string Number { get; set; } = string.Empty;

    /// <summary>The button's text: the country's code and calling code (<c>CA +1</c>), or "Country".</summary>
    [ObservableProperty]
    public partial string CountryButton { get; set; } = string.Empty;

    /// <summary>The button's accessible name: "Canada, calling code +1".</summary>
    [ObservableProperty]
    public partial string CountryLabel { get; set; } = string.Empty;

    /// <summary>The box's placeholder: an example local number for the country.</summary>
    [ObservableProperty]
    public partial string Placeholder { get; set; } = string.Empty;

    /// <summary>"Rings Canada · +1 416 555 0199", or a no-break space, so the line keeps its height.</summary>
    [ObservableProperty]
    public partial string Destination { get; set; } = "\u00A0";

    /// <summary>Why Call is off, once there is something to correct, or empty.</summary>
    [ObservableProperty]
    public partial string Problem { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Problem"/>. The hint shows otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHint))]
    public partial bool HasProblem { get; set; }

    /// <summary>Whether the hint shows: when there is no problem to say.</summary>
    public bool ShowHint => !HasProblem;

    /// <summary>Whether Call works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DialCommand))]
    public partial bool CanDial { get; set; }

    /// <summary>Whether the call is being placed.</summary>
    [ObservableProperty]
    public partial bool Dialing { get; set; }

    /// <summary>Why the last dial failed, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>The line that says how to type a number in the country.</summary>
    [ObservableProperty]
    public partial string Hint { get; set; } = string.Empty;

    /// <summary>"Placing a call turns on your microphone."</summary>
    [ObservableProperty]
    public partial string MicrophoneNote { get; set; } = string.Empty;

    /// <summary>Why Call is off while a call or meeting is in the way, or empty.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Note"/>.</summary>
    [ObservableProperty]
    public partial bool HasNote { get; set; }

    /// <summary>The country list's title, "Country".</summary>
    [ObservableProperty]
    public partial string CountriesTitle { get; set; } = string.Empty;

    /// <summary>The country search box's placeholder.</summary>
    [ObservableProperty]
    public partial string CountrySearchPlaceholder { get; set; } = string.Empty;

    /// <summary>"No country matches." when a search found nothing, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoCountryMatch))]
    public partial string NoCountryMatch { get; set; } = string.Empty;

    /// <summary>Whether a search found nothing.</summary>
    public bool HasNoCountryMatch => NoCountryMatch.Length > 0;

    /// <summary>The country list: headings and countries.</summary>
    public ObservableCollection<CountryRowItem> Countries { get; } = [];

    internal void Attach(PageContext context) => _context = context;

    internal void Show(DialerView view)
    {
        var rewrite = _echo.Write(view.Number, Number);
        // Every edit sent has come back: the core's country is the one, the
        // workspace's own country moving an untouched button included.
        if (rewrite || _echo.Settled)
        {
            _country = view.Country;
            CountryButton = view.CountryButton;
        }
        _home = view.Home;
        if (rewrite)
        {
            WriteBox(view.Number, view.Number.Length);
        }
        CountryLabel = view.CountryLabel;
        Placeholder = view.Placeholder;
        Destination = view.Destination ?? "\u00A0";
        Problem = view.Problem ?? string.Empty;
        HasProblem = view.Problem is not null;
        Hint = view.Hint;
        MicrophoneNote = view.MicrophoneNote;
        CanDial = view.CanDial;
        Dialing = view.Dialing;
        Failure = Display.Failure(view.Failure);
        HasFailure = view.Failure is not null;
        Note = view.Note ?? string.Empty;
        HasNote = !string.IsNullOrEmpty(view.Note);
    }

    partial void OnNumberChanged(string value)
    {
        if (_writing)
        {
            return;
        }
        var caret = Math.Max(CaretSource?.Invoke() ?? value.Length, 0);
        var forward = NextDeleteForward;
        NextDeleteForward = false;
        var format = _rules.Format(_shown, value, (uint)caret, forward, _country);
        _country = format.Country;
        CountryButton = format.CountryButton;
        WriteBox(format.Shown, (int)format.Caret);
        _echo.Typed(format.Shown);
        _context?.Send(new UiEvent.DialerEdit(format.Cleaned));
    }

    /// <summary>The box lost the keyboard to something other than the country list: a wrong length is said from now until the next edit.</summary>
    public void CheckLength() => _context?.Send(new UiEvent.DialerCheckLength());

    /// <summary>The country list opened, or its search changed to <paramref name="query"/>.</summary>
    public void SearchCountries(string query)
    {
        var list = _rules.Countries(query, _home, _country);
        CountriesTitle = list.Title;
        CountrySearchPlaceholder = list.SearchPlaceholder;
        NoCountryMatch = list.NoMatch ?? string.Empty;
        Countries.Clear();
        if (list.Suggested.Length > 0)
        {
            Countries.Add(Heading(list.SuggestedTitle));
            foreach (var row in list.Suggested)
            {
                Countries.Add(Row(row));
            }
        }
        if (list.CountriesTitle.Length > 0)
        {
            Countries.Add(Heading(list.CountriesTitle));
        }
        foreach (var row in list.Countries)
        {
            Countries.Add(Row(row));
        }
    }

    /// <summary>Enter in the search: the first country the list shows.</summary>
    public void PickFirstCountry()
    {
        var first = Countries.FirstOrDefault(row => !row.IsHeading);
        if (first is not null)
        {
            PickCountry(first.Iso);
        }
    }

    /// <summary>A country was picked: the digits typed are read again there, here at once and in the core.</summary>
    public void PickCountry(string iso)
    {
        var format = _rules.Pick(_shown, _country, iso);
        _country = format.Country;
        CountryButton = format.CountryButton;
        WriteBox(format.Shown, (int)format.Caret);
        _echo.Typed(format.Shown);
        _context?.Send(new UiEvent.DialerPickCountry(iso));
    }

    private static CountryRowItem Heading(string text) => new(string.Empty, text, string.Empty, false);

    private static CountryRowItem Row(DialCountryRow row) => new(row.Iso, row.Name, row.CallingCode, row.Selected);

    private void WriteBox(string text, int caret)
    {
        _shown = text;
        _writing = true;
        try
        {
            Number = text;
        }
        finally
        {
            _writing = false;
        }
        CaretMoved?.Invoke(this, caret);
    }

    [RelayCommand(CanExecute = nameof(CanDial))]
    private void Dial() => _context?.Send(new UiEvent.Dial());
}
