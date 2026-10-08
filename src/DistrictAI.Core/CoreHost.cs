using DistrictAI.Core.Ffi;

namespace DistrictAI.Core;

// The generated `Core` class shares its simple name with this namespace's last
// segment, and C# finds the namespace first, so it is named by alias here.
using FfiCore = Ffi.Core;

/// <summary>
/// Runs work on the UI thread. In the app this is the window's
/// <c>DispatcherQueue</c>; in tests, a queue the test drains.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Queues <paramref name="work"/>; false when the queue has shut down.</summary>
    bool TryEnqueue(Action work);
}

/// <summary>Opens a page in the user's own browser.</summary>
public interface IBrowser
{
    /// <summary>Opens <paramref name="url"/>, and answers whether a browser took it.</summary>
    Task<bool> OpenAsync(string url);
}

/// <summary>What the window shows, as of one revision of the core's snapshot.</summary>
/// <param name="Revision">The snapshot's revision.</param>
/// <param name="Shell">The window's frame.</param>
/// <param name="Screen">The screen inside it.</param>
public sealed record CoreSnapshot(ulong Revision, ShellView Shell, ScreenView Screen);

/// <summary>
/// The app's one core: starts it, forwards what the window sends, and turns the
/// core's <c>state_changed</c> calls into at most one <see cref="Changed"/> per
/// turn of the UI thread.
/// </summary>
/// <remarks>
/// The core calls <see cref="StateChanged"/> from its own threads, as often as
/// its model changes. Only the first call since the last render queues one; the
/// render reads the snapshot once, so a burst of changes costs one redraw.
/// </remarks>
public sealed class CoreHost : UiHost, IAsyncDisposable
{
    private readonly FfiCore _core = new();
    private readonly IUiDispatcher _dispatcher;
    private readonly IBrowser _browser;
    private int _queued;
    private ulong _rendered;
    private int _disposed;

    /// <summary>A host that renders on <paramref name="dispatcher"/> and opens pages with <paramref name="browser"/>.</summary>
    public CoreHost(IUiDispatcher dispatcher, IBrowser browser)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(browser);
        _dispatcher = dispatcher;
        _browser = browser;
    }

    /// <summary>Raised on the UI thread with each new snapshot.</summary>
    public event EventHandler<CoreSnapshot>? Changed;

    /// <summary>
    /// Raised on the UI thread when the core shows a notification: show it as a
    /// toast, replacing any with the same <see cref="NotificationView.Id"/>, and
    /// hand its activation back through <see cref="ActivateNotification"/>.
    /// </summary>
    public event EventHandler<NotificationView>? NotificationRequested;

    /// <summary>Raised on the UI thread with the id of a notification to take away.</summary>
    public event EventHandler<string>? NotificationWithdrawn;

    /// <summary>Raised on the UI thread when a call starts ringing here: loop the ringtone.</summary>
    public event EventHandler? RingtoneStarted;

    /// <summary>Raised on the UI thread when the ringtone should stop.</summary>
    public event EventHandler? RingtoneStopped;

    /// <summary>Raised on the UI thread when the window should come forward: shown, restored, raised and focused.</summary>
    public event EventHandler? PresentWindowRequested;

    /// <summary>The latest snapshot, read now.</summary>
    public CoreSnapshot Current => new(_core.Revision(), _core.Shell(), _core.Screen());

    /// <summary>
    /// Starts the core, with its files in <paramref name="dataDir"/> (the
    /// package's LocalState) and its credentials under
    /// <paramref name="credentialNamespace"/> (the package family name).
    /// </summary>
    /// <exception cref="StartException">The core could not start.</exception>
    public void Start(string dataDir, string appVersion, string? deviceName, string credentialNamespace) =>
        _core.Start(new StartConfig(dataDir, appVersion, deviceName, credentialNamespace), this);

    /// <summary>Forwards something the user did.</summary>
    public void Send(UiEvent uiEvent) => _core.Send(uiEvent);

    /// <summary>Forwards a <c>districtai:</c> link the app was activated with.</summary>
    public LinkKind OpenLink(string uri) => _core.OpenLink(uri);

    /// <summary>Forwards the machine going to sleep or waking.</summary>
    public void Power(PowerChange change) => _core.Power(change);

    /// <summary>
    /// Forwards a toast's activation: the toast itself clicked
    /// (<paramref name="actionId"/> null), or the button whose
    /// <see cref="NotificationActionView.ActionId"/> it is.
    /// </summary>
    public void ActivateNotification(string id, string? actionId) => _core.ActivateNotification(id, actionId);

    /// <inheritdoc/>
    public void StateChanged(ulong revision)
    {
        if (Interlocked.Exchange(ref _queued, 1) == 1)
        {
            return;
        }
        if (!_dispatcher.TryEnqueue(Render))
        {
            // The UI thread is gone; the next change tries again.
            Volatile.Write(ref _queued, 0);
        }
    }

    /// <inheritdoc/>
    public Task<bool> OpenUrl(string url) => _browser.OpenAsync(url);

    /// <inheritdoc/>
    public void Notify(NotificationView notification) =>
        OnUiThread(() => NotificationRequested?.Invoke(this, notification));

    /// <inheritdoc/>
    public void Withdraw(string id) => OnUiThread(() => NotificationWithdrawn?.Invoke(this, id));

    /// <inheritdoc/>
    public void StartRingtone() => OnUiThread(() => RingtoneStarted?.Invoke(this, EventArgs.Empty));

    /// <inheritdoc/>
    public void StopRingtone() => OnUiThread(() => RingtoneStopped?.Invoke(this, EventArgs.Empty));

    /// <inheritdoc/>
    public void PresentWindow() => OnUiThread(() => PresentWindowRequested?.Invoke(this, EventArgs.Empty));

    /// <summary>
    /// Runs <paramref name="raise"/> on the UI thread, in the order the core
    /// asked. A UI thread that has shut down takes nothing: the window those
    /// requests were for is gone.
    /// </summary>
    private void OnUiThread(Action raise) => _dispatcher.TryEnqueue(raise);

    private void Render()
    {
        // Cleared before reading, so a change that lands after the read queues
        // another render instead of being lost.
        Volatile.Write(ref _queued, 0);
        var snapshot = Current;
        if (snapshot.Revision == _rendered)
        {
            return;
        }
        _rendered = snapshot.Revision;
        Changed?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Gives the core its last word (saving the session) and stops it. Safe to
    /// call more than once.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }
        await _core.Shutdown().ConfigureAwait(false);
        _core.Dispose();
    }
}
