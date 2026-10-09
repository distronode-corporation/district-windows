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
        bool offersWeb = true,
        bool webEnabled = true,
        bool opening = false) =>
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
            OffersWeb: offersWeb,
            WebEnabled: webEnabled,
            Opening: opening);

    private static (SchedulingViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new SchedulingViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    [Fact]
    public void LiveBookingPagesAreManagedOnTheWeb()
    {
        var (model, sink) = Attached();
        model.Show(View());
        Assert.True(model.Load.Ready);
        Assert.Equal("Your booking page is live.", model.Message);
        Assert.True(model.HasBookingUrl);
        Assert.False(model.HasLastLive);
        Assert.True(model.OffersWeb);
        Assert.False(model.OffersEnable);
        Assert.False(model.Busy);
        Assert.True(model.ManageOnWebCommand.CanExecute(null));
        model.ManageOnWebCommand.Execute(null);
        Assert.Equal([new UiEvent.Scheduling(SchedulingAction.ManageOnWeb)], sink.Sent);
    }

    [Fact]
    public void WhileTheLinkIsAskedForTheButtonWaits()
    {
        var (model, _) = Attached();
        model.Show(View(webEnabled: true, opening: true));
        Assert.True(model.Busy);
        Assert.True(model.ManageOnWebCommand.CanExecute(null));
        model.Show(View(webEnabled: false, opening: true));
        Assert.False(model.ManageOnWebCommand.CanExecute(null));
    }

    [Fact]
    public void TurningThemOnIsOnePressAtATime()
    {
        var (model, sink) = Attached();
        model.Show(View(SchedulingState.NotSetUp, bookingUrl: null, offersEnable: true, offersWeb: false));
        Assert.True(model.OffersEnable);
        Assert.True(model.EnableCommand.CanExecute(null));
        model.EnableCommand.Execute(null);
        model.Show(View(SchedulingState.NotSetUp, bookingUrl: null, offersEnable: true, enabling: true, offersWeb: false));
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
            offersCheck: true,
            offersWeb: false));
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
        model.Show(View(null, new LoadStatus.Failed(new FailureView("Offline.", null, true), "Could not load booking pages"), offersWeb: false));
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
    public void NothingIsSentBeforeAttaching()
    {
        var model = new SchedulingViewModel();
        model.Send(SchedulingAction.Open);
        var (context, sink) = Pages.Context();
        model.Attach(context);
        Assert.Empty(sink.Sent);
    }
}
