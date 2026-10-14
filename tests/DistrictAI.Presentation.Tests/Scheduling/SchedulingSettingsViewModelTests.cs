using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Scheduling;
using Xunit;

namespace DistrictAI.Presentation.Tests.Scheduling;

public sealed class SchedulingSettingsViewModelTests
{
    private static readonly SchedulingSectionEntryView[] _sections =
    [
        new(SchedulingAdminSection.Overview, "Overview", "Your booking desk", false),
        new(SchedulingAdminSection.Team, "Team", "Who takes bookings", false),
        new(SchedulingAdminSection.Settings, "Settings", "Your booking page", true),
    ];

    private static readonly LoadStatus _failed =
        new LoadStatus.Failed(new FailureView("Offline.", null, true), "Could not load this section");

    private static DialogView Dialog(string title, string confirm, bool canConfirm = true, bool working = false) =>
        new(title, string.Empty, confirm, "Cancel", canConfirm, working, false, null);

    private static SettingsTabView[] Tabs(SchedulingSettingsTab selected) =>
    [
        new(SchedulingSettingsTab.BookingPage, "Booking page", selected == SchedulingSettingsTab.BookingPage),
        new(SchedulingSettingsTab.Assistant, "Booking assistant", selected == SchedulingSettingsTab.Assistant),
        new(SchedulingSettingsTab.Profile, "Your profile", selected == SchedulingSettingsTab.Profile),
        new(SchedulingSettingsTab.Notifications, "Your notifications", selected == SchedulingSettingsTab.Notifications),
    ];

    private static SettingsImageView Image(
        SchedulingImageTarget target,
        string label,
        bool set = true,
        bool enabled = true,
        bool working = false,
        string? notice = null,
        FailureView? failure = null) =>
        new(
            target,
            label,
            set ? "Set" : "Not set",
            "Choose image",
            "Remove",
            set,
            enabled,
            working,
            notice,
            failure,
            new SchedulingAskView(label, "Images save as soon as you choose or remove them.", "Remove", "Cancel"));

    private static SettingsBrandingView Branding(bool working = false, string? error = null, string? notice = null) =>
        new(
            Dialog: Dialog("Booking page", "Save booking page", working: working),
            BusinessNameLabel: "Business name",
            BusinessNameHint: "Shown on your booking page.",
            BusinessName: "Contract Agency",
            LogoHeightLabel: "Logo height",
            LogoHeightHint: "16 to 64 pixels.",
            LogoHeight: 48,
            LogoHeightMin: 16,
            LogoHeightMax: 64,
            LogoOpacityLabel: "Logo opacity",
            BannerOpacityLabel: "Banner opacity",
            OpacityHint: "20 to 100 percent.",
            LogoOpacity: 100,
            BannerOpacity: 80,
            OpacityMin: 20,
            OpacityMax: 100,
            PrivacyLabel: "Privacy policy link",
            PrivacyUrl: string.Empty,
            TermsLabel: "Terms link",
            TermsUrl: "https://www.contract.test/terms",
            LegalUrlHint: "A full address, or leave it empty.",
            LocaleLabel: "Page language",
            LocaleHint: "When a visitor's language is not offered.",
            Locales: [new("en", "English", true), new("fr", "Français", false)],
            Error: error,
            Editable: !working,
            Notice: notice,
            ImagesNote: "Images save as soon as you choose or remove them.",
            ImageHint: "JPEG, PNG, GIF or WebP, up to 5 MB.",
            Logo: Image(SchedulingImageTarget.Logo, "Logo", enabled: !working),
            Banner: Image(SchedulingImageTarget.Banner, "Banner", enabled: !working));

    private static SettingsProfileView Profile(string? error = null, SettingsImageView? avatar = null) =>
        new(
            Dialog: Dialog("Your profile", "Save profile"),
            NameLabel: "Name",
            Name: "Contract Member",
            TimezoneLabel: "Time zone",
            Timezones: [new("America/Halifax", "America/Halifax", false), new("America/Toronto", "America/Toronto", true)],
            TimeFormatLabel: "Time format",
            TimeFormats: [new("12h", "12-hour (2:30 pm)", false), new("24h", "24-hour (14:30)", true)],
            WeekStartLabel: "Week starts on",
            WeekStarts: [new("0", "Sunday", false), new("1", "Monday", true)],
            DateFormatLabel: "Date format",
            DateFormats: [new("dmy", "31/12/2026", false), new("ymd", "2026-12-31", true)],
            Error: error,
            Editable: true,
            Notice: null,
            ImageHint: "JPEG, PNG, GIF or WebP, up to 5 MB.",
            Avatar: avatar ?? Image(SchedulingImageTarget.Avatar, "Profile picture"));

