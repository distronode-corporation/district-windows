using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// An image of a settings dialog (the core's <see cref="SettingsImageView"/>):
/// whether there is one, Choose image, and Remove, asked once more here, as
/// on iPhone, the remove never the default. Each saves as soon as it is
/// chosen or removed, and says what it came to beside it.
/// </summary>
/// <remarks>
/// The window opens the file chooser and hands over what was picked
/// (<see cref="Picked"/>); the core checks the type, sniffed from the bytes,
/// and the size, and says what it refused.
/// </remarks>
public sealed partial class SettingsImageViewModel : ObservableObject
{
    private PageContext? _context;
    private bool _removing;

    /// <summary>Which image.</summary>
    [ObservableProperty]
    public partial SchedulingImageTarget Target { get; set; }

    /// <summary>"Logo".</summary>
    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    /// <summary>"Set" or "Not set".</summary>
    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    /// <summary>"Choose image".</summary>
    [ObservableProperty]
    public partial string ChooseLabel { get; set; } = string.Empty;

    /// <summary>"Remove".</summary>
    [ObservableProperty]
    public partial string RemoveLabel { get; set; } = string.Empty;

    /// <summary>Whether Remove shows: there is an image.</summary>
    [ObservableProperty]
    public partial bool ShowsRemove { get; set; }

    /// <summary>Whether Choose image and Remove can be pressed: nothing in the dialog is on its way.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskRemoveCommand), nameof(ConfirmRemoveCommand))]
    public partial bool Enabled { get; set; }

    /// <summary>Whether this image's upload or removal is on its way.</summary>
    [ObservableProperty]
    public partial bool Working { get; set; }

    /// <summary>What its last upload or removal came to, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>Why its last upload or removal failed, or the file was refused, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>Whether the removal is being asked about.</summary>
    [ObservableProperty]
    public partial bool Asking { get; set; }

    /// <summary>The question's title: the image's name.</summary>
    [ObservableProperty]
    public partial string AskTitle { get; set; } = string.Empty;

    /// <summary>That it saves at once.</summary>
    [ObservableProperty]
    public partial string AskBody { get; set; } = string.Empty;

    /// <summary>The removal's confirm.</summary>
    [ObservableProperty]
    public partial string AskConfirm { get; set; } = string.Empty;

    /// <summary>The way back.</summary>
    [ObservableProperty]
    public partial string AskCancel { get; set; } = string.Empty;

    /// <summary>What a screen reader says for Choose image: "Choose image: Logo".</summary>
    public string ChooseName => ChooseLabel + ": " + Label;

    /// <summary>What a screen reader says for Remove: "Remove: Logo".</summary>
    public string RemoveName => RemoveLabel + ": " + Label;

    /// <summary>Whether an image may be picked now.</summary>
    public bool MayPick => Enabled && !Working;

    internal void Attach(PageContext context) => _context = context;

    internal void Show(SettingsImageView view)
    {
        Target = view.Target;
        Label = view.Label;
        Value = view.Value;
        ChooseLabel = view.ChooseLabel;
        RemoveLabel = view.RemoveLabel;
        ShowsRemove = view.ShowsRemove;
        Enabled = view.Enabled;
        Working = view.Working;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        Failure = Display.Failure(view.Failure);
        HasFailure = view.Failure is not null;
        AskTitle = view.RemoveAsk.Title;
        AskBody = view.RemoveAsk.Body;
        AskConfirm = view.RemoveAsk.Confirm;
        AskCancel = view.RemoveAsk.Cancel;
        if (!view.Working)
        {
            _removing = false;
        }
        if (!view.ShowsRemove)
        {
            Asking = false;
        }
        OnPropertyChanged(nameof(ChooseName));
        OnPropertyChanged(nameof(RemoveName));
    }

    /// <summary>Puts the question away: the dialog closed.</summary>
    internal void Reset()
    {
        Asking = false;
        _removing = false;
    }

