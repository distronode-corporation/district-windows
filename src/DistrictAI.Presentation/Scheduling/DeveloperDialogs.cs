using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// Making an API key (the core's <see cref="KeyDialogView"/>): its name, then
/// the key, shown once, with Copy and Done. The create is the dialog's
/// confirm; once the key is made there is nothing left to confirm.
/// </summary>
/// <remarks>
/// The name is written once, as the dialog opens: from then on what the
/// member types is the name, and each change goes to the core. The key
/// arrives as UTF-8 bytes (no record's <c>ToString</c> prints it) and is held
/// here only while it is shown: it goes when the dialog closes.
/// </remarks>
public sealed partial class DeveloperKeyViewModel : ObservableObject
{
    private PageContext? _context;
    private bool _writing;

    /// <summary>The key dialog, with nothing shown yet.</summary>
    public DeveloperKeyViewModel()
    {
        Dialog = new AdminDialogViewModel(
            () => SchedulingDeveloperViewModel.Event(new DeveloperAction.CreateKey()),
            () => SchedulingDeveloperViewModel.Event(new DeveloperAction.CloseDialog()));
    }

    /// <summary>The dialog's title, create, way out and the create's failure.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>"Name".</summary>
    [ObservableProperty]
    public partial string NameLabel { get; set; } = string.Empty;

    /// <summary>What the name is for.</summary>
    [ObservableProperty]
    public partial string NameHint { get; set; } = string.Empty;

    /// <summary>The name, as typed.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Why the name cannot be sent, or empty.</summary>
    [ObservableProperty]
    public partial string NameError { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="NameError"/>.</summary>
    [ObservableProperty]
    public partial bool HasNameError { get; set; }

    /// <summary>Whether the name can be changed: nothing is on its way.</summary>
    [ObservableProperty]
    public partial bool Editable { get; set; }

    /// <summary>Whether the form shows: no key is made yet.</summary>
    [ObservableProperty]
    public partial bool ShowsForm { get; set; }

    /// <summary>The key made, shown once.</summary>
    public SecretShownViewModel Shown { get; } = new();

    internal void Attach(PageContext context)
    {
        _context = context;
        Dialog.Attach(context);
    }

    /// <summary>Shows <paramref name="view"/>, or closes the dialog when the core holds none.</summary>
    internal void Show(KeyDialogView? view)
    {
        var opening = view is not null && !Dialog.IsOpen;
        Dialog.Show(view?.Dialog);
        Shown.Show(view?.Minted);
        if (view is null)
        {
            return;
        }
        if (opening)
        {
            _writing = true;
            try
            {
                Name = view.Name;
            }
            finally
            {
                _writing = false;
            }
        }
        NameLabel = view.NameLabel;
        NameHint = view.NameHint;
        NameError = view.NameError ?? string.Empty;
        HasNameError = view.NameError is not null;
        Editable = view.Editable;
        ShowsForm = view.Minted is null;
    }

    /// <summary>The key was put on the clipboard, or Windows refused it: the core counts a copy that happened.</summary>
    internal void Copied(bool copied)
    {
        Shown.Copied(copied);
        if (copied)
        {
            _context?.Send(SchedulingDeveloperViewModel.Event(new DeveloperAction.KeyCopied()));
        }
    }

    partial void OnNameChanged(string value)
    {
        if (!_writing && Dialog.IsOpen)
        {
            _context?.Send(SchedulingDeveloperViewModel.Event(new DeveloperAction.EditKeyName(value)));
        }
    }
}

/// <summary>
/// Adding a webhook, or changing what one sends (the core's
/// <see cref="WebhookEditorView"/>): where it goes, typed for an add and fixed
/// once added; a switch per event and per part of a booking, a person's own
/// data marked; then, for a webhook just added, its signing secret, shown
/// once. The save is the dialog's confirm; what it came to shows in the
/// dialog.
/// </summary>
/// <remarks>
/// The address is written once, as the dialog opens, then is the member's.
/// The switches are built again from each snapshot: the core holds what is
/// chosen.
/// </remarks>
public sealed partial class WebhookEditorViewModel : ObservableObject
{
    private PageContext? _context;
    private bool _writing;

    /// <summary>The webhook form, with nothing shown yet.</summary>
    public WebhookEditorViewModel()
    {
        Dialog = new AdminDialogViewModel(
            () => SchedulingDeveloperViewModel.Event(new DeveloperAction.SaveWebhook()),
            () => SchedulingDeveloperViewModel.Event(new DeveloperAction.CloseDialog()));
    }

    /// <summary>The dialog's title, save, way out and the save's failure.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The events a webhook can send.</summary>
    public ObservableCollection<ToggleEntry> Events { get; } = [];

    /// <summary>The parts of a booking a delivery can carry.</summary>
    public ObservableCollection<ToggleEntry> Fields { get; } = [];

    /// <summary>The signing secret of a webhook just added, shown once.</summary>
    public SecretShownViewModel Shown { get; } = new();

    /// <summary>The labels and hints, or null while the dialog is closed.</summary>
    [ObservableProperty]
    public partial WebhookEditorView? Words { get; set; }

    /// <summary>Whether the form shows: no secret is shown.</summary>
    [ObservableProperty]
    public partial bool ShowsForm { get; set; }

