using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Settings.Messaging;

/// <summary>
/// The workspace's messaging (carrier) accounts, copied from the core's
/// <see cref="MessagingView"/>: the accounts and the default sender, each
/// channel's sender, the numbers Distronode holds, the owner's mobile number,
/// and the form that adds or edits an account and checks its keys.
/// </summary>
/// <remarks>
/// A key the person types goes to the core as it is typed and stays in its
/// box; the core never sends one back, only whether a box is filled, and this
/// model never writes one into a box: a secret box is only ever emptied, when
/// the core has dropped what was typed (a change of carrier, the form closed).
/// Nothing here logs or formats a key.
/// </remarks>
public sealed partial class MessagingViewModel : ObservableObject
{
    private readonly TextEcho _labelEcho = new();
    private readonly TextEcho _numbersEcho = new();
    private readonly TextEcho _ownerEcho = new();
    private PageContext? _context;
    private bool _writing;
    private MessagingCarrier[] _carriers = [];
    private MessagingSource[] _sources = [];

    /// <summary>The accounts.</summary>
    public ObservableCollection<MessagingAccountItem> Accounts { get; } = [];

    /// <summary>Each channel's sender.</summary>
    public ObservableCollection<ChannelSenderItem> Channels { get; } = [];

    /// <summary>The form's carriers, by name.</summary>
    public ObservableCollection<string> CarrierLabels { get; } = [];

    /// <summary>The form's sources, by what they read.</summary>
    public ObservableCollection<string> SourceLabels { get; } = [];

    /// <summary>The form's key boxes, in the carrier's order.</summary>
    public ObservableCollection<KeyItem> Keys { get; } = [];

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Whether the accounts are being read.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Whether the accounts are read.</summary>
    [ObservableProperty]
    public partial bool IsReady { get; set; }

    /// <summary>Whether the read failed (a status instead of the list).</summary>
    [ObservableProperty]
    public partial bool HasStatus { get; set; }

    /// <summary>The failure's heading.</summary>
    [ObservableProperty]
    public partial string StatusTitle { get; set; } = string.Empty;

    /// <summary>Why.</summary>
    [ObservableProperty]
    public partial string StatusBody { get; set; } = string.Empty;

    /// <summary>Whether "Try again" is offered.</summary>
    [ObservableProperty]
    public partial bool CanRetry { get; set; }

    /// <summary>For a viewer: why nothing can be changed, or empty.</summary>
    [ObservableProperty]
    public partial string ViewerNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ViewerNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasViewerNote { get; set; }

    /// <summary>Whether the last change left a notice.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>"Saved.", or why the change failed.</summary>
    [ObservableProperty]
    public partial string NoticeText { get; set; } = string.Empty;

    /// <summary>Whether the notice says it saved.</summary>
    [ObservableProperty]
    public partial bool NoticeSaved { get; set; }

    /// <summary>Whether the notice says it failed.</summary>
    [ObservableProperty]
    public partial bool NoticeFailed { get; set; }

    /// <summary>Whether a change or a check is on its way.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Whether changes can be made now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool Editable { get; set; }

    /// <summary>Whether "Add an account" shows.</summary>
    [ObservableProperty]
    public partial bool OffersAdd { get; set; }

    /// <summary>Whether no carrier is connected.</summary>
    [ObservableProperty]
    public partial bool HasEmpty { get; set; }

    /// <summary>The empty state's heading.</summary>
    [ObservableProperty]
    public partial string EmptyTitle { get; set; } = string.Empty;

    /// <summary>The empty state's text.</summary>
    [ObservableProperty]
    public partial string EmptyBody { get; set; } = string.Empty;

    /// <summary>Whether Distronode holds numbers for the workspace.</summary>
    [ObservableProperty]
    public partial bool HasManaged { get; set; }

    /// <summary>Their heading.</summary>
    [ObservableProperty]
    public partial string ManagedHeading { get; set; } = string.Empty;

    /// <summary>What they are.</summary>
    [ObservableProperty]
    public partial string ManagedBody { get; set; } = string.Empty;

    /// <summary>Their carrier and numbers, on one line.</summary>
    [ObservableProperty]
    public partial string ManagedLine { get; set; } = string.Empty;

    /// <summary>Whether the channels' senders show.</summary>
    [ObservableProperty]
    public partial bool HasChannels { get; set; }

    /// <summary>Whether the senders can be chosen (else read only).</summary>
    [ObservableProperty]
    public partial bool ChannelsEditable { get; set; }