    private static SettingsNotificationsView Notifications(bool reschedule = false, bool working = false) =>
        new(
            Dialog: Dialog("Your notifications", "Save notifications", working: working),
            Groups:
            [
                new("As an attendee", "Emails sent to you when you book.",
                [
                    new("notify_confirmation", "Booking confirmed", true),
                    new("notify_reschedule", "Booking moved", reschedule),
                ]),
                new("As a host", "Emails sent to you when someone books you.",
                [
                    new("notify_host_booking", "New booking", true),
                ]),
            ],
            Editable: !working,
            Notice: null);

    private static SettingsAssistantView Assistant(bool enabled = true, bool dirty = false, string? notice = null) =>
        new(
            Dialog: Dialog("Booking assistant", "Save assistant", canConfirm: dirty),
            Hint: "It answers booking questions.",
            ToggleLabel: "Turn on the booking assistant",
            Enabled: enabled,
            InstructionsLabel: "Extra instructions",
            InstructionsHint: "Up to 4,000 characters.",
            InstructionsPlaceholder: "Always offer the earliest time.",
            Instructions: "Always list the agreed next steps first.",
            Error: null,
            Editable: true,
            Notice: notice);

    private static SchedulingSettingsView Settings(
        SchedulingSettingsTab tab = SchedulingSettingsTab.BookingPage,
        LoadStatus? status = null,
        bool offersEdit = true,
        SettingsBrandingView? branding = null,
        SettingsProfileView? profile = null,
        SettingsNotificationsView? notifications = null,
        SettingsAssistantView? assistant = null)
    {
        status ??= new LoadStatus.Ready();
        SchedulingSettingsGroupView[] groups = status is LoadStatus.Ready
            ?
            [
                new("Booking page", null, [new("Business name", "Contract Agency"), new("Logo", "Set")]),
                new("As a host", "Emails sent to you.", [new("New booking", "On")]),
            ]
            : [];
        return new(Tabs(tab), status, groups, offersEdit && status is LoadStatus.Ready, "Edit", profile, notifications, branding, assistant);
    }

    private static SchedulingAdminView Page(SchedulingSettingsView view) =>
        new("Settings", _sections, new SchedulingAdminPage.Settings(view));