    /// <summary>
    /// Uploads what the member picked; an unreadable file says so, and a
    /// cancelled pick sends nothing.
    /// </summary>
    internal void Picked(IReadOnlyList<PickedFile> picked)
    {
        if (!MayPick || picked.Count == 0)
        {
            return;
        }
        Asking = false;
        Send(picked[0].Data is { } data
            ? new SchedulingSettingsAction.UploadImage(Target, data)
            : new SchedulingSettingsAction.ImageUnreadable(Target));
    }

    [RelayCommand(CanExecute = nameof(Enabled))]
    private void AskRemove() => Asking = true;

    [RelayCommand]
    private void CancelRemove() => Asking = false;

    /// <summary>Removes the image: one press, one removal.</summary>
    [RelayCommand(CanExecute = nameof(Enabled))]
    private void ConfirmRemove()
    {
        if (Asking && !_removing)
        {
            Asking = false;
            _removing = true;
            Send(new SchedulingSettingsAction.RemoveImage(Target));
        }
    }

    private void Send(SchedulingSettingsAction action) => _context?.Send(SchedulingSettingsViewModel.Event(action));
}

/// <summary>
/// The booking page (the core's <see cref="SettingsBrandingView"/>): its
/// business name, the logo's height and opacity, the banner's opacity, the
/// privacy and terms links, the language it falls back to, and its two
/// images. The save is the dialog's confirm; what it came to shows in the
/// dialog.
/// </summary>
/// <remarks>
/// The fields are written once, as the dialog opens: from then on what the
/// member leaves in them is the form, and each change sends it whole.
/// </remarks>
public sealed partial class SettingsBrandingViewModel : ObservableObject
{
    private string[] _locales = [];
    private PageContext? _context;
    private bool _writing;

    /// <summary>The dialog, with nothing shown yet.</summary>
    public SettingsBrandingViewModel()
    {
        Dialog = new AdminDialogViewModel(
            () => SchedulingSettingsViewModel.Event(new SchedulingSettingsAction.SaveBranding()),
            () => SchedulingSettingsViewModel.Event(new SchedulingSettingsAction.CloseDialog()));
    }

    /// <summary>The dialog's title, save, way out and the save's failure.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The logo.</summary>
    public SettingsImageViewModel Logo { get; } = new();

    /// <summary>The banner.</summary>
    public SettingsImageViewModel Banner { get; } = new();

    /// <summary>The languages the page can fall back to.</summary>
    public ObservableCollection<ChoiceEntry> Locales { get; } = [];

    /// <summary>The labels and hints, or null while the dialog is closed.</summary>
    [ObservableProperty]
    public partial SettingsBrandingView? Words { get; set; }

    /// <summary>The business name, as typed.</summary>
    [ObservableProperty]
    public partial string BusinessName { get; set; } = string.Empty;

    /// <summary>The logo's height, in pixels.</summary>
    [ObservableProperty]
    public partial double LogoHeight { get; set; }

    /// <summary>The logo's opacity, in percent.</summary>
    [ObservableProperty]
    public partial double LogoOpacity { get; set; }

    /// <summary>The banner's opacity, in percent.</summary>
    [ObservableProperty]
    public partial double BannerOpacity { get; set; }

    /// <summary>The least logo height.</summary>
    [ObservableProperty]
    public partial double LogoHeightMinimum { get; set; }

    /// <summary>The greatest logo height.</summary>
    [ObservableProperty]
    public partial double LogoHeightMaximum { get; set; }

    /// <summary>The least opacity.</summary>
    [ObservableProperty]
    public partial double OpacityMinimum { get; set; }

    /// <summary>The greatest opacity.</summary>
    [ObservableProperty]
    public partial double OpacityMaximum { get; set; }

    /// <summary>The privacy policy's link, as typed.</summary>
    [ObservableProperty]
    public partial string PrivacyUrl { get; set; } = string.Empty;