    /// <summary>Whether the senders are shown read only.</summary>
    [ObservableProperty]
    public partial bool ChannelsReadOnly { get; set; }

    /// <summary>Whether the owner's mobile number box shows.</summary>
    [ObservableProperty]
    public partial bool HasCreator { get; set; }

    /// <summary>Its heading.</summary>
    [ObservableProperty]
    public partial string CreatorHeading { get; set; } = string.Empty;

    /// <summary>What it is for.</summary>
    [ObservableProperty]
    public partial string CreatorHelp { get; set; } = string.Empty;

    /// <summary>The owner's mobile number, as typed (the saved one is never shown).</summary>
    [ObservableProperty]
    public partial string OwnerNumber { get; set; } = string.Empty;

    /// <summary>Whether "Save number" works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveOwnerNumberCommand))]
    public partial bool CanSaveOwnerNumber { get; set; }

    /// <summary>Whether the number is being saved.</summary>
    [ObservableProperty]
    public partial bool SavingOwnerNumber { get; set; }

    /// <summary>Whether the question before removing an account shows.</summary>
    [ObservableProperty]
    public partial bool Confirming { get; set; }

    /// <summary>Its heading.</summary>
    [ObservableProperty]
    public partial string ConfirmTitle { get; set; } = string.Empty;

    /// <summary>What removing does.</summary>
    [ObservableProperty]
    public partial string ConfirmBody { get; set; } = string.Empty;

    /// <summary>The confirming button's label.</summary>
    [ObservableProperty]
    public partial string ConfirmAction { get; set; } = string.Empty;

    /// <summary>Whether the form is open.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool FormOpen { get; set; }

    /// <summary>The form's heading.</summary>
    [ObservableProperty]
    public partial string FormTitle { get; set; } = string.Empty;

    /// <summary>Which of <see cref="CarrierLabels"/> is chosen.</summary>
    [ObservableProperty]
    public partial int CarrierIndex { get; set; } = -1;

    /// <summary>Which of <see cref="SourceLabels"/> is chosen.</summary>
    [ObservableProperty]
    public partial int SourceIndex { get; set; } = -1;

    /// <summary>The account's name, as typed.</summary>
    [ObservableProperty]
    public partial string AccountName { get; set; } = string.Empty;

    /// <summary>The line under the key boxes.</summary>
    [ObservableProperty]
    public partial string KeysNote { get; set; } = string.Empty;

