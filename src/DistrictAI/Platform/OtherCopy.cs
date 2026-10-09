using System.Runtime.InteropServices;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Copies;
using Windows.System;

namespace DistrictAI.Platform;

/// <summary>
/// Whether the other copy of the app (the Store's, or the GitHub one) is
/// installed for this user beside this one. Both register <c>districtai:</c>,
/// so Windows is asked which packages handle it: the cheapest reliable
/// question, it needs no capability, and it asks exactly what matters, which
/// apps a browser's sign-in answer could reach.
/// </summary>
internal sealed class OtherCopy : ISchemeHandlers
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> FamilyNamesForAsync(string scheme, CancellationToken cancellationToken)
    {
        var handlers = await Launcher.FindUriSchemeHandlersAsync(scheme).AsTask(cancellationToken).ConfigureAwait(false);
        return [.. handlers.Select(app => app.PackageFamilyName ?? string.Empty)];
    }

    /// <summary>
    /// What the window says about the copies installed, for this copy
    /// (<paramref name="ownFamily"/>). Asked once, at start; a failure to ask
    /// is no other copy found (<see cref="OtherCopyFinder"/>).
    /// </summary>
    public static async Task<CopiesView> CheckAsync(string ownFamily)
    {
        IReadOnlyList<string> others;
        try
        {
            others = await OtherCopyFinder.OthersAsync(new OtherCopy(), ownFamily).ConfigureAwait(false);
        }
        catch (COMException)
        {
            others = [];
        }
        return DistrictFfi.CopiesView(ownFamily, [.. others]);
    }
}
