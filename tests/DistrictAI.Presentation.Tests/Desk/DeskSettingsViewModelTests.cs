using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Desk;
using Xunit;

namespace DistrictAI.Presentation.Tests.Desk;

public sealed class DeskSettingsViewModelTests
{
    private const string TypeRefused = "The logo must be a PNG, JPEG or WebP image.";
    private const string SizeRefused = "The logo must be 5 MB or smaller.";
    private const string EmptyRefused = "This file is empty.";
    private const int LogoLimit = 5 * 1024 * 1024;

    private static readonly byte[] _png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] _gif = "GIF89a"u8.ToArray();

    private static readonly DeskSettingsView _loading = new(
        Title: "Help desk settings",
        Intro: "Leave the name blank to show customers the workspace's own name.",
        Status: V.Loading,
        EnabledLabel: "Take tickets",
        EnabledNote: "While the desk is off, no tickets are recorded.",
        Enabled: false,
        NotifyLabel: "Email customers your replies",
        NotifyCustomersByEmail: false,
        BrandLabel: "Name customers see",
        BrandName: string.Empty,
        CanEdit: false,
        CanSave: false,
        Saving: false,
        SaveFailure: null,
        LogoHelp: "A PNG, JPEG or WebP image.",
        LogoLine: string.Empty,
        LogoUrl: null,
        CanChooseLogo: false,
        ShowRemoveLogo: false,
        CanRemoveLogo: false,
        LogoBusy: false,
        LogoFailure: null,
        LogoFileKept: null);

    private static readonly DeskSettingsView _ready = _loading with
    {
        Status = V.Ready,
        Enabled = true,
        NotifyCustomersByEmail = true,
        BrandName = "Example Dental",
        CanEdit = true,
        LogoLine = "A logo is published.",
        LogoUrl = "https://desk.example/logo.png",
        CanChooseLogo = true,
        ShowRemoveLogo = true,
        CanRemoveLogo = true,
    };

    /// <summary>
    /// Stands in for district-ffi's check, which the projection tests pin
    /// against the real bytes: a GIF is refused for its type, and a file over
    /// five megabytes for its size.
    /// </summary>
    private static string? Check(PickedFileView file) =>
        file.Bytes.Length == 0 ? EmptyRefused
        : file.Bytes.AsSpan().StartsWith("GIF8"u8) ? TypeRefused
        : file.Bytes.Length > LogoLimit ? SizeRefused
        : null;

    private static (DeskSettingsViewModel Model, RecordingSink Sink) Attached(DeskSettingsView view)
    {
        var (context, sink) = Pages.Context();
        var model = new DeskSettingsViewModel { LogoProblem = Check };
        model.Attach(context);
        model.Show(view);
        return (model, sink);
    }

    private static PickedFile File(string name, byte[] bytes) => new(name, new PickedFileView(name, (ulong)bytes.Length, bytes));

    [Fact]
    public void NoFormBeforeTheSettingsAreRead()
    {
        var (model, sink) = Attached(_loading);
        Assert.Same(_loading, model.View);
        Assert.True(model.Load.Loading);
        Assert.False(model.CanEdit);
        Assert.False(model.MayPickLogo);
        model.PickedLogo([File("logo.png", _png)]);

        model.Show(_loading with { Status = V.Failed(V.Failure("Offline.", retryable: true), "Could not load the desk's settings") });
        Assert.True(model.Load.Failed);
        Assert.Equal("Could not load the desk's settings", model.Load.FailureTitle);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void EachChangeIsSentAsItHappensAndSaveOnce()
    {
        var (model, sink) = Attached(_ready);
        Assert.Equal(("Help desk settings", "Take tickets", "Email customers your replies", "Name customers see"), (model.Title, model.EnabledLabel, model.NotifyLabel, model.BrandLabel));
        Assert.StartsWith("Leave the name blank", model.Intro, StringComparison.Ordinal);
        Assert.Equal("While the desk is off, no tickets are recorded.", model.EnabledNote);
        Assert.True(model.Enabled);
        Assert.True(model.Notify);
        Assert.Equal("Example Dental", model.BrandName);
        Assert.Empty(sink.Sent);

        model.Enabled = false;
        model.Notify = false;
        model.BrandName = "Example Dental Help";
        Assert.Equal(
            [
                new UiEvent.Desk(new DeskAction.SetEnabled(false)),
                new UiEvent.Desk(new DeskAction.SetNotify(false)),
                new UiEvent.Desk(new DeskAction.EditBrandName("Example Dental Help")),
            ],
            sink.Sent);

        model.Show(_ready with { Enabled = false, NotifyCustomersByEmail = false, BrandName = "Example Dental Help", CanSave = true });
        Assert.Equal(3, sink.Sent.Count);
        model.SaveCommand.Execute(null);
        model.SaveCommand.Execute(null);
        Assert.Equal(new UiEvent.Desk(new DeskAction.SaveSettings()), sink.Sent[^1]);
        Assert.Equal(4, sink.Sent.Count);

        model.Show(_ready with { Saving = true, CanEdit = false, CanChooseLogo = false, CanRemoveLogo = false });
        Assert.True(model.Saving);
        Assert.False(model.CanEdit);
        model.Show(_ready with { CanSave = true, SaveFailure = V.Failure("Could not save.") });
        Assert.True(model.HasSaveFailure);
        Assert.Equal("Could not save.", model.SaveFailure);
        model.DismissFailuresCommand.Execute(null);
        Assert.Equal(new UiEvent.Desk(new DeskAction.DismissSettingsFailures()), sink.Sent[^1]);
    }

    [Fact]
    public void AGifLogoIsRefusedHereAndNeverSent()
    {
        var (model, sink) = Attached(_ready);
        model.PickedLogo([File("logo.gif", _gif)]);
        model.PickedLogo([File("renamed.png", _gif)]);

        Assert.Empty(sink.Sent);
        Assert.True(model.HasLogoNote);
        Assert.True(model.LogoNoteIsFailure);
        Assert.Equal(TypeRefused, model.LogoNote);

        // The refusal stands while the core says nothing newer about the logo.
        model.Show(_ready with { BrandName = "Example Dental Help" });
        Assert.Equal(TypeRefused, model.LogoNote);

        // A logo the service takes is sent, and the refusal goes.
        model.PickedLogo([File("logo.png", _png)]);
        Assert.Equal([new UiEvent.Desk(new DeskAction.UploadLogo(new PickedFileView("logo.png", (ulong)_png.Length, _png)))], sink.Sent);
        Assert.False(model.HasLogoNote);
        Assert.False(model.CanChooseLogo);
    }

    [Fact]
    public void ALogoOverTheSizeLimitIsRefusedHereAndNeverSent()
    {
        var (model, sink) = Attached(_ready);
        var large = new byte[LogoLimit + 1];
        _png.CopyTo(large, 0);
        model.PickedLogo([File("large.png", large)]);
        Assert.Empty(sink.Sent);
        Assert.Equal(SizeRefused, model.LogoNote);

        var limit = new byte[LogoLimit];
        _png.CopyTo(limit, 0);
        model.PickedLogo([File("limit.png", limit)]);
        Assert.Single(sink.Sent);
        Assert.False(model.HasLogoNote);
    }

    [Fact]
    public void AnEmptyLogoIsRefusedHereAndNeverSent()
    {
        var (model, sink) = Attached(_ready);
        model.PickedLogo([File("empty.png", [])]);
        Assert.Empty(sink.Sent);
        Assert.Equal(EmptyRefused, model.LogoNote);
    }

    [Fact]
    public void ARefusalGivesWayToWhatTheCoreSaysNext()
    {
        var (model, sink) = Attached(_ready);
        model.PickedLogo([File("logo.gif", _gif)]);
        model.Show(_ready with { LogoFailure = V.Failure("That image could not be read. Try picking it again.") });
        Assert.Equal("That image could not be read. Try picking it again.", model.LogoNote);

        model.PickedLogo([File("logo.gif", _gif)]);
        Assert.Equal(TypeRefused, model.LogoNote);
        model.DismissFailuresCommand.Execute(null);
        Assert.Equal("That image could not be read. Try picking it again.", model.LogoNote);
        Assert.Equal([new UiEvent.Desk(new DeskAction.DismissSettingsFailures())], sink.Sent);

        model.PickedLogo([File("logo.gif", _gif)]);
        model.Show(_ready with { LogoBusy = true, CanChooseLogo = false, CanRemoveLogo = false });
        Assert.False(model.HasLogoNote);
    }

    [Fact]
    public void AnUnreadableLogoIsTheCoresToSayAndACancelIsNothing()
    {
        var (model, sink) = Attached(_ready);
        model.PickedLogo([]);
        model.PickedLogo([new PickedFile("gone.png", null)]);
        Assert.Equal([new UiEvent.Desk(new DeskAction.LogoUnreadable())], sink.Sent);
    }

    [Fact]
    public void TheLogoIsRemovedOnceAndItsNotesShow()
    {
        var (model, sink) = Attached(_ready);
        Assert.True(model.ShowRemoveLogo);
        Assert.Equal("A logo is published.", model.LogoLine);
        model.RemoveLogoCommand.Execute(null);
        model.RemoveLogoCommand.Execute(null);
        Assert.Equal([new UiEvent.Desk(new DeskAction.DeleteLogo())], sink.Sent);

        model.Show(_ready with { LogoBusy = true, CanChooseLogo = false, CanRemoveLogo = false });
        Assert.True(model.LogoBusy);
        model.Show(_ready with { LogoUrl = null, ShowRemoveLogo = false, CanRemoveLogo = false, LogoLine = "No logo yet.", LogoFileKept = "The logo is no longer shown to customers." });
        Assert.Equal("The logo is no longer shown to customers.", model.LogoNote);
        Assert.False(model.LogoNoteIsFailure);
        Assert.False(model.ShowRemoveLogo);
        model.Show(_ready with { LogoFailure = V.Failure("That file is too large.") });
        Assert.Equal("That file is too large.", model.LogoNote);
        Assert.True(model.LogoNoteIsFailure);
    }

    [Fact]
    public void ARoleThatMayNotUseTheDeskIsOfferedNothing()
    {
        var (model, sink) = Attached(_ready with { CanEdit = false, CanChooseLogo = false, CanRemoveLogo = false });
        Assert.False(model.CanEdit);
        Assert.False(model.MayPickLogo);
        Assert.False(model.RemoveLogoCommand.CanExecute(null));
        Assert.False(model.SaveCommand.CanExecute(null));
        model.PickedLogo([File("logo.png", _png)]);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void TheDefaultCheckIsDistrictFfis()
    {
        // Constructing the view model calls nothing native: the check runs
        // only for a picked file.
        var model = new DeskSettingsViewModel();
        Assert.NotNull(model.LogoProblem);
    }
}
