using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;
using Xunit;

namespace DistrictAI.Presentation.Tests.Calls;

/// <summary>The core's dialler rules, scripted: each edit shows as the text typed plus a mark, under a country the test names.</summary>
internal sealed class FakeDialRules : IDialRules
{
    public List<(string Previous, string Edited, uint Caret, bool Forward, string? Country)> Formats { get; } = [];

    public string? NextCountry { get; set; } = "CA";

    public DialEntryFormat Format(string previous, string edited, uint caret, bool forward, string? country)
    {
        Formats.Add((previous, edited, caret, forward, country));
        var shown = $"({edited})";
        return new DialEntryFormat(Cleaned: edited, Shown: shown, Country: NextCountry, CountryButton: $"{NextCountry} +1", Caret: (uint)shown.Length);
    }

    public DialEntryFormat Pick(string shown, string? country, string iso) =>
        new(Cleaned: "digits", Shown: $"{iso}:{shown}", Country: iso, CountryButton: $"{iso} +44", Caret: 3);

    public DialCountryList Countries(string query, string? home, string? current) =>
        query.Length == 0
            ? new DialCountryList(
                Suggested: [new DialCountryRow("CA", "Canada", "+1", current == "CA")],
                Countries: [new DialCountryRow("AF", "Afghanistan", "+93", false), new DialCountryRow("CA", "Canada", "+1", current == "CA")],
                SuggestedTitle: "Suggested",
                CountriesTitle: "All countries",
                NoMatch: null,
                SearchPlaceholder: "Country name or code",
                Title: "Country")
            : new DialCountryList([], [], "Suggested", string.Empty, query == "zzz" ? "No country matches." : null, "Country name or code", "Country");
}

public sealed class DialerViewModelTests
{
    private static DialerView View(
        string number = "",
        string? country = null,
        bool canDial = false,
        FailureView? failure = null,
        string? note = null,
        string? destination = null,
        string? problem = null,
        string? home = null) =>
        new(
            number,
            country,
            CountryButton: country is null ? "Country" : $"{country} +1",
            CountryLabel: country is null ? "Country" : "Canada, calling code +1",
            home,
            Placeholder: "(416) 555-0199",
            destination,
            problem,
            canDial,
            Dialing: false,
            failure,
            note,
            Hint: "Type the number as you would dial it locally (Canada), or start with + and its country code.",
            MicrophoneNote: "Placing a call turns on your microphone.");

    [Fact]
    public void ItCopiesTheDialler()
    {
        var dialer = new DialerViewModel(new FakeDialRules());
        Assert.Equal("\u00A0", dialer.Destination);

        dialer.Show(View(
            "(416) 555-0199",
            "CA",
            canDial: true,
            failure: V.Failure("Could not place it."),
            note: "Finish the call first.",
            destination: "Rings Canada \u00B7 +1 416 555 0199"));
        Assert.Equal("(416) 555-0199", dialer.Number);
        Assert.Equal("CA +1", dialer.CountryButton);
        Assert.Equal("Canada, calling code +1", dialer.CountryLabel);
        Assert.Equal("(416) 555-0199", dialer.Placeholder);
        Assert.Equal("Rings Canada \u00B7 +1 416 555 0199", dialer.Destination);
        Assert.StartsWith("Type the number", dialer.Hint);
        Assert.True(dialer.ShowHint);
        Assert.False(dialer.HasProblem);
        Assert.Equal("Placing a call turns on your microphone.", dialer.MicrophoneNote);
        Assert.True(dialer.CanDial);
        Assert.True(dialer.DialCommand.CanExecute(null));
        Assert.False(dialer.Dialing);
        Assert.Equal("Could not place it.", dialer.Failure);
        Assert.True(dialer.HasFailure);
        Assert.Equal("Finish the call first.", dialer.Note);
        Assert.True(dialer.HasNote);

        dialer.Show(View(note: string.Empty, problem: "That is the wrong number of digits."));
        Assert.Equal("\u00A0", dialer.Destination);
        Assert.Equal("That is the wrong number of digits.", dialer.Problem);
        Assert.True(dialer.HasProblem);
        Assert.False(dialer.ShowHint);
        Assert.Equal("Country", dialer.CountryButton);
        Assert.False(dialer.DialCommand.CanExecute(null));
        Assert.False(dialer.HasFailure);
        Assert.Equal(string.Empty, dialer.Note);
        Assert.False(dialer.HasNote);
    }

