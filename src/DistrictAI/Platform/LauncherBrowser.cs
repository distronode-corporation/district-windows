using DistrictAI.Core;
using Microsoft.UI.Dispatching;
using Windows.System;

namespace DistrictAI.Platform;

/// <summary>
/// <see cref="IBrowser"/> over <see cref="Launcher"/>: the user's default
/// browser, never a view inside the app, so the page has the browser's own
/// session and the app sees nothing of it.
/// </summary>
internal sealed class LauncherBrowser(DispatcherQueue queue) : IBrowser
{
    public Task<bool> OpenAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return Task.FromResult(false);
        }
        // The core asks from its own threads; the launch is made from the UI
        // thread, where Windows expects it.
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = queue.TryEnqueue(async () =>
        {
            try
            {
                opened.SetResult(await Launcher.LaunchUriAsync(uri));
            }
            catch (Exception error) when (error is UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                opened.SetResult(false);
            }
        });
        return queued ? opened.Task : Task.FromResult(false);
    }
}
