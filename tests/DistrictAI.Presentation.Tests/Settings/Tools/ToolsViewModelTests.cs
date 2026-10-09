using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Tools;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.Tools;

public sealed class ToolsViewModelTests
{
    private static ToolRowView Row(string id, bool enabled = true, string? label = null, string? note = null) =>
        new(id, label ?? id, label is not null, enabled, note);

    private static readonly ToolRowView[] _rows =
    [
        Row("transfer_to_agent", label: "Transfer to a person (fallback)", note: "Needs a support team number."),
        Row("send_sms", enabled: false, label: "Text the caller"),
        Row("transfer_to_creator", note: "Stored for this workspace."),
    ];

    private static ToolsView View(
        SectionStatus? status = null,
        ToolRowView[]? tools = null,
        bool editable = true,
        SaveNoticeView? toolsNotice = null,
        bool toolsSaving = false,
        bool canSaveTools = false,
        bool research = true,
        SaveNoticeView? researchNotice = null,
        bool researchSaving = false,
        bool canSaveResearch = false) =>
        new(
            Title: "Skills",
            Status: status ?? new SectionStatus.Ready(),
            ToolsHeading: "On a call",
            ToolsNote: "What the receptionist may do.",
            Tools: tools ?? _rows,
            Editable: editable,
            ToolsNotice: toolsNotice,
            ToolsSaving: toolsSaving,
            CanSaveTools: canSaveTools,
            SaveToolsLabel: "Save skills",
            ResearchHeading: "Research contacts",
            ResearchTitle: "Outside research on contacts",
            ResearchBody: "When on, contacts can be researched.",
            ResearchEnabled: research,
            ResearchNotice: researchNotice,
            ResearchSaving: researchSaving,
            CanSaveResearch: canSaveResearch,
            SaveResearchLabel: "Save research setting");

