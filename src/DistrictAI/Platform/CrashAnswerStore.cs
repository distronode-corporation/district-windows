using System.Diagnostics;
using System.Runtime.InteropServices;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Windows.Storage;

namespace DistrictAI.Platform;

/// <summary>
/// The user's answer about crash reports, in the package's local settings
/// under <see cref="Key"/>: <c>granted</c> or <c>denied</c>. No value is no
/// answer, so a new install and an install upgraded from a version that never
/// asked are both asked once. Local settings survive an update and go with an
/// uninstall; each copy of the app (the Store's, the GitHub flavour) keeps its
/// own.
/// </summary>
internal sealed class CrashAnswerStore : ICrashAnswerStore
{
    /// <summary>The local settings key.</summary>
    internal const string Key = "CrashReports";

    private const string Granted = "granted";
    private const string Denied = "denied";

    /// <inheritdoc/>
    public CrashConsent? Read()
    {
        try
        {
            if (!ApplicationData.Current.LocalSettings.Values.TryGetValue(Key, out var value) || value is not string word)
            {
                return null;
            }
            return word switch
            {
                Granted => CrashConsent.Granted,
                Denied => CrashConsent.Denied,
                _ => null,
            };
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Unreadable is no answer: the question comes again.
            Trace.TraceWarning($"District AI: the crash reports answer could not be read: {error.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public bool Write(CrashConsent consent)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[Key] = consent == CrashConsent.Granted ? Granted : Denied;
            return true;
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"District AI: the crash reports answer could not be kept: {error.Message}");
            return false;
        }
    }
}
