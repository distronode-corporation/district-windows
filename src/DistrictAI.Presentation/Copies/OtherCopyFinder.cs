namespace DistrictAI.ViewModels.Copies;

/// <summary>
/// The packages that handle a URI scheme for this user, by package family
/// name. Windows' answer on the app (<c>OtherCopy</c>); a fake in tests.
/// </summary>
public interface ISchemeHandlers
{
    /// <summary>The package family names of the packaged apps that handle <paramref name="scheme"/>.</summary>
    Task<IReadOnlyList<string>> FamilyNamesForAsync(string scheme, CancellationToken cancellationToken);
}

/// <summary>
/// Finds the other packages that answer the sign-in scheme: the Store copy and
/// the GitHub copy both register <c>districtai:</c>, and with both installed a
/// browser sign-in can go to the wrong one. What a handler means (another copy
/// of this app, or something else) is the core's to say
/// (<c>DistrictFfi.CopiesView</c>); this only asks Windows, once, at start.
/// </summary>
public static class OtherCopyFinder
{
    /// <summary>The scheme both copies register.</summary>
    public const string Scheme = "districtai";

    /// <summary>
    /// The package family names that handle <see cref="Scheme"/> other than
    /// <paramref name="ownFamily"/>, each once, ignoring case as Windows does.
    /// A question Windows could not answer is no other copy found: browser
    /// sign-in is never held on a guess.
    /// </summary>
    public static async Task<IReadOnlyList<string>> OthersAsync(
        ISchemeHandlers handlers,
        string ownFamily,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(ownFamily);
        IReadOnlyList<string> found;
        try
        {
            found = await handlers.FamilyNamesForAsync(Scheme, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return [];
        }
        return [.. found
            .Where(family => !string.IsNullOrWhiteSpace(family))
            .Where(family => !string.Equals(family, ownFamily, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