    /// <summary>Whether this adds a webhook: its address can be typed.</summary>
    [ObservableProperty]
    public partial bool Creating { get; set; }

    /// <summary>Whether this changes one: its address is fixed.</summary>
    [ObservableProperty]
    public partial bool Changing { get; set; }

    /// <summary>The address, as typed (an add) or as stored.</summary>
    [ObservableProperty]
    public partial string Url { get; set; } = string.Empty;

    /// <summary>Why the address cannot be sent, or empty.</summary>
    [ObservableProperty]
    public partial string UrlError { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="UrlError"/>.</summary>
    [ObservableProperty]
    public partial bool HasUrlError { get; set; }

    /// <summary>The events a save stops, or empty.</summary>
    [ObservableProperty]
    public partial string Dropped { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Dropped"/>.</summary>
    [ObservableProperty]
    public partial bool HasDropped { get; set; }

    /// <summary>Why the events cannot be sent, or empty.</summary>
    [ObservableProperty]
    public partial string EventsError { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="EventsError"/>.</summary>
    [ObservableProperty]
    public partial bool HasEventsError { get; set; }

    /// <summary>Whether the form can be changed: nothing is on its way.</summary>
    [ObservableProperty]
    public partial bool Editable { get; set; }

    /// <summary>What the last save came to, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Dialog.Attach(context);
    }

    /// <summary>Shows <paramref name="view"/>, or closes the dialog when the core holds none.</summary>
    internal void Show(WebhookEditorView? view)
    {
        var opening = view is not null && !Dialog.IsOpen;
        Dialog.Show(view?.Dialog);
        Shown.Show(view?.Minted);
        Words = view;
        if (view is null)
        {
            Events.Clear();
            Fields.Clear();
            return;
        }
        if (opening)
        {
            _writing = true;
            try
            {
                Url = view.Url;
            }
            finally
            {
                _writing = false;
            }
        }
        ShowsForm = view.Minted is null;
        Creating = view.Creating;
        Changing = !view.Creating;
        UrlError = view.UrlError ?? string.Empty;
        HasUrlError = view.UrlError is not null;
        Dropped = view.Dropped ?? string.Empty;
        HasDropped = view.Dropped is not null;
        EventsError = view.EventsError ?? string.Empty;
        HasEventsError = view.EventsError is not null;
        Editable = view.Editable;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        Display.Sync(Events, [.. view.Events.Select(row => ToggleEntry.From(row, view.Editable))]);
        Display.Sync(Fields, [.. view.Fields.Select(row => ToggleEntry.From(row, view.Editable))]);
    }

    /// <summary>The event <paramref name="value"/>'s switch now says <paramref name="on"/>; what the core holds sends nothing.</summary>
    internal void SetEvent(string value, bool on)
    {
        if (Editable && Events.FirstOrDefault(entry => entry.Value == value) is { } entry && entry.On != on)
        {
            Send(new DeveloperAction.ToggleEvent(value));
        }
    }

    /// <summary>The field <paramref name="value"/>'s switch now says <paramref name="on"/>; what the core holds sends nothing.</summary>
    internal void SetField(string value, bool on)
    {
        if (Editable && Fields.FirstOrDefault(entry => entry.Value == value) is { } entry && entry.On != on)
        {
            Send(new DeveloperAction.ToggleField(value));
        }
    }

    /// <summary>The secret was put on the clipboard, or Windows refused it.</summary>
    internal void Copied(bool copied) => Shown.Copied(copied);

    partial void OnUrlChanged(string value)
    {
        if (!_writing && Dialog.IsOpen && Creating)
        {
            Send(new DeveloperAction.EditWebhookUrl(value));
        }
    }

    private void Send(DeveloperAction action) => _context?.Send(SchedulingDeveloperViewModel.Event(action));
}

/// <summary>
/// A value shown once (the core's <see cref="SecretView"/>): a key just made,
/// or a webhook's signing secret. The value is decoded here only while it
/// shows, for its box and its Copy, and goes when the core stops showing it;
/// this class's <c>ToString</c> is its type's name.
/// </summary>
public sealed partial class SecretShownViewModel : ObservableObject
{
    /// <summary>Whether a value shows.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    /// <summary>The value, while it shows; empty otherwise.</summary>
    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    /// <summary>That it is shown once.</summary>
    [ObservableProperty]
    public partial string Warning { get; set; } = string.Empty;

    /// <summary>Where it is used, or empty.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Note"/>.</summary>
    [ObservableProperty]
    public partial bool HasNote { get; set; }

    /// <summary>"Copy key", "Copied", or "Copy signing secret".</summary>
    [ObservableProperty]
    public partial string CopyLabel { get; set; } = string.Empty;

    /// <summary>Whether the last copy was refused by Windows.</summary>
    [ObservableProperty]
    public partial bool CopyFailed { get; set; }

    internal void Show(SecretView? view)
    {
        IsShown = view is not null;
        Value = view is null ? string.Empty : Encoding.UTF8.GetString(view.Value);
        Warning = view?.Warning ?? string.Empty;
        Note = view?.Note ?? string.Empty;
        HasNote = view?.Note is not null;
        CopyLabel = view?.CopyLabel ?? string.Empty;
        if (view is null)
        {
            CopyFailed = false;
        }
    }

    internal void Copied(bool copied) => CopyFailed = !copied;
}
