using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Directory;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.Directory;

public sealed class DirectoryViewModelTests
{
    private static DirectoryEntryView Entry(uint index, string name, string number) =>
        new(index, name.Length > 0 ? name : "No name", number.Length > 0 ? number : "No phone number", name, number);

    private static DirectoryView View(
        SectionStatus? status = null,
        EmptyView? unmodellable = null,
        DirectoryEntryView[]? entries = null,
        EmptyView? empty = null,
        string? incomplete = null,
        string newName = "",
        string newNumber = "",
        string? addRejected = null,
        bool canEdit = true,
        bool canSave = false,
        bool saving = false,
        SaveNoticeView? notice = null,
        QuestionView? confirming = null) =>
        new(
            "Transfer directory",
            "Who the receptionist can put a live caller through to.",
            status ?? new SectionStatus.Ready(),
            unmodellable,
            entries ?? [Entry(0, "Ops desk", "+12125550177"), Entry(1, "On call", "+12125550166")],
            empty,
            incomplete,
            newName,
            newNumber,
            addRejected,
            canEdit,
            canSave,
            saving,
            notice,
            confirming);

    private static (DirectoryViewModel Model, RecordingSink Sink) Attached(DirectoryView view)
    {
        var (context, sink) = Pages.Context();
        var model = new DirectoryViewModel();
        model.Attach(context);
        model.Show(view);
        return (model, sink);
    }

    [Fact]
    public void TheEntriesShowAsTheCoreHasThemAndShowingSendsNothing()
    {
        var (model, sink) = Attached(View(incomplete: "One entry lacks a name or a number."));
        Assert.True(model.IsReady);
        Assert.Equal(2, model.Entries.Count);
        var first = model.Entries[0];
        Assert.Equal("Ops desk", first.Title);
        Assert.Equal("+12125550177", first.PhoneNumber);
        Assert.Equal("Name of Ops desk", first.NameLabel);
        Assert.Equal("Phone number of Ops desk", first.NumberLabel);
        Assert.Equal("Remove Ops desk from the directory", first.RemoveName);
        Assert.True(model.HasIncomplete);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void EachEditAndTheNewEntryAreSent()
    {
        var (model, sink) = Attached(View());
        model.Entries[1].Name = "Night line";
        model.Entries[1].PhoneNumber = "+12125550199";
        model.Entries[0].RemoveCommand.Execute(null);
        model.NewName = "Front desk";
        model.NewPhoneNumber = "+12125550155";
        model.AddCommand.Execute(null);
        Assert.Equal(
            [
                new UiEvent.Directory(new DirectoryAction.Edit(1, DirectoryEntryField.Name, "Night line")),
                new UiEvent.Directory(new DirectoryAction.Edit(1, DirectoryEntryField.PhoneNumber, "+12125550199")),
                new UiEvent.Directory(new DirectoryAction.Remove(0)),
                new UiEvent.Directory(new DirectoryAction.EditNewName("Front desk")),
                new UiEvent.Directory(new DirectoryAction.EditNewPhoneNumber("+12125550155")),
                new UiEvent.Directory(new DirectoryAction.Add()),
            ],
            sink.Sent);

        // The core echoes what the boxes sent: the boxes are not moved back.
        model.NewName = "Front desk team";
        model.Show(View(newName: "Front desk", newNumber: "+12125550155"));
        Assert.Equal("Front desk team", model.NewName);
        // Added: the core clears the new entry's boxes, which they did not send.
        model.Show(View(entries: [Entry(0, "Ops desk", "+12125550177"), Entry(1, "On call", "+12125550166"), Entry(2, "Front desk", "+12125550155")]));
        Assert.Equal(string.Empty, model.NewName);
        Assert.Equal(string.Empty, model.NewPhoneNumber);
        Assert.Equal(3, model.Entries.Count);
    }

    [Fact]
    public void AnAddWithoutBothSaysWhy()
    {
        var (model, _) = Attached(View(newName: "Front desk", addRejected: "A name and a phone number are both needed to add someone."));
        Assert.True(model.HasAddRejected);
        Assert.Equal("Front desk", model.NewName);
    }

    [Fact]
    public void SavingAsksTheCoreAndTheAnswerIsSent()
    {
        var (model, sink) = Attached(View(canSave: true));
        model.SaveCommand.Execute(null);
        model.SaveCommand.Execute(null);
        model.Show(View(canSave: true, confirming: new QuestionView("Remove every transfer target?", "Saving an empty directory removes everyone.", "Remove everyone", true)));
        Assert.True(model.Confirming?.Destructive);
        model.Answer(true);
        model.Answer(false);
        Assert.Equal(
            [
                new UiEvent.Directory(new DirectoryAction.Save()),
                new UiEvent.Directory(new DirectoryAction.ConfirmSave()),
                new UiEvent.Directory(new DirectoryAction.CancelSave()),
            ],
            sink.Sent);

        model.Show(View(canEdit: false, saving: true));
        Assert.True(model.Saving);
        model.Entries[0].Name = "x";
        model.Entries[0].RemoveCommand.Execute(null);
        model.AddCommand.Execute(null);
        Assert.Equal(3, sink.Sent.Count);

        model.Show(View(notice: new SaveNoticeView("The service refused it.", false)));
        Assert.True(model.HasNotice);
        Assert.False(model.NoticeSaved);
        model.DismissNoticeCommand.Execute(null);
        Assert.Equal(new UiEvent.Directory(new DirectoryAction.DismissNotice()), sink.Sent[^1]);
    }

    [Fact]
    public void EachPageStatusSaysItsOwnWords()
    {
        var (model, sink) = Attached(View(status: new SectionStatus.Loading(), entries: []));
        Assert.True(model.IsLoading);

        model.Show(View(status: new SectionStatus.Failed("Could not load this workspace's settings", V.Failure("Offline.", retryable: true)), entries: []));
        Assert.Equal("Try again", model.StatusAction);
        model.RetryCommand.Execute(null);
        Assert.Equal([new UiEvent.Refresh()], sink.Sent);

        model.Show(View(status: new SectionStatus.Failed("Could not load", V.Failure("Refused.")), entries: []));
        Assert.False(model.HasStatusAction);

        model.Show(View(status: new SectionStatus.Stale("Saved", "Read them again before changing anything else.", "Read them again"), entries: []));
        Assert.Equal("Saved", model.StatusTitle);

        model.Show(View(unmodellable: new EmptyView("Cannot be edited here", "Change it on the website."), entries: []));
        Assert.False(model.IsReady);
        Assert.True(model.HasStatus);

        model.Show(View(entries: [], empty: new EmptyView("No transfer targets", "Add someone below.")));
        Assert.True(model.ShowEmpty);

        var detached = new DirectoryViewModel();
        detached.Send(new DirectoryAction.Add());
        detached.RetryCommand.Execute(null);
    }
}