    /// <summary>The terms' link, as typed.</summary>
    [ObservableProperty]
    public partial string TermsUrl { get; set; } = string.Empty;

    /// <summary>Which of <see cref="Locales"/> is chosen, or -1.</summary>
    [ObservableProperty]
    public partial int LocaleIndex { get; set; } = -1;

    /// <summary>Why the fields cannot be sent, or empty.</summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="Error"/>.</summary>
    [ObservableProperty]
    public partial bool HasError { get; set; }

    /// <summary>Whether the fields can be changed: nothing is on its way.</summary>
    [ObservableProperty]
    public partial bool Editable { get; set; }

    /// <summary>What the save came to, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Dialog.Attach(context);
        Logo.Attach(context);
        Banner.Attach(context);
    }

    /// <summary>Shows <paramref name="view"/>, or closes the dialog when the core holds none.</summary>
    internal void Show(SettingsBrandingView? view)
    {
        var opening = view is not null && !Dialog.IsOpen;
        Dialog.Show(view?.Dialog);
        Words = view;
        if (view is null)
        {
            Logo.Reset();
            Banner.Reset();
            return;
        }
        if (opening)
        {
            _writing = true;
            try
            {
                LogoHeightMinimum = view.LogoHeightMin;
                LogoHeightMaximum = view.LogoHeightMax;
                OpacityMinimum = view.OpacityMin;
                OpacityMaximum = view.OpacityMax;
                BusinessName = view.BusinessName;
                LogoHeight = view.LogoHeight;
                LogoOpacity = view.LogoOpacity;
                BannerOpacity = view.BannerOpacity;
                PrivacyUrl = view.PrivacyUrl;
                TermsUrl = view.TermsUrl;
                _locales = [.. view.Locales.Select(choice => choice.Value)];
                Display.Sync(Locales, [.. view.Locales.Select(ChoiceEntry.From)]);
                LocaleIndex = Array.FindIndex(view.Locales, choice => choice.Selected);
            }
            finally
            {
                _writing = false;
            }
        }
        Error = view.Error ?? string.Empty;
        HasError = view.Error is not null;
        Editable = view.Editable;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        Logo.Show(view.Logo);
        Banner.Show(view.Banner);
    }

    /// <summary>Chooses the language at <paramref name="index"/>; the one chosen sends nothing.</summary>
    internal void PickLocale(int index)
    {
        if (index < 0 || index >= _locales.Length || index == LocaleIndex)
        {
            return;
        }
        LocaleIndex = index;
        Edited();
    }

    /// <summary>The form as the fields hold it.</summary>
    internal SettingsBrandingForm Form() => new(
        BusinessName,
        Whole(LogoHeight),
        Whole(LogoOpacity),
        Whole(BannerOpacity),
        PrivacyUrl,
        TermsUrl,
        LocaleIndex >= 0 && LocaleIndex < _locales.Length ? _locales[LocaleIndex] : string.Empty);

    /// <summary>A number box's value as a whole number: an emptied box is 0, which the core refuses.</summary>
    internal static long Whole(double value) => double.IsFinite(value) ? (long)Math.Round(value) : 0;

    partial void OnBusinessNameChanged(string value) => Edited();

    partial void OnLogoHeightChanged(double value) => Edited();

    partial void OnLogoOpacityChanged(double value) => Edited();

    partial void OnBannerOpacityChanged(double value) => Edited();

    partial void OnPrivacyUrlChanged(string value) => Edited();

    partial void OnTermsUrlChanged(string value) => Edited();

    private void Edited()
    {
        if (!_writing && Dialog.IsOpen)
        {
            _context?.Send(SchedulingSettingsViewModel.Event(new SchedulingSettingsAction.EditBranding(Form())));
        }
    }
}

