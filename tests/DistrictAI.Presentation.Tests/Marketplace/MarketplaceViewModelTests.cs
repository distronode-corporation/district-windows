using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Marketplace;
using Xunit;

namespace DistrictAI.Presentation.Tests.Marketplace;

public sealed class MarketplaceViewModelTests
{
    private static readonly NumberTypeView[] _types =
        [new("local", "Local"), new("tollFree", "Toll-free"), new("mobile", "Mobile")];

    private static NumberFormView Form(string area = "", string country = "US", string type = "local") =>
        new(area, country, type, _types);

    private static MarketplaceView View(
        NumbersTab tab = NumbersTab.Owned,
        bool offersWeb = true,
        NumberRowView[]? owned = null,
        string? partial = null,
        NumberFormView? form = null,
        NumberSearchView? search = null) =>
        new(
            Title: "Phone numbers",
            Tab: tab,
            ReadOnlyNote: "Numbers cannot be bought, released or changed in this app, so this screen is read only.",
            OffersWeb: offersWeb,
            WebAction: "Open the number marketplace on the web",
            WebCaption: "Numbers are bought on the District AI website. This opens it in your browser.",
            OwnedStatus: new LoadStatus.Ready(),
            OwnedEmpty: null,
            Owned: owned ?? [new NumberRowView("+1 416 555 0100", "Main line", "1.15 a month")],
            PartialNote: partial,
            Refreshing: false,
            Form: form ?? Form(),
            Search: search ?? new NumberSearchView.Idle());

    private static (MarketplaceViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new MarketplaceViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    [Fact]
    public void TheHeldNumbersShowReadOnly()
    {
        var (model, sink) = Attached();
        model.Show(View(partial: "Could not reach telnyx, so this list may be missing numbers."));
        Assert.True(model.OnOwned);
        Assert.False(model.OnSearch);
        Assert.Single(model.Owned);
        Assert.Equal("+1 416 555 0100, Main line, 1.15 a month", model.Owned[0].AccessibleName);
        Assert.Equal(model.Owned[0].AccessibleName, model.Owned[0].ToString());
        Assert.True(model.Owned[0].HasMonthly);
        Assert.True(model.HasPartialNote);
        Assert.True(model.OpenWebCommand.CanExecute(null));
        Assert.Equal(["Local", "Toll-free", "Mobile"], model.TypeLabels);
        Assert.Equal(0, model.TypeIndex);
        Assert.Empty(sink.Sent);

        model.OpenWebCommand.Execute(null);
        Assert.Equal([new UiEvent.Marketplace(new MarketplaceAction.OpenWeb())], sink.Sent);
    }

    [Fact]
    public void AViewerIsNotOfferedTheWeb()
    {
        var (model, _) = Attached();
        model.Show(View(offersWeb: false));
        Assert.False(model.OffersWeb);
        Assert.False(model.OpenWebCommand.CanExecute(null));
    }

    [Fact]
    public void ChoosingATabAndTypingSendTheCoreTheForm()
    {
        var (model, sink) = Attached();
        model.Show(View());
        model.TabIndex = 1;
        model.Show(View(NumbersTab.Search));
        Assert.True(model.OnSearch);
        Assert.True(model.SearchIdle);

        model.AreaCode = "416";
        model.Country = "CA";
        model.TypeIndex = 1;
        // The core's older value while the person typed on: the box keeps theirs.
        model.Show(View(NumbersTab.Search, form: Form("416", "US")));
        Assert.Equal("CA", model.Country);
        model.SearchCommand.Execute(null);
        model.TabIndex = 7;

        Assert.Equal(
            [
                new UiEvent.Marketplace(new MarketplaceAction.SelectTab(NumbersTab.Search)),
                new UiEvent.Marketplace(new MarketplaceAction.EditSearch("416", "US", "local")),
                new UiEvent.Marketplace(new MarketplaceAction.EditSearch("416", "CA", "local")),
                new UiEvent.Marketplace(new MarketplaceAction.EditSearch("416", "CA", "tollFree")),
                new UiEvent.Marketplace(new MarketplaceAction.Search()),
            ],
            sink.Sent);
    }

    [Fact]
    public void AFormTheCoreChangedIsWrittenIntoTheBoxes()
    {
        var (model, sink) = Attached();
        model.Show(View(NumbersTab.Search, form: Form("212", "US", "unknownType")));
        Assert.Equal("212", model.AreaCode);
        Assert.Equal(-1, model.TypeIndex);
        model.Country = "GB";
        Assert.Equal(new UiEvent.Marketplace(new MarketplaceAction.EditSearch("212", "GB", string.Empty)), sink.Sent.Single());
    }

    [Fact]
    public void TheSearchSaysWhereItStands()
    {
        var (model, _) = Attached();
        model.Show(View(NumbersTab.Search, search: new NumberSearchView.Searching()));
        Assert.True(model.Searching);
        Assert.False(model.SearchIdle);

        model.Show(View(NumbersTab.Search, search: new NumberSearchView.Found(
            "Offered by Twilio",
            [new NumberRowView("+1 800 555 0122", "Toll-free", null)])));
        Assert.True(model.HasFound);
        Assert.Equal("Offered by Twilio", model.ProviderLine);
        Assert.False(model.Found[0].HasMonthly);

        model.Show(View(NumbersTab.Search, search: new NumberSearchView.Status("No carrier connected", "Connect one.", false)));
        Assert.False(model.HasFound);
        Assert.Empty(model.Found);
        Assert.True(model.HasSearchStatus);
        Assert.Equal("No carrier connected", model.SearchStatusTitle);
        Assert.False(model.CanRetrySearch);

        model.Show(View(NumbersTab.Search, search: new NumberSearchView.Status("Could not search for numbers", "Busy.", true)));
        Assert.True(model.CanRetrySearch);
    }

    [Fact]
    public void NothingIsSentBeforeAttaching()
    {
        var model = new MarketplaceViewModel();
        model.AreaCode = "416";
        model.Send(new MarketplaceAction.Open());
        var (context, sink) = Pages.Context();
        model.Attach(context);
        Assert.Empty(sink.Sent);
    }
}
