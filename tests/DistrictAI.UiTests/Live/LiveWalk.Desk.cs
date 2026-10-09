using System.Drawing;
using System.Drawing.Imaging;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace DistrictAI.UiTests.Live;

/// <summary>Help desk and Workflows: the logo, a ticket, a workflow's switch and the campaign's question.</summary>
internal sealed partial class LiveWalk
{
    /// <summary>The refusal of a logo that is not a PNG, JPEG or WebP by its bytes (district-ffi desk.rs).</summary>
    private const string LogoRefusal = "The logo must be a PNG, JPEG or WebP image.";

    /// <summary>A whole 1x1 GIF, which the walk saves as logo-gif.png: the picker offers only the logo types, the bytes say GIF.</summary>
    private static readonly byte[] _gif = Convert.FromBase64String("R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==");

    private Outcome Desk(Check check)
    {
        RequireRunId();
        if (Config.DeskCustomerEmail is not { Length: > 0 })
        {
            throw new PreconditionException("live-config.json names no deskCustomerEmail");
        }
        _ = Ui.Go(App, "Help desk", "Help desk");
        if (Ui.TryText(App, "The help desk is off") is not null)
        {
            throw new PreconditionException("the QA workspace's help desk is off");
        }
        var done = new List<string>();

        // Settings: replies are not emailed, then the logo.
        Ui.Press(App, "Help desk settings");
        _ = UiTests.Walk.Heading(App, "Help desk settings", Ui.Step);
        Ui.Loaded(App);
        if (Ui.IsOn(App, "Email customers your replies"))
        {
            throw new PreconditionException("\"Email customers your replies\" is On in the QA workspace, so no reply is sent");
        }
        var hadLogo = Ui.TryText(App, "A logo is published.") is not null;
        var gif = Path.Combine(Folder, "logo-gif.png");
        File.WriteAllBytes(gif, _gif);
        PickFile("Choose a logo image", gif);
        _ = Ui.Text(App, LogoRefusal);
        done.Add("a GIF named .png is refused by its bytes");
        if (hadLogo)
        {
            done.Add("a logo was already published, so none was uploaded or removed");
        }
        else
        {
            var png = Path.Combine(Folder, "logo.png");
            using (var image = new Bitmap(64, 64))
            {
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.FromArgb(0x1f, 0x6f, 0xeb));
                }
                image.Save(png, ImageFormat.Png);
            }
            PickFile("Choose a logo image", png);
            _ = Ui.Text(App, "A logo is published.", TimeSpan.FromSeconds(60));
            check.Shot();
            Ui.Press(App, "Remove the logo");
            _ = Ui.Text(App, "No logo yet.", TimeSpan.FromSeconds(60));
            done.Add("a PNG is accepted and published, then removed");
        }

        // A ticket to our own address, a reply that is not emailed, its status changed, resolved.
        _ = Ui.Go(App, "Help desk", "Help desk");
        var subject = $"{Config.Prefix} ticket";
        Ui.Press(App, "New ticket");
        Ui.Type(App, "Subject", subject);
        Ui.Type(App, "Message", "A ticket the win-smoke walk raises, replies to and resolves.");
        Ui.Type(App, "Customer's name", "win-smoke");
        Ui.Type(App, "Customer's email address", Config.DeskCustomerEmail);
        Ui.Press(App, "Raise the ticket");
        _ = Ui.Find(App, null, "Ticket raised", TimeSpan.FromSeconds(45));
        UiTests.Walk.Activate(Ui.Row(App, subject, TimeSpan.FromSeconds(45)));
        _ = UiTests.Walk.Heading(App, subject, Ui.Step);
        Ui.Type(App, "Reply", "A reply from the win-smoke walk.");
        Ui.Press(App, "Send the reply", allow: "\"Email customers your replies\" was read Off first, so the reply is stored, not emailed");
        _ = Ui.Find(App, null, "Reply sent", TimeSpan.FromSeconds(45));
        Check.Expect(Ui.TryText(App, "The customer was emailed your reply.") is null, "the reply was emailed to the customer");
        _ = Ui.Text(App, "The customer was not emailed this reply.");
        Ui.Press(App, "Mark as Waiting on the customer");
        _ = Ui.Find(App, ControlType.Button, "Mark as Waiting on the customer, current status");
        Ui.Press(App, "Mark as Resolved");
        _ = Ui.Find(App, ControlType.Button, "Mark as Resolved, current status");
        check.Shot();
        done.Add("a ticket raised, replied to without email, marked waiting, then resolved");
        return Outcome.Pass(string.Join("; ", done));
    }

    /// <summary>
    /// Presses <paramref name="button"/>, then in the file dialog it opens
    /// (a window of its own: File name is AutomationId 1148, Open is 1)
    /// picks <paramref name="path"/>.
    /// </summary>
    private void PickFile(string button, string path)
    {
        Ui.Press(App, button);
        var dialog = App.WaitWindow("Open", Ui.Step);
        var name = Wait.For(
            () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("1148").And(cf.ByControlType(ControlType.Edit)))
                ?? dialog.FindFirstDescendant(cf => cf.ByAutomationId("1148")),
            Ui.Step,
            "the file dialog's File name box",
            App.Describe);
        if (name.Patterns.Value.IsSupported)
        {
            name.Patterns.Value.Pattern.SetValue(path);
        }
        else
        {
            name.AsTextBox().Text = path;
        }
        var open = Wait.For(
            () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("1").And(cf.ByControlType(ControlType.Button))),
            Ui.Step,
            "the file dialog's Open button",
            App.Describe);
        open.Patterns.Invoke.Pattern.Invoke();
        Check.Expect(Wait.Until(() => App.TryWindow("Open") is null, Ui.Step), "the file dialog stayed open");
        InstalledApp.Log($"picked {Path.GetFileName(path)}");
    }

    /// <summary>
    /// The first workflow turned off and on again (or on and off), its runs
    /// shown, and the campaign's Pause or Resume question answered Cancel.
    /// </summary>
    private Outcome Workflows(Check check)
    {
        _ = Ui.Go(App, "Workflows", "Workflows");
        var done = new List<string>();
        var missing = new List<string>();

        var toggle = Named(ControlType.Button, "Turn on ").FirstOrDefault(button => button.Patterns.Toggle.IsSupported);
        if (toggle is null)
        {
            missing.Add("the QA workspace has no workflow");
        }
        else
        {
            var switchName = UiTests.Walk.NameOf(toggle);
            var workflow = switchName["Turn on ".Length..];
            var was = Ui.IsOn(App, switchName);
            Ui.SetToggle(App, switchName, !was);
            Check.Expect(Ui.Stays(() => App.TryFind(null, "Could not change a workflow") is not null, TimeSpan.FromSeconds(3)), "\"Could not change a workflow\" after the first toggle");
            Ui.SetToggle(App, switchName, was);
            Check.Expect(Ui.Stays(() => App.TryFind(null, "Could not change a workflow") is not null, TimeSpan.FromSeconds(3)), "\"Could not change a workflow\" after putting it back");
            Ui.Press(App, $"Show runs of {workflow}");
            _ = Wait.For(
                () => App.TryFind(ControlType.ProgressBar, "Reading runs") is null
                    && (Ui.TryText(App, "This workflow has not run yet.") is not null
                        || Named(ControlType.Button, "More runs").Length > 0
                        || App.TryMainWindow()?.FindFirstDescendant(cf => cf.ByName("Hide runs of " + workflow)) is not null)
                    ? (object)true : null,
                Ui.Step,
                $"the runs of {workflow}",
                App.Describe);
            check.Shot();
            Ui.Press(App, $"Hide runs of {workflow}");
            done.Add("the first workflow turned " + (was ? "off and on" : "on and off") + ", its runs shown");
        }

        var campaign = Shows(App, ControlType.Button, "Pause campaign") ? "Pause"
            : Shows(App, ControlType.Button, "Resume campaign") ? "Resume"
            : null;
        if (campaign is null)
        {
            missing.Add("the QA workspace has no campaign");
        }
        else
        {
            Ui.Press(App, $"{campaign} campaign");
            var question = Ui.Dialog(App, $"{campaign} the outbound campaign?");
            check.Shot();
            Ui.PressIn(App, question, "Cancel");
            Check.Expect(Wait.Until(() => !Ui.HasDialog(App, $"{campaign} the outbound campaign?"), Ui.Step), "the campaign's question stayed after Cancel");
            Check.Expect(Shows(App, ControlType.Button, $"{campaign} campaign"), "the campaign changed after Cancel");
            done.Add($"the campaign's {campaign} question appeared and Cancel left it as it was");
        }
        var detail = string.Join("; ", done.Concat(missing));
        return missing.Count == 0 ? Outcome.Pass(detail) : Outcome.NotAutomated(detail);
    }
}
