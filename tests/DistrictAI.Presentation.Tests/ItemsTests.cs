using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Xunit;

namespace DistrictAI.Presentation.Tests;

public sealed class ItemsTests
{
    private const string Then = "2020-01-02T03:04:05Z";

    [Fact]
    public void AFactIsItsLabelAndValue()
    {
        var fact = FactItem.From(new FactView("Carrier", "Example Mobile"));
        Assert.Equal(new FactItem("Carrier", "Example Mobile"), fact);
        Assert.Equal("Carrier: Example Mobile", fact.AccessibleName);
        Assert.Equal(fact.AccessibleName, fact.ToString());
    }

    [Fact]
    public void ACallRowWithASummary()
    {
        var row = CallRowItem.From(V.CallRow(summary: "Booked a visit."));
        Assert.Equal("call-1", row.CallId);
        Assert.True(row.HasSummary);
        Assert.Equal(Display.When(Then), row.When);
        Assert.Equal("Alex, Inbound, " + row.When, row.AccessibleName);
        Assert.Equal(row.AccessibleName, row.ToString());
    }

    [Fact]
    public void ACallRowWithoutASummaryOrAPartLeavesThemOut()
    {
        var row = CallRowItem.From(V.CallRow(detail: string.Empty, startedAt: "never"));
        Assert.Equal(string.Empty, row.Summary);
        Assert.False(row.HasSummary);
        Assert.Equal("Alex", row.AccessibleName);
    }

    [Fact]
    public void AThreadRowThatOpensIsOpaqueAndSaysUnread()
    {
        var row = ThreadRowItem.From(V.ThreadRow(unread: true, lastAt: Then));
        Assert.Equal(1.0, row.RowOpacity);
        Assert.Equal("Alex, unread, Text message, Hello, " + Display.When(Then), row.AccessibleName);
        Assert.Equal(row.AccessibleName, row.ToString());
    }

    [Fact]
    public void AThreadRowThatCannotOpenIsFaded()
    {
        var row = ThreadRowItem.From(V.ThreadRow(canOpen: false));
        Assert.Equal(0.6, row.RowOpacity);
        Assert.Equal("Alex, Text message, Hello", row.AccessibleName);
    }

    [Fact]
    public void ASearchHit()
    {
        var hit = SearchHitItem.From(V.Hit(at: Then));
        Assert.Equal(1.0, hit.RowOpacity);
        Assert.Equal("Alex, the match, " + Display.When(Then), hit.AccessibleName);
        Assert.Equal(hit.AccessibleName, hit.ToString());

        var faded = SearchHitItem.From(V.Hit(canOpen: false));
        Assert.Equal(0.6, faded.RowOpacity);
        Assert.Equal("Alex, the match", faded.AccessibleName);
    }

    [Fact]
    public void AnOutboundMessageWithAttachmentsAndDelivery()
    {
        var item = TimelineItem.From(
            new TimelineItemView(
                "e-1",
                new TimelineKind.Message(
                    Outbound: true,
                    Author: "Sam",
                    Body: "See attached.",
                    Attachments: ["a.pdf", "b.png"],
                    Subject: "Quote",
                    ChannelLabel: "Email",
                    Delivery: "Delivered",
                    DeliveryFailed: false,
                    AttachmentsLabel: "2 attachments"),
                Then,
                ReportAvailability.InApp),
            reportEnabled: true);

        Assert.Equal("e-1", item.Id);
        Assert.True(item.IsOutboundMessage);
        Assert.False(item.IsInboundMessage);
        Assert.Equal("a.pdf" + Environment.NewLine + "b.png", item.Attachments);
        Assert.Equal("Email \u00B7 Delivered", item.Meta);
        Assert.True(item.HasSubject);
        Assert.True(item.HasMeta);
        Assert.True(item.HasAuthor);
        Assert.True(item.HasAttachments);
        Assert.False(item.HasSummary);
        Assert.False(item.IsNote);
        Assert.True(item.HasWhen);
        Assert.Equal("Report", item.ReportLabel);
        Assert.True(item.ReportVisible);
        Assert.True(item.ReportEnabled);
        // A sent message is "You", whoever wrote it.
        Assert.StartsWith("You, Quote, See attached., ", item.AccessibleName, StringComparison.Ordinal);
        Assert.Equal(item.AccessibleName, item.ToString());
    }

    [Fact]
    public void AnInboundMessageWithOnlyAnAttachmentsLabel()
    {
        var item = TimelineItem.From(
            new TimelineItemView(
                "e-2",
                new TimelineKind.Message(false, null, "Hi", [], null, "Text message", null, false, "1 attachment"),
                null,
                ReportAvailability.Hidden),
            reportEnabled: false);

        Assert.True(item.IsInboundMessage);
        Assert.Equal(string.Empty, item.Author);
        Assert.False(item.HasAuthor);
        Assert.Equal(string.Empty, item.Subject);
        Assert.Equal("1 attachment", item.Attachments);
        Assert.Equal("Text message", item.Meta);
        Assert.False(item.HasWhen);
        Assert.False(item.ReportVisible);
        Assert.Equal("Hi, 1 attachment, Text message", item.AccessibleName);
    }