/// <summary>
/// The member's profile (the core's <see cref="SettingsProfileView"/>): their
/// name, time zone, time and date formats, the day their week starts on, and
/// their picture. The save is the dialog's confirm.
/// </summary>
/// <remarks>
/// The fields are written once, as the dialog opens; each change sends the
/// whole form.
/// </remarks>
public sealed partial class SettingsProfileViewModel : ObservableObject
{
    private string[] _timezones = [];
    private string[] _timeFormats = [];
    private string[] _weekStarts = [];
    private string[] _dateFormats = [];
    private PageContext? _context;
    private bool _writing;

    /// <summary>The dialog, with nothing shown yet.</summary>
    public SettingsProfileViewModel()
    {
        Dialog = new AdminDialogViewModel(
            () => SchedulingSettingsViewModel.Event(new SchedulingSettingsAction.SaveProfile()),
            () => SchedulingSettingsViewModel.Event(new SchedulingSettingsAction.CloseDialog()));
    }

    /// <summary>The dialog's title, save, way out and the save's failure.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The member's picture.</summary>
    public SettingsImageViewModel Avatar { get; } = new();

    /// <summary>Every time zone.</summary>
    public ObservableCollection<ChoiceEntry> Timezones { get; } = [];

    /// <summary>12-hour and 24-hour.</summary>
    public ObservableCollection<ChoiceEntry> TimeFormats { get; } = [];

    /// <summary>The seven days.</summary>
    public ObservableCollection<ChoiceEntry> WeekStarts { get; } = [];

    /// <summary>The date formats.</summary>
    public ObservableCollection<ChoiceEntry> DateFormats { get; } = [];

    /// <summary>The labels, or null while the dialog is closed.</summary>
    [ObservableProperty]
    public partial SettingsProfileView? Words { get; set; }

    /// <summary>The name, as typed.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Which of <see cref="Timezones"/> is chosen, or -1.</summary>
    [ObservableProperty]
    public partial int TimezoneIndex { get; set; } = -1;

    /// <summary>Which of <see cref="TimeFormats"/> is chosen, or -1.</summary>
    [ObservableProperty]
    public partial int TimeFormatIndex { get; set; } = -1;

    /// <summary>Which of <see cref="WeekStarts"/> is chosen, or -1.</summary>
    [ObservableProperty]
    public partial int WeekStartIndex { get; set; } = -1;

    /// <summary>Which of <see cref="DateFormats"/> is chosen, or -1.</summary>
    [ObservableProperty]
    public partial int DateFormatIndex { get; set; } = -1;

    /// <summary>Why the fields cannot be sent, or empty.</summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="Error"/>.</summary>
    [ObservableProperty]
    public partial bool HasError { get; set; }

    /// <summary>Whether the fields can be changed: nothing is on its way.</summary>
    [ObservableProperty]
    public partial bool Editable { get; set; }

