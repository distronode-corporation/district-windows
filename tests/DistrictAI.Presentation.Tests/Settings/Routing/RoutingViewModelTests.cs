using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Routing;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.Routing;

public sealed class RoutingViewModelTests
{
    private static RulePickerView Picker(string label, string selected, params string[] values) =>
        new(label, new PickerView(
            [.. values.Select(value => new ChoiceView(value, value))],
            selected,
            values.Contains(selected) ? selected : selected.Length > 0 ? selected : "Not chosen"));

    private static RoutingRuleView Rule(uint index, string value = "tech", string voice = "Fenrir", string? kept = null) =>
        new(
            index,
            "Rule " + (index + 1),
            "Industry contains \"" + value + "\"",
            Picker("Caller detail", "industry", "industry", "seniority"),
            Picker("Compared by", "contains", "contains", "equals"),
            value,
            Picker("Voice", voice, "Puck", "Fenrir"),
            "Be brief.",
            "The persona's own",
            kept);

    private static RoutingView View(
        SectionStatus? status = null,
        EmptyView? unmodellable = null,
        RoutingRuleView[]? rules = null,
        EmptyView? empty = null,
        bool canEdit = true,
        bool canSave = false,
        bool saving = false,
        SaveNoticeView? notice = null,
        QuestionView? confirming = null) =>
        new(
            "Call routing rules",
            "Which callers get which voice and instruction.",
            status ?? new SectionStatus.Ready(),
            unmodellable,
            rules ?? [Rule(0), Rule(1, "dental", "Orpheus", "Also stored: Match.")],
            empty,
            canEdit,
            canSave,
            saving,
            notice,
            confirming);

    private static (RoutingViewModel Model, RecordingSink Sink) Attached(RoutingView view)
    {
        var (context, sink) = Pages.Context();
        var model = new RoutingViewModel();
        model.Attach(context);
        model.Show(view);
        return (model, sink);
    }

    [Fact]
    public void TheRulesShowAsTheCoreHasThemAndShowingSendsNothing()
    {
        var (model, sink) = Attached(View());
        Assert.True(model.IsReady);
        Assert.False(model.HasStatus);
        Assert.Equal(2, model.Rules.Count);
        var second = model.Rules[1];
        Assert.Equal("Rule 2", second.Heading);
        Assert.Equal("Remove Rule 2", second.RemoveName);
        Assert.Equal("dental", second.Value);
        // A stored voice the choices lack is listed first, as stored, and chosen.
        Assert.Equal("Orpheus", second.Voice?.Label);
        Assert.Equal(3, second.VoiceChoices.Count);
        Assert.True(second.HasKept);
        Assert.Equal("The persona's own", second.Engine);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void EachEditIsSentWithItsRuleAndField()
    {
        var (model, sink) = Attached(View());
        var rule = model.Rules[0];
        rule.Field = rule.FieldChoices[1];
        rule.Operator = rule.OperatorChoices[1];
        rule.Voice = rule.VoiceChoices[0];
        rule.Value = "law";
        rule.Instruction = "Be formal.";
        rule.RemoveCommand.Execute(null);
        Assert.Equal(
            [
                new UiEvent.Routing(new RoutingAction.Edit(0, RoutingField.Field, "seniority")),
                new UiEvent.Routing(new RoutingAction.Edit(0, RoutingField.Operator, "equals")),
                new UiEvent.Routing(new RoutingAction.Edit(0, RoutingField.Voice, "Puck")),
                new UiEvent.Routing(new RoutingAction.Edit(0, RoutingField.Value, "law")),
                new UiEvent.Routing(new RoutingAction.Edit(0, RoutingField.Instruction, "Be formal.")),
                new UiEvent.Routing(new RoutingAction.Remove(0)),
            ],
            sink.Sent);

        // The core's echo of what the box sent does not move the box back.
        var kept = model.Rules[0];
        rule.Value = "lawyers";
        model.Show(View(rules: [Rule(0, "law"), Rule(1)]));
        Assert.Same(kept, model.Rules[0]);
        Assert.Equal("lawyers", model.Rules[0].Value);
    }

    [Fact]
    public void AddingAndRemovingMakesTheCardsAgain()
    {
        var (model, sink) = Attached(View());
        model.AddCommand.Execute(null);
        Assert.Equal([new UiEvent.Routing(new RoutingAction.Add())], sink.Sent);
        model.Show(View(rules: [Rule(0), Rule(1), Rule(2, string.Empty, "Puck")], canSave: true));
        Assert.Equal(3, model.Rules.Count);
        Assert.Equal(string.Empty, model.Rules[2].Value);
    }

    [Fact]
    public void SavingAsksTheCoreAndTheAnswerIsSent()
    {
        var (model, sink) = Attached(View(canSave: true));
        model.SaveCommand.Execute(null);
        model.SaveCommand.Execute(null);
        model.Show(View(canSave: true, confirming: new QuestionView("Replace the routing rules?", "Exactly 2 rules.", "Replace", false)));
        Assert.Equal("Replace", model.Confirming?.Action);
        model.Answer(false);
        model.Answer(true);
        Assert.Equal(
            [
                new UiEvent.Routing(new RoutingAction.Save()),
                new UiEvent.Routing(new RoutingAction.CancelSave()),
                new UiEvent.Routing(new RoutingAction.ConfirmSave()),
            ],
            sink.Sent);

        model.Show(View(canEdit: false, saving: true));
        Assert.True(model.Saving);
        Assert.False(model.AddCommand.CanExecute(null));
        model.Rules[0].Value = "x";
        model.Rules[0].RemoveCommand.Execute(null);
        model.AddCommand.Execute(null);
        Assert.Equal(3, sink.Sent.Count);

        model.Show(View(notice: new SaveNoticeView("Saved.", true)));
        Assert.True(model.NoticeSaved);
        model.DismissNoticeCommand.Execute(null);
        Assert.Equal(new UiEvent.Routing(new RoutingAction.DismissNotice()), sink.Sent[^1]);
    }

    [Fact]
    public void EachPageStatusSaysItsOwnWords()
    {
        var (model, sink) = Attached(View(status: new SectionStatus.Loading(), rules: []));
        Assert.True(model.IsLoading);
        Assert.False(model.IsReady);

        model.Show(View(status: new SectionStatus.Failed("Could not load this workspace's settings", V.Failure("Offline.", retryable: true)), rules: []));
        Assert.Equal("Try again", model.StatusAction);
        model.RetryCommand.Execute(null);
        Assert.Equal([new UiEvent.Refresh()], sink.Sent);

        model.Show(View(status: new SectionStatus.Failed("Could not load", V.Failure("Refused.")), rules: []));
        Assert.False(model.HasStatusAction);

        model.Show(View(status: new SectionStatus.Stale("Saved", "Read them again before changing anything else.", "Read them again"), rules: []));
        Assert.Equal("Read them again", model.StatusAction);

        model.Show(View(unmodellable: new EmptyView("Cannot be edited here", "Change them on the website."), rules: []));
        Assert.Equal("Cannot be edited here", model.StatusTitle);
        Assert.False(model.IsReady);
        Assert.False(model.HasStatusAction);

        model.Show(View(rules: [], empty: new EmptyView("No routing rules", "Every caller gets the persona.")));
        Assert.True(model.ShowEmpty);
        Assert.Empty(model.Rules);

        var detached = new RoutingViewModel();
        detached.Send(new RoutingAction.Add());
        detached.RetryCommand.Execute(null);
    }
}