    [Fact]
    public void AnEditIsFormattedAtOnceAndTheCleanedNumberSent()
    {
        var (context, sink) = Pages.Context();
        var rules = new FakeDialRules();
        var dialer = new DialerViewModel(rules);
        dialer.Attach(context);
        var carets = new List<int>();
        dialer.CaretMoved += (_, caret) => carets.Add(caret);
        dialer.CaretSource = () => 1;

        dialer.Number = "4";
        Assert.Equal("(4)", dialer.Number);
        Assert.Equal("CA +1", dialer.CountryButton);
        dialer.NextDeleteForward = true;
        dialer.Number = "(4)5";
        Assert.Equal("((4)5)", dialer.Number);
        Assert.False(dialer.NextDeleteForward, "a delete's direction counts once");
        Assert.Equal([3, 6], carets);
        Assert.Equal([("", "4", 1u, false, (string?)null), ("(4)", "(4)5", 1u, true, "CA")], rules.Formats);

        // The core's answer to the first edit, after the second was formatted.
        dialer.Show(View("(4)", "US"));
        Assert.Equal("((4)5)", dialer.Number);
        // The box's country until the core catches up.
        Assert.Equal("CA +1", dialer.CountryButton);
        // Caught up: the core's country.
        dialer.Show(View("((4)5)", "US"));
        Assert.Equal("US +1", dialer.CountryButton);

        // A number the box did not send (a call back) is written, and not sent back.
        dialer.Show(View("(212) 555-0100", "US"));
        Assert.Equal("(212) 555-0100", dialer.Number);

        Assert.Equal([new UiEvent.DialerEdit("4"), new UiEvent.DialerEdit("(4)5")], sink.Sent);
    }

    [Fact]
    public void TheCountryListSuggestsThenListsAndAPickIsSent()
    {
        var (context, sink) = Pages.Context();
        var dialer = new DialerViewModel(new FakeDialRules());
        dialer.Attach(context);
        dialer.Show(View(country: "CA", home: "CA"));

        dialer.SearchCountries(string.Empty);
        Assert.Equal("Country", dialer.CountriesTitle);
        Assert.Equal("Country name or code", dialer.CountrySearchPlaceholder);
        Assert.False(dialer.HasNoCountryMatch);
        Assert.Equal(
            ["Suggested", "Canada", "All countries", "Afghanistan", "Canada"],
            dialer.Countries.Select(row => row.Name));
        Assert.True(dialer.Countries[0].IsHeading);
        Assert.True(dialer.Countries[1].Selected);

        dialer.SearchCountries("zzz");
        Assert.Empty(dialer.Countries);
        Assert.Equal("No country matches.", dialer.NoCountryMatch);
        Assert.True(dialer.HasNoCountryMatch);
        dialer.PickFirstCountry();
        Assert.Empty(sink.Sent);

        dialer.SearchCountries(string.Empty);
        dialer.PickFirstCountry();
        Assert.Equal("CA:", dialer.Number);
        Assert.Equal("CA +44", dialer.CountryButton);
        dialer.PickCountry("GB");
        Assert.Equal("GB:CA:", dialer.Number);
        dialer.CheckLength();
        Assert.Equal(
            [new UiEvent.DialerPickCountry("CA"), new UiEvent.DialerPickCountry("GB"), new UiEvent.DialerCheckLength()],
            sink.Sent);
    }

    [Fact]
    public void CallSendsTheDial()
    {
        var dialer = new DialerViewModel(new FakeDialRules());
        dialer.Number = "5";
        dialer.DialCommand.Execute(null);
        dialer.CheckLength();

        var (context, sink) = Pages.Context();
        dialer.Attach(context);
        dialer.DialCommand.Execute(null);
        Assert.Equal([new UiEvent.Dial()], sink.Sent);
    }
}