    /// <summary>What the save came to, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Dialog.Attach(context);
        Avatar.Attach(context);
    }

    /// <summary>Shows <paramref name="view"/>, or closes the dialog when the core holds none.</summary>
    internal void Show(SettingsProfileView? view)
    {
        var opening = view is not null && !Dialog.IsOpen;
        Dialog.Show(view?.Dialog);
        Words = view;
        if (view is null)
        {
            Avatar.Reset();
            return;
        }
        if (opening)
        {
            _writing = true;
            try
            {
                Name = view.Name;
                TimezoneIndex = Fill(Timezones, view.Timezones, out _timezones);
                TimeFormatIndex = Fill(TimeFormats, view.TimeFormats, out _timeFormats);
                WeekStartIndex = Fill(WeekStarts, view.WeekStarts, out _weekStarts);
                DateFormatIndex = Fill(DateFormats, view.DateFormats, out _dateFormats);
            }
            finally
            {
                _writing = false;
            }
        }
        Error = view.Error ?? string.Empty;
        HasError = view.Error is not null;
        Editable = view.Editable;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        Avatar.Show(view.Avatar);
    }

    /// <summary>Chooses the time zone at <paramref name="index"/>.</summary>
    internal void PickTimezone(int index) => Pick(index, _timezones.Length, TimezoneIndex, i => TimezoneIndex = i);

    /// <summary>Chooses the time format at <paramref name="index"/>.</summary>
    internal void PickTimeFormat(int index) => Pick(index, _timeFormats.Length, TimeFormatIndex, i => TimeFormatIndex = i);

    /// <summary>Chooses the day the week starts on at <paramref name="index"/>.</summary>
    internal void PickWeekStart(int index) => Pick(index, _weekStarts.Length, WeekStartIndex, i => WeekStartIndex = i);

    /// <summary>Chooses the date format at <paramref name="index"/>.</summary>
    internal void PickDateFormat(int index) => Pick(index, _dateFormats.Length, DateFormatIndex, i => DateFormatIndex = i);

    /// <summary>The form as the fields hold it.</summary>
    internal SettingsProfileForm Form() => new(
        Name,
        At(_timezones, TimezoneIndex),
        At(_timeFormats, TimeFormatIndex),
        long.TryParse(At(_weekStarts, WeekStartIndex), System.Globalization.CultureInfo.InvariantCulture, out var day) ? day : -1,
        At(_dateFormats, DateFormatIndex));

    partial void OnNameChanged(string value) => Edited();

    /// <summary>Fills <paramref name="target"/> with <paramref name="choices"/>; the index of the one chosen, or -1.</summary>
    private static int Fill(ObservableCollection<ChoiceEntry> target, ChoiceRowView[] choices, out string[] values)
    {
        values = [.. choices.Select(choice => choice.Value)];
        Display.Sync(target, [.. choices.Select(ChoiceEntry.From)]);
        return Array.FindIndex(choices, choice => choice.Selected);
    }

    private static string At(string[] values, int index) =>
        index >= 0 && index < values.Length ? values[index] : string.Empty;

    private void Pick(int index, int count, int current, Action<int> set)
    {
        if (index < 0 || index >= count || index == current)
        {
            return;
        }
        set(index);
        Edited();
    }

    private void Edited()
    {
        if (!_writing && Dialog.IsOpen)
        {
            _context?.Send(SchedulingSettingsViewModel.Event(new SchedulingSettingsAction.EditProfile(Form())));
        }
    }
}

/// <summary>
/// The member's email notifications (the core's
/// <see cref="SettingsNotificationsView"/>), as an attendee and as a host, a
/// switch each. The save is the dialog's confirm.
/// </summary>
public sealed partial class SettingsNotificationsViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The dialog, with nothing shown yet.</summary>
    public SettingsNotificationsViewModel()
    {
        Dialog = new AdminDialogViewModel(
            () => SchedulingSettingsViewModel.Event(new SchedulingSettingsAction.SaveNotifications()),
            () => SchedulingSettingsViewModel.Event(new SchedulingSettingsAction.CloseDialog()));
    }

    /// <summary>The dialog's title, save, way out and the save's failure.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The switches, in their two groups.</summary>
    public ObservableCollection<NotificationGroupEntry> Groups { get; } = [];

    /// <summary>What the save came to, or empty.</summary>
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
    internal void Show(SettingsNotificationsView? view)
    {
        Dialog.Show(view?.Dialog);
        if (view is null)
        {
            return;
        }
        Display.Sync(Groups, [.. view.Groups.Select(group => NotificationGroupEntry.From(group, view.Editable))]);
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
    }

    /// <summary>
    /// The switch <paramref name="field"/> now reads <paramref name="on"/>;
    /// one the core already holds that way sends nothing.
    /// </summary>
    internal void Set(string field, bool on)
    {
        var current = Groups.SelectMany(group => group.Switches).FirstOrDefault(entry => entry.Field == field);
        if (current is { Enabled: true } && current.On != on)
        {
            _context?.Send(SchedulingSettingsViewModel.Event(new SchedulingSettingsAction.SetNotification(field, on)));
        }
    }
}

