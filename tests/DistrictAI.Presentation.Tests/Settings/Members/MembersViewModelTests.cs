using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Members;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.Members;

public sealed class MembersViewModelTests
{
    private static readonly RoleChoiceView[] _roles =
    [
        new(MemberRoleView.Agency, "Agency", "Full access."),
        new(MemberRoleView.Client, "Client", "Everyday use."),
        new(MemberRoleView.Viewer, "Viewer", "Read only."),
    ];

    private static MemberRowView Row(string email, MemberRoleView? role, bool canChange = true) =>
        new(email, (role?.ToString() ?? "Owner") + " · what it may do", role, canChange, "Remove from this workspace");

    private static AddMemberView Add(string email = "", bool editable = true, string? rejected = null) =>
        new("Add a member", "Email address", email, "Role", MemberRoleView.Client, editable, editable && email.Contains('@', StringComparison.Ordinal), rejected, "Adding someone sends no invitation.", "Add");

    private static RenameView Rename(string current = "Example Dental", string newName = "", bool canRename = false, bool renaming = false) =>
        new("The workspace's name", "Now", current, "New name", newName, !renaming, canRename, renaming, "Rename the workspace");

    private static MembersView View(
        SectionStatus? status = null,
        MemberRowView[]? members = null,
        bool changing = false,
        string? readOnly = null,
        SaveNoticeView? notice = null,
        AddMemberView? add = null,
        RenameView? rename = null,
        RemoveQuestionView? question = null) =>
        new(
            "Members",
            status ?? new SectionStatus.Ready(),
            members ?? [Row("founder@example.com", MemberRoleView.Agency), Row("auditor@example.com", MemberRoleView.Viewer)],
            changing,
            readOnly,
            notice,
            _roles,
            add,
            rename,
            question);

    [Fact]
    public void AnAgencyMemberSeesEveryControl()
    {
        var model = new MembersViewModel();
        var view = View(add: Add(), rename: Rename());
        model.Show(view);

        Assert.Same(view, model.View);
        Assert.Equal("Members", model.Title);
        Assert.True(model.IsReady && model.CanManage && model.CanRenameHere);
        Assert.False(model.IsLoading || model.HasStatus || model.HasReadOnlyNote);
        Assert.Equal(["founder@example.com", "auditor@example.com"], model.Members.Select(member => member.Email));
        var founder = model.Members[0];
        Assert.True(founder.CanChange);
        Assert.Equal(MemberRoleView.Agency, founder.SelectedRole?.Role);
        Assert.Equal("Agency", founder.SelectedRole?.ToString());
        Assert.StartsWith("founder@example.com, Agency", founder.AccessibleName, StringComparison.Ordinal);
        Assert.Equal("Remove from this workspace", founder.RemoveLabel);
        Assert.Equal(3, model.Roles.Count);
        Assert.Equal(("Add a member", "Email address", "Role", "Add"), (model.AddHeading, model.EmailLabel, model.RoleLabel, model.AddLabel));
        Assert.Equal("Adding someone sends no invitation.", model.AddNote);
        Assert.Equal(MemberRoleView.Client, model.NewRole?.Role);
        Assert.Equal(("The workspace's name", "Now", "Example Dental", "New name"), (model.RenameHeading, model.CurrentLabel, model.CurrentName, model.NewNameLabel));
        Assert.Equal("Rename the workspace", model.RenameLabel);
    }

    [Fact]
    public void AClientReadsTheMembersAndRenames()
    {
        var model = new MembersViewModel();
        model.Show(View(
            members: [Row("founder@example.com", MemberRoleView.Agency, canChange: false)],
            readOnly: "Only an agency member can add members.",
            rename: Rename()));
        Assert.True(model.HasReadOnlyNote);
        Assert.False(model.CanManage);
        Assert.False(model.Members[0].CanChange);
        Assert.True(model.CanRenameHere);
    }

    [Fact]
    public void LoadingAndAFailedRead()
    {
        var model = new MembersViewModel();
        model.Show(View(status: new SectionStatus.Loading(), members: []));
        Assert.True(model.IsLoading);
        model.Show(View(status: new SectionStatus.Failed("Could not load the members", V.Failure("Offline.", retryable: true)), members: []));
        Assert.True(model.HasStatus && model.CanRetry);
        Assert.Equal(("Could not load the members", "Offline."), (model.StatusTitle, model.StatusBody));
    }

