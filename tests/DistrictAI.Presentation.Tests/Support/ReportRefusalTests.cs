using DistrictAI.Core.Ffi;
using Xunit;

namespace DistrictAI.Presentation.Tests.Support;

// While a support request is being written in Support, the report dialog does
// not send and keeps its note (ReportDialog.OfferAsync): what the page context
// holds for it.
public sealed class ReportRefusalTests
{
    private static readonly ReportTarget _call = new ReportTarget.Call("call-1");

    [Fact]
    public void ARefusedReportsNoteIsHandedBackOnceForItsTarget()
    {
        var (context, sink) = Pages.Context();
        Assert.Null(context.ReportRefusal);
        context.ReportRefusal = "Send or discard the support request you are writing in Support, then report this.";

        context.KeepReportNote(_call, "Wrong caller name.");
        Assert.Equal(string.Empty, context.TakeReportNote(new ReportTarget.Contact("contact-1")));
        Assert.Equal("Wrong caller name.", context.TakeReportNote(new ReportTarget.Call("call-1")));
        Assert.Equal(string.Empty, context.TakeReportNote(_call));
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void AnEmptyNoteKeepsNothing()
    {
        var (context, _) = Pages.Context();
        context.KeepReportNote(_call, "First.");
        context.KeepReportNote(_call, string.Empty);
        Assert.Equal(string.Empty, context.TakeReportNote(_call));
    }
}