/// <summary>The notifications of one kind: as an attendee, or as a host.</summary>
/// <param name="Heading">"As an attendee".</param>
/// <param name="Description">What they are.</param>
/// <param name="Switches">Its switches.</param>
public sealed record NotificationGroupEntry(string Heading, string Description, IReadOnlyList<NotificationSwitchEntry> Switches)
{
    internal static NotificationGroupEntry From(NotificationGroupView group, bool editable) =>
        new(group.Heading, group.Description, [.. group.Switches.Select(entry => new NotificationSwitchEntry(entry.Field, entry.Label, entry.On, editable))]);

    /// <inheritdoc/>
    public override string ToString() => Heading;
}

/// <summary>One notification's switch.</summary>
/// <param name="Field">The scheduler's name for it, which keys it.</param>
/// <param name="Label">"Booking confirmed".</param>
/// <param name="On">Whether it is on, as the core holds it.</param>
/// <param name="Enabled">Whether it can be switched: nothing is on its way.</param>
public sealed record NotificationSwitchEntry(string Field, string Label, bool On, bool Enabled)
{
    /// <summary>The UI Automation id of the switch ("SettingsNotify" and its field).</summary>
    public string AutomationId => "SettingsNotify" + Field;

    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>
/// The booking assistant (the core's <see cref="SettingsAssistantView"/>): on
/// or off, and its extra instructions. The save is the dialog's confirm,
/// offered once something changed.
/// </summary>
/// <remarks>
/// The fields are written once, as the dialog opens; each change sends both.
/// </remarks>
public sealed partial class SettingsAssistantViewModel : ObservableObject
{
    private PageContext? _context;
    private bool _writing;

    /// <summary>The dialog, with nothing shown yet.</summary>
    public SettingsAssistantViewModel()
    {
        Dialog = new AdminDialogViewModel(
            () => SchedulingSettingsViewModel.Event(new SchedulingSettingsAction.SaveAssistant()),
            () => SchedulingSettingsViewModel.Event(new SchedulingSettingsAction.CloseDialog()));
    }

    /// <summary>The dialog's title, save, way out and the save's failure.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The labels and hints, or null while the dialog is closed.</summary>
    [ObservableProperty]
    public partial SettingsAssistantView? Words { get; set; }

    /// <summary>Whether the assistant is on, as switched.</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>The extra instructions, as typed.</summary>
    [ObservableProperty]
    public partial string Instructions { get; set; } = string.Empty;

    /// <summary>Why they cannot be sent, or empty.</summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="Error"/>.</summary>
    [ObservableProperty]
    public partial bool HasError { get; set; }

    /// <summary>Whether the fields can be changed: nothing is on its way.</summary>
    [ObservableProperty]
    public partial bool Editable { get; set; }

    /// <summary>What the save came to, or empty.</summary>
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
    internal void Show(SettingsAssistantView? view)
    {
        var opening = view is not null && !Dialog.IsOpen;
        Dialog.Show(view?.Dialog);
        Words = view;
        if (view is null)
        {
            return;
        }
        if (opening)
        {
            _writing = true;
            try
            {
                Enabled = view.Enabled;
                Instructions = view.Instructions;
            }
            finally
            {
                _writing = false;
            }
        }
        Error = view.Error ?? string.Empty;
        HasError = view.Error is not null;
        Editable = view.Editable;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
    }

    partial void OnEnabledChanged(bool value) => Edited();

    partial void OnInstructionsChanged(string value) => Edited();

    private void Edited()
    {
        if (!_writing && Dialog.IsOpen)
        {
            _context?.Send(SchedulingSettingsViewModel.Event(new SchedulingSettingsAction.EditAssistant(Enabled, Instructions)));
        }
    }
}