    [Fact]
    public void AddingSendsWhatIsTypedAndTheCoreJudgesIt()
    {
        var model = new MembersViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View(add: Add()));
        Assert.Empty(sink.Sent);

        model.Email = "not an address";
        model.NewRole = model.Roles[2];
        model.AddCommand.Execute(null);
        model.Show(View(add: Add("not an address", rejected: "That does not look like an email address.")));
        Assert.True(model.HasAddRejected);
        Assert.Equal("not an address", model.Email);

        model.Show(View(add: Add(editable: false), changing: true));
        Assert.True(model.Changing);
        Assert.False(model.AddCommand.CanExecute(null));

        // Added: the core empties the box, which the box did not send.
        model.Show(View(add: Add(""), notice: new SaveNoticeView("Saved.", true)));
        Assert.Equal(string.Empty, model.Email);
        Assert.True(model.HasNotice && model.NoticeSaved);
        Assert.Equal(
            [
                new UiEvent.Members(new MembersAction.EditEmail("not an address")),
                new UiEvent.Members(new MembersAction.SetRole(MemberRoleView.Viewer)),
                new UiEvent.Members(new MembersAction.Add()),
            ],
            sink.Sent);
    }

    [Fact]
    public void ARoleChangeIsSentAndARefusalShowsTheStoredRoleAgain()
    {
        var model = new MembersViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View());
        var founder = model.Members[0];

        founder.SelectedRole = model.Roles[0];
        Assert.Empty(sink.Sent);
        founder.SelectedRole = model.Roles[1];
        Assert.Equal([new UiEvent.Members(new MembersAction.ChangeRole("founder@example.com", MemberRoleView.Client))], sink.Sent);

        model.Show(View(notice: new SaveNoticeView("Cannot demote the last agency member.", false)));
        Assert.Same(founder, model.Members[0]);
        Assert.Equal(MemberRoleView.Agency, founder.SelectedRole?.Role);
        Assert.False(model.NoticeSaved);
        Assert.Single(sink.Sent);

        // A changed list is drawn afresh.
        model.Show(View(members: [Row("founder@example.com", MemberRoleView.Client)]));
        Assert.NotSame(founder, model.Members[0]);
    }

    [Fact]
    public void RemovingAsksAndTheAnswerGoesToTheCore()
    {
        var model = new MembersViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View(members: [Row("someone@example.com", null)]));
        Assert.Null(model.Members[0].SelectedRole);

        model.Members[0].RemoveCommand.Execute(null);
        var question = new RemoveQuestionView("someone@example.com", "Remove this member?", "someone@example.com loses access.", "Remove", "Cancel");
        model.Show(View(question: question));
        Assert.Same(question, model.Question);
        model.Answer(false);
        model.Answer(true);
        model.Show(View());
        Assert.Null(model.Question);
        Assert.Equal(
            [
                new UiEvent.Members(new MembersAction.AskRemove("someone@example.com")),
                new UiEvent.Members(new MembersAction.CancelRemove()),
                new UiEvent.Members(new MembersAction.ConfirmRemove()),
            ],
            sink.Sent);
    }

    [Fact]
    public void RenamingTheNoticeAndRetry()
    {
        var model = new MembersViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View(rename: Rename()));
        model.NewName = "New Name";
        model.Show(View(rename: Rename(newName: "New Name", canRename: true)));
        Assert.Equal("New Name", model.NewName);
        model.RenameCommand.Execute(null);
        model.Show(View(rename: Rename(newName: "New Name", renaming: true)));
        Assert.True(model.Renaming);
        Assert.False(model.NameEditable);
        model.Show(View(rename: Rename(current: "New Name")));
        Assert.Equal(("New Name", string.Empty), (model.CurrentName, model.NewName));
        model.DismissNoticeCommand.Execute(null);
        model.RetryCommand.Execute(null);
        Assert.Equal(
            [
                new UiEvent.Members(new MembersAction.EditName("New Name")),
                new UiEvent.Members(new MembersAction.Rename()),
                new UiEvent.Members(new MembersAction.DismissNotice()),
                new UiEvent.Refresh(),
            ],
            sink.Sent);
    }

    [Fact]
    public void WithNoCoreNothingIsSent()
    {
        var model = new MembersViewModel();
        model.Send(new MembersAction.Open());
        model.Show(View(add: Add(), rename: Rename()));
        model.Email = "a@example.com";
        model.NewName = "N";
        model.NewRole = null;
        Assert.Equal("a@example.com", model.Email);
    }
}
