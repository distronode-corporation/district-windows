using FlaUI.Core.Definitions;

namespace DistrictAI.UiTests.Live;

/// <summary>
/// Contacts: one "win-smoke &lt;run&gt;" contact with a 555 number, added,
/// edited, blocked (asked first), found in Blocked callers, unblocked (asked
/// first) and deleted (asked first). Run research is never pressed: it is
/// billed.
/// </summary>
internal sealed partial class LiveWalk
{
    /// <summary>A 555 number (reserved for fiction) in Toronto's area code.</summary>
    private const string ContactNumber = "+14165550142";

    private string ContactName => $"{Config.Prefix} contact";

    private string EditedContactName => $"{Config.Prefix} contact edited";

    private bool _contactMade;

    private Outcome ContactsWrites(Check check)
    {
        RequireRunId();
        _ = Ui.Go(App, "Contacts", "Contacts");
        Ui.Press(App, "Add contact");
        var form = Ui.Dialog(App, "Add contact");
        Ui.Type(App, "Name", ContactName, form);
        Ui.Type(App, "Phone number", ContactNumber, form);
        Ui.PressIn(App, form, "Add");
        Check.Expect(Wait.Until(() => !Ui.HasDialog(App, "Add contact"), Ui.Step), "the Add contact dialog did not close: the contact was not added");
        _contactMade = true;

        OpenContact(ContactName);
        Ui.Press(App, "Edit contact");
        form = Ui.Dialog(App, "Edit contact");
        Ui.Type(App, "Name", EditedContactName, form);
        Ui.PressIn(App, form, "Save");
        _ = UiTests.Walk.Heading(App, EditedContactName, Ui.Step);

        // Block, asked first: the question's title and its button are both "Block".
        Ui.Press(App, "Block");
        Ui.PressIn(App, Ui.Dialog(App, "Block"), "Block");
        _ = Ui.Find(App, ControlType.Button, "Unblock");
        _ = Ui.Find(App, ControlType.Text, "Blocked");
        check.Shot();
        return Outcome.Pass("added with a 555 number, edited, and blocked (asked first); deleted after the blocked list (see contacts-blocked)");
    }

    private Outcome ContactsBlocked(Check check)
    {
        if (!_contactMade)
        {
            throw new PreconditionException("no contact was made to block (see contacts-writes)");
        }
        _ = Ui.Go(App, "Contacts", "Contacts");
        Ui.Press(App, "Blocked callers");
        _ = UiTests.Walk.Heading(App, "Blocked callers", Ui.Step);
        Ui.Loaded(App);
        _ = Ui.Row(App, EditedContactName);
        check.Shot();
        Ui.Press(App, $"Unblock {EditedContactName}");
        Ui.PressIn(App, Ui.Dialog(App, $"Unblock {EditedContactName}?"), "Unblock");
        Check.Expect(
            Wait.Until(() => Ui.TryRow(App, EditedContactName) is null, Ui.Step),
            "the contact stayed in Blocked callers after Unblock");
        return Outcome.Pass("the blocked contact is listed in Blocked callers, and Unblock (asked first) takes it off");
    }

    /// <summary>The last part of contacts-writes: the contact deleted, asked first. Its failure fails contacts-writes.</summary>
    private void ContactsDelete()
    {
        if (!_contactMade)
        {
            return;
        }
        var record = Run("contacts-delete", DefaultCap, check =>
        {
            _ = Ui.Go(App, "Contacts", "Contacts");
            OpenContact(EditedContactName, ContactName);
            Ui.Press(App, "Delete contact");
            Ui.PressIn(App, Ui.Dialog(App, "Delete"), "Delete");
            _ = UiTests.Walk.Heading(App, "Contacts", Ui.Step);
            Check.Expect(
                Ui.Stays(() => Ui.TryRow(App, ContactName) is not null, TimeSpan.FromSeconds(3)),
                "the contact is still listed after Delete");
            return Outcome.Pass("deleted (asked first)");
        });
        // Not a checklist item: its result folds into contacts-writes.
        Results.Remove(record.Id);
        if (record.Result != "PASS")
        {
            Amend("contacts-writes", $"deleting it failed: {record.Detail}");
        }
    }

    /// <summary>Opens the contact whose row starts with one of <paramref name="names"/>, loading more of the list as needed.</summary>
    private void OpenContact(params string[] names)
    {
        for (var page = 0; ; page++)
        {
            var row = Wait.Until(() => names.Any(name => Ui.TryRow(App, name) is not null), TimeSpan.FromSeconds(page == 0 ? 15 : 5))
                ? names.Select(name => Ui.TryRow(App, name)).First(found => found is not null)
                : null;
            if (row is not null)
            {
                UiTests.Walk.Activate(row);
                _ = Wait.For(
                    () => names.Select(name => UiTests.Walk.TryHeading(App, name)).FirstOrDefault(found => found is not null),
                    Ui.Step,
                    $"the contact page of \"{names[0]}\"",
                    App.Describe);
                return;
            }
            if (page >= 10 || App.TryFind(ControlType.Button, "Load more contacts") is null)
            {
                throw new CheckFailedException($"\"{names[0]}\" is not in the contact list");
            }
            Ui.Press(App, "Load more contacts");
        }
    }

    private void RequireRunId()
    {
        if (Config.RunId is not { Length: > 0 })
        {
            throw new PreconditionException("live-config.json names no runId, so nothing can be named for the sweep");
        }
    }
}