    /// <summary>The warning after changing an existing account's carrier, or empty.</summary>
    [ObservableProperty]
    public partial string CarrierSwitch { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="CarrierSwitch"/>.</summary>
    [ObservableProperty]
    public partial bool HasCarrierSwitch { get; set; }

    /// <summary>The numbers, one per line, as typed or read.</summary>
    [ObservableProperty]
    public partial string PhoneNumbers { get; set; } = string.Empty;

    /// <summary>What the numbers box does.</summary>
    [ObservableProperty]
    public partial string NumbersHelp { get; set; } = string.Empty;

    /// <summary>Whether to make it the default sender.</summary>
    [ObservableProperty]
    public partial bool MakeDefault { get; set; }

    /// <summary>Whether the form can be changed.</summary>
    [ObservableProperty]
    public partial bool FormEditable { get; set; }

    /// <summary>Whether "Save" works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool CanSaveForm { get; set; }

    /// <summary>Whether "Check these keys" works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckKeysCommand))]
    public partial bool CanCheck { get; set; }

    /// <summary>Why the check is not offered yet, or empty.</summary>
    [ObservableProperty]
    public partial string CheckHint { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="CheckHint"/>.</summary>
    [ObservableProperty]
    public partial bool HasCheckHint { get; set; }

    /// <summary>What the last check came to, or empty.</summary>
    [ObservableProperty]
    public partial string CheckResult { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="CheckResult"/>.</summary>
    [ObservableProperty]
    public partial bool HasCheckResult { get; set; }

    /// <summary>How the last check went, or null.</summary>
    [ObservableProperty]
    public partial KeyCheckOutcome? CheckOutcome { get; set; }

    /// <summary>Whether the save is on its way.</summary>
    [ObservableProperty]
    public partial bool Saving { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(MessagingView view)
    {
        Title = view.Title;
        ShowStatus(view.Status);
        ViewerNote = view.ViewerNote ?? string.Empty;
        HasViewerNote = view.ViewerNote is not null;
        HasNotice = view.Notice is not null;
        NoticeText = view.Notice?.Message ?? string.Empty;
        NoticeSaved = view.Notice?.Saved ?? false;
        NoticeFailed = view.Notice is { Saved: false };
        Busy = view.Busy;
        Editable = view.Editable;
        OffersAdd = view.OffersAdd;
        HasEmpty = view.Empty is not null;
        EmptyTitle = view.Empty?.Title ?? string.Empty;
        EmptyBody = view.Empty?.Body ?? string.Empty;
        Display.Sync(Accounts, [.. view.Accounts.Select(account => MessagingAccountItem.From(account, view.Editable))]);
        HasManaged = view.Managed is not null;
        ManagedHeading = view.Managed?.Heading ?? string.Empty;
        ManagedBody = view.Managed?.Body ?? string.Empty;
        ManagedLine = view.Managed is null ? string.Empty : view.Managed.Carrier + " \u00B7 " + view.Managed.Numbers;
        Display.Sync(Channels, [.. view.Channels.Select(ChannelSenderItem.From)]);
        HasChannels = view.Channels.Length > 0;
        ChannelsEditable = view.ChannelsEditable;
        ChannelsReadOnly = !view.ChannelsEditable;
        ShowCreator(view.Creator);
        Confirming = view.Confirm is not null;
        ConfirmTitle = view.Confirm?.Title ?? string.Empty;
        ConfirmBody = view.Confirm?.Body ?? string.Empty;
        ConfirmAction = view.Confirm?.Action ?? string.Empty;
        ShowForm(view.Form);
    }

    private void ShowStatus(SectionStatus status)
    {
        IsLoading = status is SectionStatus.Loading;
        IsReady = status is SectionStatus.Ready;
        var failed = status as SectionStatus.Failed;
        HasStatus = failed is not null;
        StatusTitle = failed?.Title ?? string.Empty;
        StatusBody = Display.Failure(failed?.Failure);
        CanRetry = failed?.Failure.Retryable ?? false;
    }

    private void ShowCreator(CreatorCellView? creator)
    {
        HasCreator = creator is not null;
        CreatorHeading = creator?.Heading ?? string.Empty;
        CreatorHelp = creator?.Help ?? string.Empty;
        var number = creator?.Number ?? string.Empty;
        if (_ownerEcho.Write(number, OwnerNumber))
        {
            Write(() => OwnerNumber = number);
        }
        CanSaveOwnerNumber = creator?.CanSave ?? false;
        SavingOwnerNumber = creator?.Saving ?? false;
    }

    private void ShowForm(MessagingFormView? form)
    {
        FormOpen = form is not null;
        FormTitle = form?.Title ?? string.Empty;
        Write(() =>
        {
            _carriers = form is null ? [] : [.. form.Carriers.Select(choice => choice.Carrier)];
            string[] carrierLabels = form is null ? [] : [.. form.Carriers.Select(choice => choice.Label)];
            Display.Sync(CarrierLabels, carrierLabels);
            CarrierIndex = form is null ? -1 : Array.IndexOf(_carriers, form.Carrier);
            _sources = form is null ? [] : [.. form.Sources.Select(choice => choice.Source)];
            string[] sourceLabels = form is null ? [] : [.. form.Sources.Select(choice => choice.Label)];
            Display.Sync(SourceLabels, sourceLabels);
            SourceIndex = form is null ? -1 : Array.IndexOf(_sources, form.Source);
            var name = form?.Label ?? string.Empty;
            if (_labelEcho.Write(name, AccountName))
            {
                AccountName = name;
            }
            var numbers = form?.PhoneNumbers ?? string.Empty;
            if (_numbersEcho.Write(numbers, PhoneNumbers))
            {
                PhoneNumbers = numbers;
            }
            MakeDefault = form?.MakeDefault ?? false;
        });
        ShowKeys(form?.Keys ?? []);
        KeysNote = form?.KeysNote ?? string.Empty;
        CarrierSwitch = form?.CarrierSwitch ?? string.Empty;
        HasCarrierSwitch = form?.CarrierSwitch is not null;
        NumbersHelp = form?.NumbersHelp ?? string.Empty;
        FormEditable = form?.Editable ?? false;
        CanSaveForm = form?.CanSave ?? false;
        CanCheck = form?.CanTest ?? false;
        CheckHint = form?.TestHint ?? string.Empty;
        HasCheckHint = form?.TestHint is not null;
        CheckResult = form?.Test?.Message ?? string.Empty;
        HasCheckResult = form?.Test is not null;
        CheckOutcome = form?.Test?.Outcome;
        Saving = form?.Saving ?? false;
    }

    /// <summary>
    /// The key boxes: the same boxes are kept while the carrier's boxes are the
    /// same, so what is typed stays; a box the core has emptied is emptied.
    /// </summary>
    private void ShowKeys(KeyFieldView[] keys)
    {
        var same = Keys.Count == keys.Length && Keys.Zip(keys).All(pair => pair.First.Field == pair.Second.Field);
        if (!same)
        {
            Keys.Clear();
            foreach (var key in keys)
            {
                Keys.Add(new KeyItem(key.Field, key.Label, key.Secret, OnKeyTyped));
            }
        }
        for (var i = 0; i < keys.Length; i++)
        {
            Keys[i].Show(keys[i]);
        }
    }

    private void OnKeyTyped(KeyField field, string value) => Send(new MessagingAction.EditKey(field, value));

    private void Write(Action write)
    {
        var was = _writing;
        _writing = true;
        try
        {
            write();
        }
        finally
        {
            _writing = was;
        }
    }

    partial void OnCarrierIndexChanged(int value)
    {
        if (!_writing && value >= 0 && value < _carriers.Length)
        {
            Send(new MessagingAction.SetCarrier(_carriers[value]));
        }
    }

    partial void OnSourceIndexChanged(int value)
    {
        if (!_writing && value >= 0 && value < _sources.Length)
        {
            Send(new MessagingAction.SetSource(_sources[value]));
        }
    }

    partial void OnAccountNameChanged(string value)
    {
        if (!_writing)
        {
            _labelEcho.Typed(value);
            Send(new MessagingAction.EditLabel(value));
        }
    }

    partial void OnPhoneNumbersChanged(string value)
    {
        if (!_writing)
        {
            _numbersEcho.Typed(value);
            Send(new MessagingAction.EditNumbers(value));
        }
    }

    partial void OnMakeDefaultChanged(bool value)
    {
        if (!_writing)
        {
            Send(new MessagingAction.SetMakeDefault(value));
        }
    }

    partial void OnOwnerNumberChanged(string value)
    {
        if (!_writing)
        {
            _ownerEcho.Typed(value);
            Send(new MessagingAction.EditOwnerNumber(value));
        }
    }

    internal void EditAccount(MessagingAccountItem account) => Send(new MessagingAction.StartEdit(account.Id));

    internal void MakeAccountDefault(MessagingAccountItem account) => Send(new MessagingAction.MakeDefault(account.Id));

    internal void RemoveAccount(MessagingAccountItem account) => Send(new MessagingAction.AskRemove(account.Id));

    /// <summary>A channel's picker chose its <paramref name="index"/>th account.</summary>
    internal void ChooseSender(ChannelSenderItem channel, int index)
    {
        if (index >= 0 && index < channel.AccountIds.Count && channel.AccountIds[index] != channel.SelectedId)
        {
            Send(new MessagingAction.SetChannelSender(channel.Channel, channel.AccountIds[index]));
        }
    }

    [RelayCommand]
    private void Retry() => _context?.Send(new UiEvent.Refresh());

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add() => Send(new MessagingAction.StartAdd());

    private bool CanAdd() => Editable && !FormOpen;

    [RelayCommand(CanExecute = nameof(CanSaveForm))]
    private void Save() => Send(new MessagingAction.Save());

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private void CheckKeys() => Send(new MessagingAction.CheckKeys());

    [RelayCommand]
    private void CloseForm() => Send(new MessagingAction.CloseForm());

    [RelayCommand]
    private void ConfirmRemove() => Send(new MessagingAction.ConfirmRemove());

    [RelayCommand]
    private void CancelRemove() => Send(new MessagingAction.CancelRemove());

    [RelayCommand(CanExecute = nameof(CanSaveOwnerNumber))]
    private void SaveOwnerNumber() => Send(new MessagingAction.SaveOwnerNumber());

    [RelayCommand]
    private void DismissNotice() => Send(new MessagingAction.DismissNotice());

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(MessagingAction action) => _context?.Send(new UiEvent.Messaging(action));
}

/// <summary>One carrier account.</summary>
/// <param name="Id">The account.</param>
/// <param name="Label">Its name.</param>
/// <param name="Line">Its carrier, whose account it is, and its numbers.</param>
/// <param name="IsDefault">Whether it is the default sender.</param>
/// <param name="OffersMakeDefault">Whether "Make default" shows.</param>
/// <param name="OffersEdit">Whether "Edit" shows.</param>
/// <param name="OffersRemove">Whether "Remove" shows.</param>
/// <param name="Enabled">Whether its buttons work now.</param>
public sealed record MessagingAccountItem(
    string Id,
    string Label,
    string Line,
    bool IsDefault,
    bool OffersMakeDefault,
    bool OffersEdit,
    bool OffersRemove,
    bool Enabled)
{
    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => IsDefault ? $"{Label}, default sender, {Line}" : $"{Label}, {Line}";

    internal static MessagingAccountItem From(MessagingAccountView account, bool enabled) => new(
        account.Id,
        account.Label,
        account.Line,
        account.IsDefault,
        account.OffersMakeDefault,
        account.OffersEdit,
        account.OffersRemove,
        enabled);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>One channel's sender.</summary>
/// <param name="Channel">The channel.</param>
/// <param name="Label">Its name.</param>
/// <param name="AccountIds">The accounts to choose from, by id.</param>
/// <param name="AccountLabels">The same, by name.</param>
/// <param name="SelectedId">The account chosen now, or empty for none of its own.</param>
/// <param name="SelectedLabel">What the chosen sender reads.</param>
public sealed record ChannelSenderItem(
    SenderChannel Channel,
    string Label,
    IReadOnlyList<string> AccountIds,
    IReadOnlyList<string> AccountLabels,
    string SelectedId,
    string SelectedLabel)
{
    /// <summary>The picker's chosen index, or -1 when the channel has no sender of its own.</summary>
    public int SelectedIndex => AccountIds.ToList().IndexOf(SelectedId);

    /// <summary>The read-only line: the channel and its sender.</summary>
    public string Line => Label + ": " + SelectedLabel;

    internal static ChannelSenderItem From(ChannelSenderView channel) => new(
        channel.Channel,
        channel.Label,
        [.. channel.Picker.Choices.Select(choice => choice.Value)],
        [.. channel.Picker.Choices.Select(choice => choice.Label)],
        channel.Picker.Selected,
        channel.Picker.SelectedLabel);

    /// <inheritdoc/>
    public bool Equals(ChannelSenderItem? other) =>
        other is not null
        && Channel == other.Channel
        && Label == other.Label
        && AccountIds.SequenceEqual(other.AccountIds)
        && AccountLabels.SequenceEqual(other.AccountLabels)
        && SelectedId == other.SelectedId
        && SelectedLabel == other.SelectedLabel;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Channel, SelectedId, SelectedLabel);
}

/// <summary>
/// One key box of the form. A secret one holds what the person typed, which is
/// never written into it from the core; it is only emptied when the core says
/// it is not filled any more.
/// </summary>
public sealed partial class KeyItem : ObservableObject
{
    private readonly Action<KeyField, string> _typed;
    private readonly TextEcho _echo = new();
    private bool _writing;

    internal KeyItem(KeyField field, string label, bool secret, Action<KeyField, string> typed)
    {
        Field = field;
        Label = label;
        Secret = secret;
        _typed = typed;
    }

    /// <summary>The box.</summary>
    public KeyField Field { get; }

    /// <summary>Its label.</summary>
    public string Label { get; }

    /// <summary>Whether it is a secret (a password box).</summary>
    public bool Secret { get; }

    /// <summary>Whether it is not a secret (a text box).</summary>
    public bool Plain => !Secret;

    /// <summary>What is in the box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecretValue), nameof(PlainValue))]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>The password box's text: empty for a box that is not a secret.</summary>
    public string SecretValue
    {
        get => Secret ? Text : string.Empty;
        set
        {
            if (Secret)
            {
                Text = value;
            }
        }
    }

    /// <summary>The text box's text: empty for a secret, which only a password box holds.</summary>
    public string PlainValue
    {
        get => Secret ? string.Empty : Text;
        set
        {
            if (!Secret)
            {
                Text = value;
            }
        }
    }

    internal void Show(KeyFieldView view)
    {
        if (Secret)
        {
            // Only ever emptied: the core never sends a secret back.
            if (!view.Filled && Text.Length > 0)
            {
                SetQuietly(string.Empty);
            }
            return;
        }
        if (_echo.Write(view.Value, Text))
        {
            SetQuietly(view.Value);
        }
    }

    private void SetQuietly(string text)
    {
        _writing = true;
        try
        {
            Text = text;
        }
        finally
        {
            _writing = false;
        }
    }

    partial void OnTextChanged(string value)
    {
        if (_writing)
        {
            return;
        }
        if (!Secret)
        {
            _echo.Typed(value);
        }
        _typed(Field, value);
    }

    /// <inheritdoc/>
    public override string ToString() => Label;
}
