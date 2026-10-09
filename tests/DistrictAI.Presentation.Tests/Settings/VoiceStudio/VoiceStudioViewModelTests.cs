using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.VoiceStudio;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.VoiceStudio;

public sealed class VoiceStudioViewModelTests
{
    private const string MinDelay = "engineMix.turn.minDelay";
    private const string Keyterms = "engineMix.stt.keyterms";

    private static StudioTuningView Slider(long? value, string? defaultLabel = "Use the default (0.30)") =>
        new(MinDelay, "Shortest wait", null, true, null,
            new StudioControlView.Slider(0, 1000, 50, value, 300, value is null ? "0.30" : (value.Value / 1000.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), defaultLabel));

    private static StudioTuningView[] Controls(long? delay = null, bool on = false, string terms = "", string mode = "auto") =>
    [
        new("engineMix.preemptiveTts", "Speak sooner", "Starts speaking early.", false, null, new StudioControlView.Switch(on)),
        new("engineMix.turn.mode", "Mode", null, true, null, new StudioControlView.Select([new("auto", "Automatic"), new("fixed", "Fixed")], mode)),
        Slider(delay),
        new(Keyterms, "Key terms", null, true, "Interruptions", new StudioControlView.Lines(terms)),
    ];

    private static StudioFormView Form(
        StudioTuningView[]? controls = null,
        string voice = "asteria",
        bool editable = true,
        bool dirty = false,
        StudioNoticeView? notice = null,
        bool saving = false,
        string tier = "stable",
        string? basedOn = null) =>
        new(
            "Pick a starting point.",
            editable,
            true,
            "Models",
            "Stable models are proven.",
            [new("stable", "Stable"), new("latest", "Latest")],
            tier,
            "Starting point",
            [
                new("fastest", "Fastest", "The quickest.", "About 970 ms", false, true),
                new("natural", "Most natural", "The warmest.", "About 1,200 ms", true, false),
            ],
            "Default",
            basedOn,
            "Reset",
            "Signal chain",
            [new("stt", "Ear: Flux", "Speech recognition", false), new("tts", "Voice: Aura-2", "Speech synthesis", true)],
            new StudioEditorView(
                "Voice",
                [
                    new(StudioPicker.Vendor, "Vendor", [new("deepgram", "Deepgram")], "deepgram"),
                    new(StudioPicker.Voice, "Voice", [new("asteria", "Asteria"), new("luna", "Luna")], voice),
                ],
                controls ?? Controls(),
                "Advanced",
                true),
            new StudioMeterView("Time to first word", "From the last word.", "About 970 ms", "Some steps are not measured.", [new("End of turn", "400 ms")], "Measured on live calls."),
            new StudioResidencyView("Where the call is processed", false, "Part of this call leaves your region.", ["Brain: global"]),
            notice,
            dirty ? "Unsaved changes" : "All changes saved",
            dirty,
            "Save voice settings",
            dirty && editable && !saving,
            saving);

    private static VoiceStudioView Ready(StudioFormView? form = null) => new("Voice", new LoadStatus.Ready(), form ?? Form());

    private static (VoiceStudioViewModel Model, RecordingSink Sink) Attached(VoiceStudioView? view = null)
    {
        var model = new VoiceStudioViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(view ?? Ready());
        return (model, sink);
    }

    private static UiEvent.VoiceStudio Sent(VoiceStudioAction action) => new(action);

