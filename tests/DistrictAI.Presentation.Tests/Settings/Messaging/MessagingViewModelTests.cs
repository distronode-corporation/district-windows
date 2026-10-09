using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Messaging;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.Messaging;

public sealed class MessagingViewModelTests
{
    private static readonly CarrierChoiceView[] _carriers =
        [new(MessagingCarrier.Twilio, "Twilio"), new(MessagingCarrier.Sinch, "Sinch"), new(MessagingCarrier.Telnyx, "Telnyx")];

    private static readonly SourceChoiceView[] _sources =
        [new(MessagingSource.Own, "Your own carrier account"), new(MessagingSource.Managed, "Managed by Distronode")];

    private static KeyFieldView Key(KeyField field, string label, bool secret = true, bool filled = false, string value = "") =>
        new(field, label, secret, value, filled);

    private static MessagingFormView Form(
        MessagingCarrier carrier = MessagingCarrier.Twilio,
        KeyFieldView[]? keys = null,
        string label = "",
        bool canSave = false,
        bool canTest = false,
        KeyCheckView? test = null,
        bool editable = true,
        string? carrierSwitch = null) =>
        new(
            Title: "Add a carrier account",
            Carriers: _carriers,
            Carrier: carrier,
            Sources: _sources,
            Source: MessagingSource.Own,
            Label: label,
            Keys: keys ?? [Key(KeyField.AccountSid, "Account SID"), Key(KeyField.AuthToken, "Auth token")],
            KeysNote: "Every key the carrier needs, typed in full.",
            CarrierSwitch: carrierSwitch,
            PhoneNumbers: string.Empty,
            NumbersHelp: "Leave this alone to keep the numbers as they are.",
            MakeDefault: false,
            Editable: editable,
            CanSave: canSave,
            CanTest: canTest,
            TestHint: canTest ? null : "Fill in every key to check them.",
            Test: test,
            Saving: false);

    private static readonly MessagingAccountView[] _accounts =
    [
        new("acct-twilio", "Twilio (main)", "Twilio · Your own carrier account · +1 416 555 0111", false, true, true, true),
        new("acct-telnyx", "Telnyx (overflow)", "Telnyx · Managed by Distronode · +1 416 555 0113", true, false, true, true),
    ];

    private static readonly ChannelSenderView[] _channels =
    [
        new(SenderChannel.Sms, "SMS", new PickerView([new("acct-twilio", "Twilio (main)"), new("acct-telnyx", "Telnyx (overflow)")], "acct-twilio", "Twilio (main)")),
        new(SenderChannel.Voice, "Voice", new PickerView([new("acct-twilio", "Twilio (main)"), new("acct-telnyx", "Telnyx (overflow)")], string.Empty, "The default account")),
    ];

    private static MessagingView View(
        SectionStatus? status = null,
        bool viewer = false,
        SaveNoticeView? notice = null,
        bool busy = false,
        MessagingConfirmView? confirm = null,
        MessagingFormView? form = null,
        CreatorCellView? creator = null,
        EmptyView? empty = null) =>
        new(
            Title: "Messaging accounts",
            Status: status ?? new SectionStatus.Ready(),
            ViewerNote: viewer ? "You have read-only access to this workspace." : null,
            Notice: notice,
            Busy: busy,
            Editable: !busy && !viewer,
            OffersAdd: !viewer,
            Empty: empty,
            Accounts: empty is null ? _accounts : [],
            Managed: new ManagedNumbersView("Numbers held by Distronode", "Bought for this workspace.", "Twilio", "+1 416 555 0190"),
            Channels: empty is null ? _channels : [],
            ChannelsEditable: !viewer,
            Creator: viewer ? null : creator ?? new CreatorCellView("The owner's mobile number", "Where the receptionist reaches the owner.", string.Empty, false, false),
            Confirm: confirm,
            Form: form);