    [Fact]
    public void AMessageWithNoAttachmentsAtAll()
    {
        var item = TimelineItem.From(
            new TimelineItemView("e-3", new TimelineKind.Message(false, "Alex", "Hi", [], null, "Text message", null, false, null), null, ReportAvailability.Hidden),
            reportEnabled: true);
        Assert.Equal(string.Empty, item.Attachments);
        Assert.Equal("Alex, Hi, Text message", item.AccessibleName);
    }

    [Fact]
    public void ACallInTheConversationWithItsSummary()
    {
        var item = TimelineItem.From(
            new TimelineItemView("e-4", new TimelineKind.CallEvent(null, "Inbound call, 2m", new AiTextView("AI summary", "They booked."), false), null, ReportAvailability.OnWeb),
            reportEnabled: true);

        Assert.True(item.IsCallEvent);
        Assert.Equal("Inbound call, 2m", item.CallTitle);
        Assert.True(item.HasSummary);
        Assert.Equal("Report on the web", item.ReportLabel);
        Assert.Equal("Inbound call, 2m, AI summary: They booked.", item.AccessibleName);
    }

    [Fact]
    public void ACallInTheConversationWithoutASummary()
    {
        var item = TimelineItem.From(
            new TimelineItemView("e-5", new TimelineKind.CallEvent(null, "Missed call", null, true), null, ReportAvailability.Hidden),
            reportEnabled: true);
        Assert.Equal(string.Empty, item.SummaryLabel);
        Assert.Equal(string.Empty, item.SummaryText);
        Assert.False(item.HasSummary);
        Assert.Equal("Missed call", item.AccessibleName);
    }

    [Fact]
    public void ANote()
    {
        var item = TimelineItem.From(new TimelineItemView("e-6", new TimelineKind.Note("Assigned to Sam"), null, ReportAvailability.Hidden), reportEnabled: true);
        Assert.True(item.IsNote);
        Assert.Equal("Assigned to Sam", item.AccessibleName);
    }

    [Fact]
    public void AKindALaterCoreAddsIsItsTimeAlone()
    {
        var item = TimelineItem.From(new TimelineItemView("e-7", new TimelineKind(), Then, ReportAvailability.InApp), reportEnabled: true);
        Assert.Equal("e-7", item.Id);
        Assert.Equal(Display.When(Then), item.When);
        Assert.False(item.ReportVisible);
        Assert.False(item.ReportEnabled);
        Assert.Equal(item.When, item.AccessibleName);
    }

    [Fact]
    public void AContactRowWithAndWithoutADetail()
    {
        var row = ContactRowItem.From(new ContactRowView("c-1", "Alex", "+1 212 555 0100"));
        Assert.True(row.HasDetail);
        Assert.Equal("Alex, +1 212 555 0100", row.AccessibleName);
        Assert.Equal(row.AccessibleName, row.ToString());

        var bare = ContactRowItem.From(new ContactRowView("c-2", "Sam", string.Empty));
        Assert.False(bare.HasDetail);
        Assert.Equal("Sam", bare.AccessibleName);
    }

    [Fact]
    public void ADeviceRowSaysWhenItWasLastActiveInLocalTime()
    {
        var row = DeviceRowItem.From(new DeviceRowView("d-1", "Desk PC", "Windows", "Signed in recently", true, Then), busy: false);
        Assert.Equal("Windows \u00B7 Last active " + Display.When(Then), row.Detail);
        Assert.True(row.CanSignOut);
        Assert.Equal("Sign out Desk PC", row.SignOutName);
        Assert.Equal("Desk PC, this device, " + row.Detail, row.AccessibleName);
        Assert.Equal(row.AccessibleName, row.ToString());
    }

    [Fact]
    public void ADeviceRowWithNoTimeUsesTheServicesWords()
    {
        var row = DeviceRowItem.From(new DeviceRowView("d-2", "Phone", "iOS", "Signed in recently", false, null), busy: true);
        Assert.Equal("iOS \u00B7 Signed in recently", row.Detail);
        Assert.False(row.CanSignOut);
        Assert.Equal("Phone, " + row.Detail, row.AccessibleName);
    }

    [Fact]
    public void AWorkspaceIsItsNameAndTheRole()
    {
        Assert.Equal("Bravo, Client", WorkspaceItem.From(new WorkspaceEntryView("w-1", "Bravo", "Client")).ToString());
        Assert.Equal("Bravo", WorkspaceItem.From(new WorkspaceEntryView("w-1", "Bravo", string.Empty)).ToString());
    }
}