    [Fact]
    public void ItShowsTheStudioInTheReadsWords()
    {
        var (model, sink) = Attached();

        Assert.Equal("Voice", model.Title);
        Assert.True(model.HasStudio && model.Load.Ready && model.Editable);
        Assert.Equal("Pick a starting point.", model.Description);
        Assert.Equal(["Stable", "Latest"], model.Tiers.Select(tier => tier.ToString()));
        Assert.Equal(0, model.TierIndex);
        Assert.Equal("Starting point", model.RecipesLabel);
        Assert.Equal("Fastest. The quickest. About 970 ms", model.Recipes[0].AccessibleName);
        Assert.True(model.Recipes[0].Selected && !model.Recipes[0].HasBadge);
        Assert.True(model.Recipes[1].HasBadge);
        Assert.Equal("Most natural. Default. The warmest. About 1,200 ms", model.Recipes[1].ToString());
        Assert.Equal("Voice: Aura-2. Speech synthesis", model.Blocks[1].ToString());
        Assert.Equal("Voice", model.EditorTitle);
        Assert.Equal([StudioPicker.Vendor, StudioPicker.Voice], model.Pickers.Select(picker => picker.Picker));
        Assert.Equal(0, model.Pickers[1].SelectedIndex);
        Assert.Equal("Voice", model.Pickers[1].Label);
        Assert.Equal(["engineMix.preemptiveTts"], model.Controls.Select(control => control.Key));
        Assert.Equal(["engineMix.turn.mode", MinDelay, Keyterms], model.AdvancedControls.Select(control => control.Key));
        Assert.True(model.HasAdvanced);
        Assert.Equal("Advanced", model.AdvancedLabel);
        Assert.Equal("About 970 ms", model.MeterHeadline);
        Assert.True(model.HasMeterNote);
        Assert.Equal("End of turn: 400 ms", model.Stages[0].AccessibleName);
        Assert.Equal("Measured on live calls.", model.MeterSource);
        Assert.Equal("From the last word.", model.MeterDescription);
        Assert.Equal("Time to first word", model.MeterHeading);
        Assert.Equal("Where the call is processed", model.ResidencyHeading);
        Assert.Equal("Part of this call leaves your region.", model.ResidencyText);
        Assert.True(model.HasLegsOut);
        Assert.Equal("All changes saved", model.Pending);
        Assert.Equal("Save voice settings", model.SaveLabel);
        Assert.False(model.CanSave || model.Saving || model.HasBasedOn);
        Assert.Equal(string.Empty, model.Notice);
        Assert.Equal("Models", model.TierLabel);
        Assert.Equal("Stable models are proven.", model.TierDescription);
        Assert.Equal("Reset", model.ResetLabel);
        Assert.Equal("Signal chain", model.ChainLabel);

        var flag = model.Controls[0];
        Assert.True(flag.IsSwitch && flag.HasDescription && !flag.IsSlider);
        var slider = model.AdvancedControls[1];
        Assert.True(slider.IsSlider && slider.HasDefault && slider.UseDefault && !slider.SliderEnabled);
        Assert.Equal((0.0, 1.0, 0.05, 0.3), (slider.Minimum, slider.Maximum, slider.Step, slider.Value));
        Assert.Equal("0.30", slider.ValueText);
        Assert.Equal("Use the default (0.30)", slider.DefaultLabel);
        var lines = model.AdvancedControls[2];
        Assert.True(lines.IsLines && lines.HasHeading);
        Assert.Equal("Interruptions", lines.Heading);
        Assert.Equal(0, model.AdvancedControls[0].SelectedIndex);

        // Drawing the core's values sends nothing.
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void LoadingAndAFailedReadShowNoStudio()
    {
        var (model, _) = Attached(new VoiceStudioView("Voice", new LoadStatus.Loading(), null));
        Assert.True(model.Load.Loading);
        Assert.False(model.HasStudio || model.Editable || model.CanSave);

        model.Show(new VoiceStudioView("Voice", new LoadStatus.Failed(new FailureView("Offline.", null, true), "Could not load Voice Studio"), null));
        Assert.True(model.Load.Failed && model.Load.CanRetry);
        Assert.Equal("Could not load Voice Studio", model.Load.FailureTitle);
        Assert.False(model.HasStudio);
    }

    [Fact]
    public void EachControlSendsWhatTheMemberChanges()
    {
        var (model, sink) = Attached();

        model.TierIndex = 1;
        model.ApplyRecipe(model.Recipes[1]);
        model.Pickers[1].SelectedIndex = 1;
        model.Controls[0].On = true;
        model.AdvancedControls[0].SelectedIndex = 1;
        model.AdvancedControls[1].UseDefault = false;
        model.AdvancedControls[2].Text = "Ada\nGrace";

        Assert.Equal(
            [
                Sent(new VoiceStudioAction.SelectTier("latest")),
                Sent(new VoiceStudioAction.ApplyRecipe("natural")),
                Sent(new VoiceStudioAction.Pick(StudioPicker.Voice, "luna")),
                Sent(new VoiceStudioAction.SetFlag("engineMix.preemptiveTts", true)),
                Sent(new VoiceStudioAction.SetChoice("engineMix.turn.mode", "fixed")),
                Sent(new VoiceStudioAction.SetNumber(MinDelay, 300)),
                Sent(new VoiceStudioAction.SetLines(Keyterms, "Ada\nGrace")),
            ],
            sink.Sent);

        // The slider now has a value: moving it sends thousandths, and the box
        // goes back to the default.
        sink.Sent.Clear();
        model.Show(Ready(Form(controls: Controls(delay: 300))));
        var slider = model.AdvancedControls[1];
        Assert.True(slider.SliderEnabled && !slider.UseDefault);
        slider.Value = 0.45;
        slider.UseDefault = true;
        Assert.Equal(
            [
                Sent(new VoiceStudioAction.SetNumber(MinDelay, 450)),
                Sent(new VoiceStudioAction.SetNumber(MinDelay, null)),
            ],
            sink.Sent);
    }

    [Fact]
    public void AChoiceAlreadyHeldOrOutOfRangeSendsNothing()
    {
        var (model, sink) = Attached();

        model.TierIndex = 0;
        model.TierIndex = -1;
        model.Pickers[1].SelectedIndex = 0;
        model.Pickers[1].SelectedIndex = 7;
        model.Controls[0].On = false;
        model.AdvancedControls[0].SelectedIndex = 0;
        model.AdvancedControls[0].SelectedIndex = -1;
        model.AdvancedControls[1].UseDefault = true;
        model.AdvancedControls[1].Value = 0.5;
        model.Blocks.ToList().ForEach(model.SelectLeg);
        model.AdvancedControls[2].Text = "  \n";

        Assert.Equal([Sent(new VoiceStudioAction.SelectLeg("stt"))], sink.Sent);
    }

    [Fact]
    public void KeyTermsBeingTypedStayWhileTheySayTheSame()
    {
        var (model, _) = Attached();
        var lines = model.AdvancedControls[2];
        lines.Text = "Ada\n";
        model.Show(Ready(Form(controls: Controls(terms: "Ada"))));
        Assert.Equal("Ada\n", lines.Text);
        Assert.Same(lines, model.AdvancedControls[2]);

        // The core read them otherwise (a repeat dropped): its terms show.
        lines.Text = "Ada\nAda\nGrace";
        model.Show(Ready(Form(controls: Controls(terms: "Ada\nGrace\nHopper"))));
        Assert.Equal("Ada\nGrace\nHopper", lines.Text);
        Assert.Equal("Ada\nGrace", StudioTuningItem.Terms(" Ada \n\nGrace\nAda"));
    }

    [Fact]
    public void AControlOfAnotherKindIsMadeAgainAndOneTooManyGoes()
    {
        var (model, _) = Attached();
        var first = model.AdvancedControls[0];
        model.Show(Ready(Form(controls:
        [
            new("engineMix.turn.mode", "Mode", null, true, null, new StudioControlView.Switch(true)),
            Slider(null, defaultLabel: null),
        ])));
        Assert.NotSame(first, model.AdvancedControls[0]);
        Assert.True(model.AdvancedControls[0].IsSwitch);
        Assert.Equal(2, model.AdvancedControls.Count);
        Assert.Empty(model.Controls);
        var slider = model.AdvancedControls[1];
        Assert.True(slider.SliderEnabled && !slider.HasDefault);
        // No default box: ticking it does nothing.
        slider.UseDefault = false;
        Assert.False(slider.Fits(new("engineMix.turn.minDelay", "x", null, true, null, new StudioControlView.Lines(string.Empty))));
        Assert.True(slider.Fits(Slider(null)));
        Assert.False(slider.Fits(Controls()[1]));

        // A slider with no step moves by hundredths.
        model.Show(Ready(Form(controls: [new(MinDelay, "Shortest wait", null, false, null, new StudioControlView.Slider(0, 1000, 0, 200, 300, "0.20", null))])));
        Assert.Equal(0.01, model.Controls[0].Step);
        Assert.Equal(0.2, model.Controls[0].Value);
    }

    [Fact]
    public void NothingIsSentWhileTheStudioCannotBeChanged()
    {
        var (model, sink) = Attached(Ready(Form(editable: false, dirty: true, saving: true)));

        Assert.False(model.Editable || model.CanSave);
        Assert.True(model.Saving);
        model.TierIndex = 1;
        model.ApplyRecipe(model.Recipes[1]);
        model.ResetCommand.Execute(null);
        model.Controls[0].On = true;
        model.SaveCommand.Execute(null);
        // Opening another leg is allowed while a save is on its way.
        model.SelectLeg(model.Blocks[0]);

        Assert.Equal([Sent(new VoiceStudioAction.SelectLeg("stt"))], sink.Sent);
    }

    [Fact]
    public void SaveIsOnePressAndTheNoticeReadsAsItWent()
    {
        var (model, sink) = Attached(Ready(Form(voice: "luna", dirty: true, basedOn: "Based on Fastest, 1 change.")));
        Assert.True(model.CanSave && model.HasBasedOn);
        Assert.Equal("Based on Fastest, 1 change.", model.BasedOn);
        Assert.Equal("Unsaved changes", model.Pending);
        model.ResetCommand.Execute(null);
        model.SaveCommand.Execute(null);
        model.SaveCommand.Execute(null);
        Assert.Equal([Sent(new VoiceStudioAction.Reset()), Sent(new VoiceStudioAction.Save())], sink.Sent);

        foreach (var (tone, saved, warning, error) in new[]
        {
            (StudioNoticeTone.Success, true, false, false),
            (StudioNoticeTone.Warning, false, true, false),
            (StudioNoticeTone.Error, false, false, true),
        })
        {
            model.Show(Ready(Form(notice: new StudioNoticeView("Voice settings saved.", tone))));
            Assert.Equal("Voice settings saved.", model.Notice);
            Assert.Equal((saved, warning, error), (model.NoticeSaved, model.NoticeWarning, model.NoticeError));
        }
        sink.Sent.Clear();
        model.DismissNoticeCommand.Execute(null);
        Assert.Equal([Sent(new VoiceStudioAction.DismissNotice())], sink.Sent);
    }

    [Fact]
    public void WithoutACoreNothingIsSent()
    {
        var model = new VoiceStudioViewModel();
        model.Show(Ready());
        model.Send(new VoiceStudioAction.Open());
        model.Controls[0].On = true;
        Assert.NotNull(model.View);
    }
}