    private static (ToolsViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new ToolsViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    [Fact]
    public void TheFormShowsTheCoreValues()
    {
        var (model, sink) = Attached();
        var view = View();
        model.Show(view);

        Assert.Same(view, model.View);
        Assert.Equal("Skills", model.Title);
        Assert.True(model.IsReady && model.Editable);
        Assert.False(model.IsLoading || model.HasStatus);
        Assert.Equal(("On a call", "What the receptionist may do."), (model.ToolsHeading, model.ToolsNote));
        Assert.Equal(["transfer_to_agent", "send_sms", "transfer_to_creator"], model.Tools.Select(tool => tool.Id));
        var fallback = model.Tools[0];
        Assert.Equal("Transfer to a person (fallback)", fallback.Label);
        Assert.True(fallback.HasNote && fallback.IsOn && fallback.IsEnabled);
        Assert.False(model.Tools[1].IsOn || model.Tools[1].HasNote);
        Assert.Equal("transfer_to_creator", model.Tools[2].Label);
        Assert.Equal(("Research contacts", "Outside research on contacts"), (model.ResearchHeading, model.ResearchTitle));
        Assert.StartsWith("When on", model.ResearchBody, StringComparison.Ordinal);
        Assert.True(model.ResearchOn);
        Assert.Equal(("Save skills", "Save research setting"), (model.SaveToolsLabel, model.SaveResearchLabel));
        Assert.False(model.SaveToolsCommand.CanExecute(null) || model.SaveResearchCommand.CanExecute(null));
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void LoadingAFailedReadAndASaveNotReadBack()
    {
        var model = new ToolsViewModel();
        model.Show(View(status: new SectionStatus.Loading(), tools: []));
        Assert.True(model.IsLoading);
        Assert.False(model.IsReady || model.HasStatus);
        Assert.Empty(model.Tools);

        model.Show(View(status: new SectionStatus.Failed("Could not load", V.Failure("Offline.", retryable: true))));
        Assert.True(model.HasStatus && model.HasStatusAction);
        Assert.Equal(("Could not load", "Offline.", "Try again"), (model.StatusTitle, model.StatusBody, model.StatusAction));

        model.Show(View(status: new SectionStatus.Failed("Could not load", V.Failure("Refused."))));
        Assert.False(model.HasStatusAction);

        model.Show(View(status: new SectionStatus.Stale("Saved", "Read them again first.", "Read them again")));
        Assert.Equal(("Saved", "Read them again"), (model.StatusTitle, model.StatusAction));
        Assert.True(model.HasStatusAction);
    }

    [Fact]
    public void FlippingASwitchSendsItAndTheCoreDecidesWhatShows()
    {
        var (model, sink) = Attached();
        model.Show(View());
        var sms = model.Tools[1];

        sms.IsOn = true;
        Assert.Equal([new UiEvent.Tools(new ToolsAction.Toggle("send_sms", true))], sink.Sent);

        // The core took it: shown again, nothing more is sent, and the rows stay
        // the same objects (focus stays on the switch).
        model.Show(View(tools: [_rows[0], Row("send_sms", label: "Text the caller"), _rows[2]], canSaveTools: true));
        Assert.Single(sink.Sent);
        Assert.Same(sms, model.Tools[1]);
        Assert.True(sms.IsOn);

        // The core did not take one: the switch goes back, and the write-back sends nothing.
        var changed = new List<string?>();
        model.Tools[0].PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        model.Tools[0].IsOn = false;
        Assert.Equal(2, sink.Sent.Count);
        model.Show(View(tools: [_rows[0], Row("send_sms", label: "Text the caller"), _rows[2]]));
        Assert.True(model.Tools[0].IsOn);
        Assert.Contains(nameof(ToolItem.IsOn), changed);
        Assert.Equal(2, sink.Sent.Count);

        // A different list builds the switches again.
        model.Show(View(tools: [Row("leave_message", label: "Take a message")]));
        Assert.Equal("leave_message", Assert.Single(model.Tools).Id);
    }

    [Fact]
    public void WhileSavingTheSwitchesWait()
    {
        var model = new ToolsViewModel();
        model.Show(View(editable: false, toolsSaving: true));
        Assert.True(model.ToolsSaving);
        Assert.All(model.Tools, tool => Assert.False(tool.IsEnabled));
        Assert.False(model.Editable);
    }

    [Fact]
    public void ResearchIsSentAndSavedAlone()
    {
        var (model, sink) = Attached();
        model.Show(View());

        model.ResearchOn = false;
        model.Show(View(research: false, canSaveResearch: true));
        Assert.True(model.SaveResearchCommand.CanExecute(null));
        model.SaveResearchCommand.Execute(null);

        Assert.Equal(
            [
                new UiEvent.Tools(new ToolsAction.SetResearch(false)),
                new UiEvent.Tools(new ToolsAction.SaveResearch()),
            ],
            sink.Sent);
        Assert.False(model.ResearchOn);
    }

    [Fact]
    public void SaveAndTheNoticesSayHowItEnded()
    {
        var (model, sink) = Attached();
        model.Show(View(canSaveTools: true));
        model.SaveToolsCommand.Execute(null);

        model.Show(View(toolsNotice: new SaveNoticeView("Saved.", true), researchNotice: new SaveNoticeView("Offline.", false)));
        Assert.True(model.HasToolsNotice && model.ToolsNoticeSaved);
        Assert.Equal("Saved.", model.ToolsNoticeText);
        Assert.True(model.HasResearchNotice);
        Assert.False(model.ResearchNoticeSaved);
        Assert.Equal("Offline.", model.ResearchNoticeText);

        model.DismissNoticesCommand.Execute(null);
        model.RetryCommand.Execute(null);
        Assert.Equal(
            [
                new UiEvent.Tools(new ToolsAction.SaveTools()),
                new UiEvent.Tools(new ToolsAction.DismissNotices()),
                new UiEvent.Refresh(),
            ],
            sink.Sent);
    }

    [Fact]
    public void NothingIsSentBeforeTheContextIsAttached()
    {
        var model = new ToolsViewModel();
        model.Show(View());
        model.Tools[1].IsOn = true;
        model.ResearchOn = false;
        model.Send(new ToolsAction.SaveTools());
        model.RetryCommand.Execute(null);
        Assert.True(model.Tools[1].IsOn);
    }
}