    private static (MessagingViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new MessagingViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    private static UiEvent.Messaging Sent(MessagingAction action) => new(action);

    [Fact]
    public void TheAccountsChannelsAndManagedNumbersShow()
    {
        var (model, sink) = Attached();
        model.Show(View());
        Assert.True(model.IsReady);
        Assert.Equal("Messaging accounts", model.Title);
        Assert.Equal(2, model.Accounts.Count);
        Assert.True(model.Accounts[1].IsDefault);
        Assert.Contains("default sender", model.Accounts[1].AccessibleName, StringComparison.Ordinal);
        Assert.Equal(model.Accounts[0].AccessibleName, model.Accounts[0].ToString());
        Assert.True(model.Accounts[0].Enabled);
        Assert.True(model.HasManaged);
        Assert.Equal("Twilio · +1 416 555 0190", model.ManagedLine);
        Assert.True(model.HasChannels);
        Assert.True(model.ChannelsEditable);
        Assert.Equal(0, model.Channels[0].SelectedIndex);
        Assert.Equal(-1, model.Channels[1].SelectedIndex);
        Assert.Equal("Voice: The default account", model.Channels[1].Line);
        Assert.True(model.HasCreator);
        Assert.True(model.AddCommand.CanExecute(null));

        model.MakeAccountDefault(model.Accounts[0]);
        model.EditAccount(model.Accounts[0]);
        model.RemoveAccount(model.Accounts[0]);
        model.ChooseSender(model.Channels[0], 1);
        model.ChooseSender(model.Channels[0], 0);
        model.ChooseSender(model.Channels[0], 9);
        model.AddCommand.Execute(null);
        Assert.Equal(
            [
                Sent(new MessagingAction.MakeDefault("acct-twilio")),
                Sent(new MessagingAction.StartEdit("acct-twilio")),
                Sent(new MessagingAction.AskRemove("acct-twilio")),
                Sent(new MessagingAction.SetChannelSender(SenderChannel.Sms, "acct-telnyx")),
                Sent(new MessagingAction.StartAdd()),
            ],
            sink.Sent);
    }

    [Fact]
    public void AViewerReadsOnly()
    {
        var (model, _) = Attached();
        model.Show(View(viewer: true));
        Assert.True(model.HasViewerNote);
        Assert.False(model.OffersAdd);
        Assert.False(model.ChannelsEditable);
        Assert.True(model.ChannelsReadOnly);
        Assert.False(model.HasCreator);
        Assert.False(model.AddCommand.CanExecute(null));
    }

    [Fact]
    public void LoadingFailedAndEmptySaySo()
    {
        var (model, sink) = Attached();
        model.Show(View(status: new SectionStatus.Loading()));
        Assert.True(model.IsLoading);
        model.Show(View(status: new SectionStatus.Failed("Could not load the carrier accounts", new FailureView("Offline.", null, true))));
        Assert.True(model.HasStatus);
        Assert.True(model.CanRetry);
        Assert.Equal("Offline.", model.StatusBody);
        model.RetryCommand.Execute(null);
        Assert.Equal([new UiEvent.Refresh()], sink.Sent);
        model.Show(View(empty: new EmptyView("No carrier connected", "This workspace has no carrier account.")));
        Assert.True(model.HasEmpty);
        Assert.False(model.HasChannels);
    }

    [Fact]
    public void TypedKeysGoToTheCoreAndAreNeverWrittenBack()
    {
        var (model, sink) = Attached();
        model.Show(View(form: Form()));
        Assert.True(model.FormOpen);
        Assert.False(model.AddCommand.CanExecute(null));
        Assert.Equal(["Twilio", "Sinch", "Telnyx"], model.CarrierLabels);
        Assert.Equal(0, model.CarrierIndex);
        Assert.Equal(1, model.SourceLabels.Count - 1);
        Assert.Equal(2, model.Keys.Count);
        var token = model.Keys[1];
        Assert.True(token.Secret);
        Assert.False(token.Plain);

        token.SecretValue = "typed-token";
        token.PlainValue = "ignored for a secret";
        Assert.Equal("typed-token", token.SecretValue);
        Assert.Equal(string.Empty, token.PlainValue);
        Assert.Equal([Sent(new MessagingAction.EditKey(KeyField.AuthToken, "typed-token"))], sink.Sent);

        // The core says it is filled: the same box keeps what was typed.
        model.Show(View(form: Form(keys: [Key(KeyField.AccountSid, "Account SID"), Key(KeyField.AuthToken, "Auth token", filled: true)])));
        Assert.Same(token, model.Keys[1]);
        Assert.Equal("typed-token", token.Text);

        // The core dropped it (the form closed and opened again): the box empties, and nothing is sent.
        model.Show(View(form: Form()));
        Assert.Equal(string.Empty, token.Text);
        Assert.Single(sink.Sent);
    }

    [Fact]
    public void AnotherCarriersBoxesReplaceTheOldOnes()
    {
        var (model, sink) = Attached();
        model.Show(View(form: Form()));
        model.CarrierIndex = 1;
        model.Show(View(form: Form(MessagingCarrier.Sinch, [Key(KeyField.ProjectId, "Project ID", secret: false), Key(KeyField.KeySecret, "Key secret")], carrierSwitch: "Changing the carrier drops the saved keys.")));
        Assert.Equal(2, model.Keys.Count);
        Assert.Equal(KeyField.ProjectId, model.Keys[0].Field);
        Assert.True(model.Keys[0].Plain);
        Assert.True(model.HasCarrierSwitch);

        // A box that is not a secret shows what the core holds, and what is typed goes back.
        model.Keys[0].PlainValue = "proj-1";
        model.Keys[0].SecretValue = "ignored";
        model.Show(View(form: Form(MessagingCarrier.Sinch, [Key(KeyField.ProjectId, "Project ID", secret: false, filled: true, value: "proj-1"), Key(KeyField.KeySecret, "Key secret")])));
        Assert.Equal("proj-1", model.Keys[0].PlainValue);
        Assert.Equal("Project ID", model.Keys[0].ToString());
        Assert.Equal(
            [
                Sent(new MessagingAction.SetCarrier(MessagingCarrier.Sinch)),
                Sent(new MessagingAction.EditKey(KeyField.ProjectId, "proj-1")),
            ],
            sink.Sent);
    }

    [Fact]
    public void TheFormsOtherFieldsAndButtonsSendTheirActions()
    {
        var (model, sink) = Attached();
        model.Show(View(form: Form(canSave: true, canTest: true, test: new KeyCheckView("The carrier accepted these keys.", KeyCheckOutcome.Passed))));
        Assert.True(model.HasCheckResult);
        Assert.Equal(KeyCheckOutcome.Passed, model.CheckOutcome);
        Assert.False(model.HasCheckHint);
        model.SourceIndex = 1;
        model.AccountName = "Front desk";
        model.PhoneNumbers = "+14165550111";
        model.MakeDefault = true;
        model.CheckKeysCommand.Execute(null);
        model.SaveCommand.Execute(null);
        model.CloseFormCommand.Execute(null);
        model.CarrierIndex = 9;
        model.SourceIndex = -1;
        Assert.Equal(
            [
                Sent(new MessagingAction.SetSource(MessagingSource.Managed)),
                Sent(new MessagingAction.EditLabel("Front desk")),
                Sent(new MessagingAction.EditNumbers("+14165550111")),
                Sent(new MessagingAction.SetMakeDefault(true)),
                Sent(new MessagingAction.CheckKeys()),
                Sent(new MessagingAction.Save()),
                Sent(new MessagingAction.CloseForm()),
            ],
            sink.Sent);

        model.Show(View(busy: true, form: Form(label: "Front desk", editable: false)));
        Assert.False(model.FormEditable);
        Assert.False(model.SaveCommand.CanExecute(null));
        Assert.False(model.CheckKeysCommand.CanExecute(null));
        Assert.True(model.HasCheckHint);
        model.Show(View());
        Assert.False(model.FormOpen);
        Assert.Empty(model.Keys);
        Assert.Equal(-1, model.CarrierIndex);
    }

    [Fact]
    public void RemovingAsksFirstAndTheOwnersNumberIsTyped()
    {
        var (model, sink) = Attached();
        model.Show(View(confirm: new MessagingConfirmView("Remove this carrier account?", "It is removed.", "Remove and release")));
        Assert.True(model.Confirming);
        Assert.Equal("Remove and release", model.ConfirmAction);
        model.CancelRemoveCommand.Execute(null);
        model.ConfirmRemoveCommand.Execute(null);

        model.OwnerNumber = "+1 416 555 0144";
        model.Show(View(creator: new CreatorCellView("The owner's mobile number", "Help.", "+1 416 555 0144", true, false)));
        Assert.True(model.SaveOwnerNumberCommand.CanExecute(null));
        model.SaveOwnerNumberCommand.Execute(null);
        model.Show(View(notice: new SaveNoticeView("Saved.", true)));
        Assert.True(model.NoticeSaved);
        Assert.False(model.NoticeFailed);
        Assert.Equal(string.Empty, model.OwnerNumber);
        model.DismissNoticeCommand.Execute(null);
        model.Show(View(notice: new SaveNoticeView("The carrier could not be reached.", false)));
        Assert.True(model.NoticeFailed);
        Assert.Equal(
            [
                Sent(new MessagingAction.CancelRemove()),
                Sent(new MessagingAction.ConfirmRemove()),
                Sent(new MessagingAction.EditOwnerNumber("+1 416 555 0144")),
                Sent(new MessagingAction.SaveOwnerNumber()),
                Sent(new MessagingAction.DismissNotice()),
            ],
            sink.Sent);
    }

    [Fact]
    public void NothingIsSentBeforeAttaching()
    {
        var model = new MessagingViewModel();
        model.Show(View(form: Form()));
        model.Keys[0].SecretValue = "x";
        model.AccountName = "y";
        model.Send(new MessagingAction.Open());
        var (context, sink) = Pages.Context();
        model.Attach(context);
        Assert.Empty(sink.Sent);
    }
}
