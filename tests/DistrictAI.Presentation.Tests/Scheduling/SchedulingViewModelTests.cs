using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Scheduling;
using Xunit;

namespace DistrictAI.Presentation.Tests.Scheduling;

public sealed class SchedulingViewModelTests
{
    private static SchedulingView View(
        SchedulingState? state = SchedulingState.Live,
        LoadStatus? status = null,
        string? bookingUrl = "https://book.example.com/book/phone-consultation",
        string? problem = null,
        FailureView? notice = null,
        bool offersEnable = false,
        bool enabling = false,
        bool offersCheck = false,
        SchedulingSectionEntryView[]? sections = null) =>
        new(
            Title: "Booking pages",
            Status: status ?? new LoadStatus.Ready(),
            State: state,
            Message: "Your booking page is live.",
            BookingUrl: bookingUrl,
            LastReadyAt: string.Empty,
            Problem: problem,
            Refreshing: false,
            Notice: notice,
            OffersEnable: offersEnable,
            Enabling: enabling,
            OffersCheck: offersCheck,
            SectionsHeading: "Manage",
            Sections: sections ?? []);

    private static (SchedulingViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new SchedulingViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    [Fact]
    public void LiveBookingPagesListTheirSections()
    {
        var (model, sink) = Attached();
        model.Show(View());
        Assert.True(model.Load.Ready);
        Assert.Equal("Your booking page is live.", model.Message);
        Assert.True(model.HasBookingUrl);
        Assert.False(model.HasLastLive);
        Assert.False(model.OffersEnable);
        Assert.False(model.Busy);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void TurningThemOnIsOnePressAtATime()
    {
        var (model, sink) = Attached();
        model.Show(View(SchedulingState.NotSetUp, bookingUrl: null, offersEnable: true));
        Assert.True(model.OffersEnable);
        Assert.True(model.EnableCommand.CanExecute(null));
        model.EnableCommand.Execute(null);
        model.Show(View(SchedulingState.NotSetUp, bookingUrl: null, offersEnable: true, enabling: true));
        Assert.False(model.EnableCommand.CanExecute(null));
        Assert.True(model.Busy);
        Assert.False(model.HasBookingUrl);
        Assert.Equal([new UiEvent.Scheduling(SchedulingAction.Enable)], sink.Sent);
    }

    [Fact]
    public void AFailedSetupSaysWhyAndCanBeCheckedAgain()
    {
        var (model, sink) = Attached();
        model.Show(View(
            SchedulingState.SetupFailed,
            problem: "cloudflare refused the dns record (HTTP 403)",
            notice: new FailureView("Too many tries.", null, true),
            offersCheck: true));
        Assert.True(model.HasProblem);
        Assert.True(model.HasNotice);
        Assert.Equal("Too many tries.", model.Notice);
        Assert.True(model.OffersCheck);
        model.CheckAgainCommand.Execute(null);
        model.DismissNoticeCommand.Execute(null);
        Assert.Equal(
            [
                new UiEvent.Scheduling(SchedulingAction.CheckAgain),
                new UiEvent.Scheduling(SchedulingAction.DismissNotice),
            ],
            sink.Sent);
    }

    [Fact]
    public void AFailedReadIsAStatusPage()
    {
        var (model, _) = Attached();
        model.Show(View(null, new LoadStatus.Failed(new FailureView("Offline.", null, true), "Could not load booking pages")));
        Assert.True(model.Load.Failed);
        Assert.True(model.Load.CanRetry);
        Assert.Equal(string.Empty, model.StateGlyph);
    }

    [Fact]
    public void EachStateHasItsIcon()
    {
        Assert.Equal("\uE733", SchedulingViewModel.GlyphFor(SchedulingState.NotOffered));
        Assert.Equal("\uE787", SchedulingViewModel.GlyphFor(SchedulingState.NotSetUp));
        Assert.Equal("\uE895", SchedulingViewModel.GlyphFor(SchedulingState.Provisioning));
        Assert.Equal("\uE73E", SchedulingViewModel.GlyphFor(SchedulingState.Live));
        Assert.Equal("\uE7BA", SchedulingViewModel.GlyphFor(SchedulingState.SetupFailed));
        Assert.Equal("\uE946", SchedulingViewModel.GlyphFor(SchedulingState.SwitchedOff));
        Assert.Equal("\uE946", SchedulingViewModel.GlyphFor(SchedulingState.Unknown));
        Assert.Equal(string.Empty, SchedulingViewModel.GlyphFor(null));
    }

    [Fact]
    public void LiveBookingPagesListTheirSectionsEachOpeningItsPage()
    {
        var (model, sink) = Attached();
        model.Show(View(sections:
        [
            new SchedulingSectionEntryView(SchedulingAdminSection.Overview, "Overview", "Your booking desk", false),
            new SchedulingSectionEntryView(SchedulingAdminSection.Bookings, "Bookings", "Every booking", false),
        ]));
        Assert.True(model.HasSections);
        Assert.Equal("Manage", model.SectionsHeading);
        Assert.Equal(2, model.Sections.Count);
        Assert.Equal("SchedulingSectionBookings", model.Sections[1].AutomationId);
        Assert.Equal("Bookings. Every booking", model.Sections[1].ToString());
        model.OpenSection(model.Sections[1]);
        Assert.Equal(
            [new UiEvent.SchedulingAdmin(new SchedulingAdminAction.Open(SchedulingAdminSection.Bookings))],
            sink.Sent);
        model.Show(View());
        Assert.False(model.HasSections);
        Assert.Empty(model.Sections);
    }

    [Fact]
    public void NothingIsSentBeforeAttaching()
    {
        var model = new SchedulingViewModel();
        model.Send(SchedulingAction.Open);
        var (context, sink) = Pages.Context();
        model.Attach(context);
        Assert.Empty(sink.Sent);
    }
}