    private static (SchedulingAdminViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new SchedulingAdminViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    /// <summary>The settings action <paramref name="sent"/> carries.</summary>
    private static SchedulingSettingsAction Action(UiEvent sent) =>
        Assert.IsType<SchedulingAdminAction.Settings>(Assert.IsType<UiEvent.SchedulingAdmin>(sent).Action).Action;

    [Fact]
    public void TheTabShowsWhatItReadWithEdit()
    {
        var (model, sink) = Attached();
        model.Show(Page(Settings()));
        var vm = model.Settings;
        Assert.True(model.ShowsSettings);
        Assert.False(model.ShowsTeam || model.ShowsCalendar);
        Assert.True(vm.Load.Ready);
        Assert.Equal(0, vm.TabIndex);
        Assert.True(vm.OnBookingPage);
        Assert.False(vm.OnAssistant || vm.OnProfile || vm.OnNotifications);
        Assert.Equal(("Booking page", "Booking assistant", "Your profile", "Your notifications"),
            (vm.BookingPageLabel, vm.AssistantLabel, vm.ProfileLabel, vm.NotificationsLabel));
        Assert.Equal(2, vm.Groups.Count);
        Assert.False(vm.Groups[0].HasDescription);
        Assert.True(vm.Groups[1].HasDescription);
        Assert.Equal("Business name: Contract Agency", vm.Groups[0].Rows[0].ToString());
        Assert.Equal("Booking page", vm.Groups[0].ToString());
        Assert.True(vm.OffersEdit);
        Assert.Equal("Edit", vm.EditLabel);

        vm.OpenEditorCommand.Execute(null);
        var opened = Assert.IsType<SchedulingSettingsAction.OpenEditor>(Action(sink.Sent[^1]));
        Assert.Equal(SchedulingSettingsTab.BookingPage, opened.Tab);

        // The tab showing sends nothing; another reads.
        vm.SelectTab(0);
        Assert.Single(sink.Sent);
        vm.SelectTab(2);
        Assert.Equal(SchedulingSettingsTab.Profile, Assert.IsType<SchedulingSettingsAction.SelectTab>(Action(sink.Sent[^1])).Tab);
        vm.SelectTab(9);
        Assert.Equal(2, sink.Sent.Count);

        model.Show(Page(Settings(SchedulingSettingsTab.Notifications)));
        Assert.Equal(3, vm.TabIndex);
        Assert.True(vm.OnNotifications);
    }

    [Fact]
    public void AFailedTabReadsOnlyItselfAgain()
    {
        var (model, sink) = Attached();
        model.Show(Page(Settings(status: _failed)));
        var vm = model.Settings;
        Assert.True(vm.Load.Failed);
        Assert.False(vm.OffersEdit);
        Assert.Empty(vm.Groups);
        vm.Load.RetryCommand.Execute(null);
        Assert.IsType<SchedulingSettingsAction.Refresh>(Action(sink.Sent[^1]));
    }

    [Fact]
    public void TheBookingPageIsWrittenOnceAndSendsEachChangeWhole()
    {
        var (model, sink) = Attached();
        model.Show(Page(Settings(branding: Branding())));
        var vm = model.Settings.Branding;
        Assert.True(vm.Dialog.IsOpen);
        Assert.Equal("Contract Agency", vm.BusinessName);
        Assert.Equal((48d, 100d, 80d), (vm.LogoHeight, vm.LogoOpacity, vm.BannerOpacity));
        Assert.Equal((16d, 64d, 20d, 100d), (vm.LogoHeightMinimum, vm.LogoHeightMaximum, vm.OpacityMinimum, vm.OpacityMaximum));
        Assert.Equal(0, vm.LocaleIndex);
        Assert.Equal(2, vm.Locales.Count);
        Assert.Empty(sink.Sent);

        vm.LogoHeight = 40;
        var form = Assert.IsType<SchedulingSettingsAction.EditBranding>(Action(sink.Sent[^1])).Form;
        Assert.Equal(new SettingsBrandingForm("Contract Agency", 40, 100, 80, string.Empty, "https://www.contract.test/terms", "en"), form);

        // A box emptied is 0, which the core refuses.
        vm.LogoOpacity = double.NaN;
        Assert.Equal(0, Assert.IsType<SchedulingSettingsAction.EditBranding>(Action(sink.Sent[^1])).Form.LogoOpacity);
        Assert.Equal(0, SettingsBrandingViewModel.Whole(double.PositiveInfinity));
        Assert.Equal(17, SettingsBrandingViewModel.Whole(16.6));

        var sent = sink.Sent.Count;
        vm.PickLocale(0);
        vm.PickLocale(5);
        Assert.Equal(sent, sink.Sent.Count);
        vm.PickLocale(1);
        Assert.Equal("fr", Assert.IsType<SchedulingSettingsAction.EditBranding>(Action(sink.Sent[^1])).Form.FallbackLocale);

        // The fields are not written over while the dialog stays open.
        vm.BusinessName = "Typed";
        model.Show(Page(Settings(branding: Branding(error: "Enter a whole number from 16 to 64."))));
        Assert.Equal("Typed", vm.BusinessName);
        Assert.True(vm.HasError);

        model.Show(Page(Settings(branding: Branding(working: true))));
        Assert.False(vm.Editable);
        Assert.False(vm.Logo.Enabled || vm.Logo.MayPick);
        model.Show(Page(Settings(branding: Branding(notice: "Booking page saved"))));
        Assert.True(vm.HasNotice);

        model.Show(Page(Settings()));
        Assert.False(vm.Dialog.IsOpen);
        sent = sink.Sent.Count;
        vm.BusinessName = "After";
        Assert.Equal(sent, sink.Sent.Count);
    }

    [Fact]
    public void AnImageIsPickedAndItsRemovalAskedOnceMore()
    {
        var (model, sink) = Attached();
        model.Show(Page(Settings(branding: Branding())));
        var logo = model.Settings.Branding.Logo;
        Assert.Equal(SchedulingImageTarget.Logo, logo.Target);
        Assert.Equal(("Set", "Choose image: Logo", "Remove: Logo"), (logo.Value, logo.ChooseName, logo.RemoveName));
        Assert.True(logo.ShowsRemove && logo.MayPick);

        // A cancelled pick sends nothing; an unreadable file says so; a file goes.
        logo.Picked([]);
        Assert.Empty(sink.Sent);
        logo.Picked([new PickedFile("logo.heic", null)]);
        Assert.Equal(SchedulingImageTarget.Logo, Assert.IsType<SchedulingSettingsAction.ImageUnreadable>(Action(sink.Sent[^1])).Target);
        var data = new PickedFileView("logo.png", 4, [1, 2, 3, 4]);
        logo.Picked([new PickedFile("logo.png", data)]);
        var upload = Assert.IsType<SchedulingSettingsAction.UploadImage>(Action(sink.Sent[^1]));
        Assert.Equal((SchedulingImageTarget.Logo, data), (upload.Target, upload.File));

        // Remove asks first; the way back sends nothing; the confirm, once.
        var sent = sink.Sent.Count;
        logo.ConfirmRemoveCommand.Execute(null);
        Assert.Equal(sent, sink.Sent.Count);
        logo.AskRemoveCommand.Execute(null);
        Assert.True(logo.Asking);
        Assert.Equal(("Logo", "Remove", "Cancel"), (logo.AskTitle, logo.AskConfirm, logo.AskCancel));
        Assert.StartsWith("Images save", logo.AskBody);
        logo.CancelRemoveCommand.Execute(null);
        Assert.False(logo.Asking);
        Assert.Equal(sent, sink.Sent.Count);
        logo.AskRemoveCommand.Execute(null);
        logo.ConfirmRemoveCommand.Execute(null);
        logo.AskRemoveCommand.Execute(null);
        logo.ConfirmRemoveCommand.Execute(null);
        Assert.Equal(sent + 1, sink.Sent.Count);
        Assert.Equal(SchedulingImageTarget.Logo, Assert.IsType<SchedulingSettingsAction.RemoveImage>(Action(sink.Sent[^1])).Target);

        // While it is on its way nothing more is picked; once it landed, the removal can be asked again.
        var removing = Branding() with { Logo = Image(SchedulingImageTarget.Logo, "Logo", enabled: false, working: true) };
        model.Show(Page(Settings(branding: removing)));
        Assert.True(logo.Working);
        Assert.False(logo.MayPick);
        logo.Picked([new PickedFile("logo.png", data)]);
        Assert.Equal(sent + 1, sink.Sent.Count);
        var removed = Branding() with { Logo = Image(SchedulingImageTarget.Logo, "Logo", set: false, notice: "Logo removed") };
        model.Show(Page(Settings(branding: removed)));
        Assert.True(logo.HasNotice);
        Assert.False(logo.ShowsRemove || logo.Asking);
        var refused = Branding() with
        {
            Logo = Image(SchedulingImageTarget.Logo, "Logo", failure: new FailureView("Choose a JPEG, PNG, GIF or WebP image.", null, false)),
        };
        model.Show(Page(Settings(branding: refused)));
        Assert.True(logo.HasFailure);
        Assert.Equal("Choose a JPEG, PNG, GIF or WebP image.", logo.Failure);

        logo.AskRemoveCommand.Execute(null);
        model.Show(Page(Settings()));
        Assert.False(logo.Asking);
    }

    [Fact]
    public void TheProfileIsWrittenOnceAndItsChoicesSendTheForm()
    {
        var (model, sink) = Attached();
        model.Show(Page(Settings(SchedulingSettingsTab.Profile, profile: Profile())));
        var vm = model.Settings.Profile;
        Assert.Equal("Contract Member", vm.Name);
        Assert.Equal((1, 1, 1, 1), (vm.TimezoneIndex, vm.TimeFormatIndex, vm.WeekStartIndex, vm.DateFormatIndex));
        Assert.Equal(2, vm.Timezones.Count);
        Assert.Empty(sink.Sent);

        vm.PickWeekStart(0);
        var form = Assert.IsType<SchedulingSettingsAction.EditProfile>(Action(sink.Sent[^1])).Form;
        Assert.Equal(new SettingsProfileForm("Contract Member", "America/Toronto", "24h", 0, "ymd"), form);
        vm.PickTimezone(0);
        Assert.Equal("America/Halifax", Assert.IsType<SchedulingSettingsAction.EditProfile>(Action(sink.Sent[^1])).Form.Timezone);
        vm.PickTimeFormat(0);
        Assert.Equal("12h", Assert.IsType<SchedulingSettingsAction.EditProfile>(Action(sink.Sent[^1])).Form.TimeFormat);
        vm.PickDateFormat(0);
        Assert.Equal("dmy", Assert.IsType<SchedulingSettingsAction.EditProfile>(Action(sink.Sent[^1])).Form.DateFormat);
        var sent = sink.Sent.Count;
        vm.PickDateFormat(0);
        vm.PickDateFormat(-1);
        Assert.Equal(sent, sink.Sent.Count);
        vm.Name = "Ada";
        Assert.Equal("Ada", Assert.IsType<SchedulingSettingsAction.EditProfile>(Action(sink.Sent[^1])).Form.Name);

        model.Show(Page(Settings(SchedulingSettingsTab.Profile, profile: Profile(error: "Enter your name."))));
        Assert.True(vm.HasError);
        Assert.Equal("Ada", vm.Name);
        Assert.Equal(SchedulingImageTarget.Avatar, vm.Avatar.Target);

        model.Show(Page(Settings(SchedulingSettingsTab.Profile)));
        Assert.False(vm.Dialog.IsOpen);
    }

    [Fact]
    public void ANotificationSwitchSendsOnlyAChange()
    {
        var (model, sink) = Attached();
        model.Show(Page(Settings(SchedulingSettingsTab.Notifications, notifications: Notifications())));
        var vm = model.Settings.Notifications;
        Assert.Equal(2, vm.Groups.Count);
        var reschedule = vm.Groups[0].Switches[1];
        Assert.Equal(("SettingsNotifynotify_reschedule", "Booking moved"), (reschedule.AutomationId, reschedule.ToString()));
        Assert.Equal("As an attendee", vm.Groups[0].ToString());

        vm.Set("notify_reschedule", false);
        vm.Set("notify_unknown", true);
        Assert.Empty(sink.Sent);
        vm.Set("notify_reschedule", true);
        var set = Assert.IsType<SchedulingSettingsAction.SetNotification>(Action(sink.Sent[^1]));
        Assert.Equal(("notify_reschedule", true), (set.Field, set.On));

        // While the save is on its way the switches send nothing.
        model.Show(Page(Settings(SchedulingSettingsTab.Notifications, notifications: Notifications(reschedule: true, working: true))));
        vm.Set("notify_confirmation", false);
        Assert.Single(sink.Sent);
        Assert.False(vm.Groups[0].Switches[0].Enabled);
    }

    [Fact]
    public void TheAssistantIsWrittenOnceAndSendsBoth()
    {
        var (model, sink) = Attached();
        model.Show(Page(Settings(SchedulingSettingsTab.Assistant, assistant: Assistant())));
        var vm = model.Settings.Assistant;
        Assert.True(vm.Enabled);
        Assert.Equal("Always list the agreed next steps first.", vm.Instructions);
        Assert.False(vm.Dialog.CanConfirm);
        Assert.Empty(sink.Sent);

        vm.Enabled = false;
        var edit = Assert.IsType<SchedulingSettingsAction.EditAssistant>(Action(sink.Sent[^1]));
        Assert.Equal((false, "Always list the agreed next steps first."), (edit.Enabled, edit.Instructions));
        model.Show(Page(Settings(SchedulingSettingsTab.Assistant, assistant: Assistant(enabled: false, dirty: true))));
        Assert.True(vm.Dialog.CanConfirm);
        vm.Dialog.ConfirmCommand.Execute(null);
        Assert.IsType<SchedulingSettingsAction.SaveAssistant>(Action(sink.Sent[^1]));
        model.Show(Page(Settings(SchedulingSettingsTab.Assistant, assistant: Assistant(enabled: false, notice: "Assistant saved"))));
        Assert.True(vm.HasNotice);
        Assert.False(vm.Enabled);
    }
}