public sealed class IncomingCallViewModelTests
{
    private static IncomingRingView Ring(string id = "call-1", string? detail = "From +1 212 555 0100", string? note = "Answering puts your microphone on this call.", bool live = true) =>
        new(id, "Incoming call", detail, note, ShowAnswer: live, CanAnswer: live, CanDecline: live, Answering: false, live, Sounding: live, WorkspaceName: null);

    [Fact]
    public void ARingShowsAndIsAnnouncedOnce()
    {
        var banner = new IncomingCallViewModel();
        Assert.True(banner.Show(Ring()));

        Assert.True(banner.IsShown);
        Assert.Equal("call-1", banner.CallId);
        Assert.Equal("Incoming call", banner.Heading);
        Assert.Equal("From +1 212 555 0100", banner.Detail);
        Assert.True(banner.HasDetail);
        Assert.Equal("Answering puts your microphone on this call.", banner.Note);
        Assert.True(banner.HasNote);
        Assert.True(banner.ShowAnswer);
        Assert.True(banner.CanAnswer);
        Assert.True(banner.AnswerCommand.CanExecute(null));
        Assert.True(banner.Live);
        Assert.True(banner.CanDecline);
        Assert.True(banner.DeclineCommand.CanExecute(null));
        Assert.False(banner.Answering);
        Assert.False(banner.CanDismiss);
        Assert.True(banner.Sounding);
        Assert.Equal("Incoming call. From +1 212 555 0100", banner.Announcement);

        // The same ring again is not a new one; another call is.
        Assert.False(banner.Show(Ring()));
        Assert.True(banner.Show(Ring("call-2")));
    }

    [Fact]
    public void AnEndedRingWithNothingUnderIt()
    {
        var banner = new IncomingCallViewModel();
        banner.Show(Ring(detail: null, note: null, live: false));
        Assert.Equal(string.Empty, banner.Detail);
        Assert.False(banner.HasDetail);
        Assert.Equal(string.Empty, banner.Note);
        Assert.False(banner.HasNote);
        Assert.True(banner.CanDismiss);
        Assert.Equal("Incoming call", banner.Announcement);
    }

    [Fact]
    public void NoRingHidesTheBannerAndTheNextRingIsNew()
    {
        var banner = new IncomingCallViewModel();
        banner.Show(Ring());
        Assert.False(banner.Show(null));
        Assert.False(banner.IsShown);
        Assert.Equal(string.Empty, banner.CallId);
        Assert.False(banner.AnswerCommand.CanExecute(null));
        Assert.False(banner.DeclineCommand.CanExecute(null));

        Assert.True(banner.Show(Ring()));
    }

    [Fact]
    public void AnswerAndDeclineNameTheCall()
    {
        var banner = new IncomingCallViewModel();
        banner.Show(Ring());
        banner.AnswerCommand.Execute(null);
        banner.DeclineCommand.Execute(null);
        banner.DismissCommand.Execute(null);

        var (context, sink) = Pages.Context();
        banner.Attach(context);
        banner.AnswerCommand.Execute(null);
        banner.DeclineCommand.Execute(null);
        banner.DismissCommand.Execute(null);

        Assert.Equal([new UiEvent.Answer("call-1"), new UiEvent.Decline("call-1"), new UiEvent.DismissRing()], sink.Sent);
    }
}

public sealed class TextEchoTests
{
    [Fact]
    public void ValuesTheBoxSentAreOnlyAcknowledged()
    {
        var echo = new TextEcho();
        echo.Typed("5");
        echo.Typed("55");
        Assert.False(echo.Write("5", "55"));
        Assert.False(echo.Write("55", "55"));
    }

    [Fact]
    public void TheSameCoreValueAgainWhileTypingIsNothingNew()
    {
        var echo = new TextEcho();
        echo.Typed("5");
        Assert.False(echo.Write("5", "5"));
        echo.Typed("55");
        Assert.False(echo.Write("5", "55"));
    }

    [Fact]
    public void AValueTheBoxDidNotSendIsWrittenUnlessTheBoxHoldsIt()
    {
        var echo = new TextEcho();
        Assert.True(echo.Write("+12125550100", string.Empty));
        Assert.False(echo.Write("+12125550100", "+12125550100"));

        // The number cleared after a call, with typing pending.
        echo.Typed("+12125550101");
        Assert.True(echo.Write(string.Empty, "+12125550101"));
    }
}
