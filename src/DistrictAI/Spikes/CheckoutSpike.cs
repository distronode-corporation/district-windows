#if DISTRICT_SPIKES
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace DistrictAI.Spikes;

/// <summary>
/// W2 spike 7: the website's own checkout (and sign-in page) in WebView2,
/// inside an app window, on <c>districtai://spike-checkout</c>. It loads each
/// page, waits for Cloudflare's challenge to clear, and reports whether
/// Stripe.js loaded and Elements rendered, and every console error. It never
/// types into, or submits, anything: no payment of any kind is attempted.
/// </summary>
internal static class CheckoutSpike
{
    private static readonly string[] Pages = ["https://www.distronode.com/checkout", "https://www.distronode.com/login"];

    private const string Probe = """
        JSON.stringify({
          url: location.href,
          title: document.title,
          ready: document.readyState,
          challenge: document.title.indexOf('Just a moment') >= 0
            || !!document.querySelector('#challenge-form, #challenge-running, #challenge-stage'),
          stripe: typeof window.Stripe === 'function',
          stripeFrames: Array.from(document.querySelectorAll('iframe'))
            .filter(f => (f.src || '').indexOf('js.stripe.com') >= 0).length
        })
        """;

    public static async Task RunAsync()
    {
        string runtime;
        try
        {
            runtime = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception error)
        {
            runtime = $"none ({error.GetType().Name} 0x{error.HResult:X8})";
        }
        SpikeLog.Write($"checkout-step runtime={runtime}");
        var view = new WebView2();
        view.CoreWebView2Initialized += (_, args) => SpikeLog.Write(
            $"checkout-step initialized error={(args.Exception is null ? "none" : $"{args.Exception.GetType().Name} 0x{args.Exception.HResult:X8} {args.Exception.Message.ReplaceLineEndings(" ")}")}");
        var window = new Window { Title = "District AI checkout spike", Content = view };
        SpikeLog.Write("checkout-step window-made");
        window.Activate();
        SpikeLog.Write("checkout-step window-shown");
        // Its own user data folder in LocalState, named rather than left to
        // the default, so a failure to make one is reported here.
        var data = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "WebView2");
        // Extra browser flags, when the harness asks for them in
        // LocalState\webview2-args.txt: on the hosted runner the default run
        // failed fast with RPC_S_SERVER_UNAVAILABLE (0x800706BA), and a second
        // run rules out the GPU process and the sandbox. None by default.
        var argsFile = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "webview2-args.txt");
        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = File.Exists(argsFile) ? File.ReadAllText(argsFile).Trim() : string.Empty,
        };
        SpikeLog.Write($"checkout-step browser-args='{options.AdditionalBrowserArguments}'");
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, data, options);
        environment.BrowserProcessExited += (_, args) =>
            SpikeLog.Write($"checkout-step browser-exited kind={args.BrowserProcessExitKind} pid={args.BrowserProcessId}");
        environment.ProcessInfosChanged += (_, _) =>
            SpikeLog.Write($"checkout-step processes={string.Join(",", environment.GetProcessInfos().Select(p => $"{p.Kind}:{p.ProcessId}"))}");
        SpikeLog.Write($"checkout-step environment version={environment.BrowserVersionString}");
        await view.EnsureCoreWebView2Async(environment);
        SpikeLog.Write("checkout-step webview2-ready");
        var core = view.CoreWebView2;
        SpikeLog.Write($"checkout-webview2 version={CoreWebView2Environment.GetAvailableBrowserVersionString()}");

        var errors = new List<string>();
        void Error(string what)
        {
            lock (errors)
            {
                errors.Add(what);
            }
            SpikeLog.Write("checkout-console-error " + what.ReplaceLineEndings(" "));
        }
        await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
        await core.CallDevToolsProtocolMethodAsync("Log.enable", "{}");
        core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled").DevToolsProtocolEventReceived += (_, e) =>
        {
            using var json = JsonDocument.Parse(e.ParameterObjectAsJson);
            if (json.RootElement.GetProperty("type").GetString() is "error" or "assert")
            {
                Error("console: " + Truncate(e.ParameterObjectAsJson));
            }
        };
        core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived += (_, e) =>
            Error("exception: " + Truncate(e.ParameterObjectAsJson));
        core.GetDevToolsProtocolEventReceiver("Log.entryAdded").DevToolsProtocolEventReceived += (_, e) =>
        {
            using var json = JsonDocument.Parse(e.ParameterObjectAsJson);
            if (json.RootElement.GetProperty("entry").GetProperty("level").GetString() == "error")
            {
                Error("log: " + Truncate(e.ParameterObjectAsJson));
            }
        };

        foreach (var page in Pages)
        {
            int before;
            lock (errors)
            {
                before = errors.Count;
            }
            core.Navigate(page);
            var status = "{}";
            var passed = false;
            for (var second = 0; second < 90 && !passed; second++)
            {
                await Task.Delay(1000);
                status = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(Probe)) ?? "{}";
                using var json = JsonDocument.Parse(status);
                var root = json.RootElement;
                var cleared = !root.GetProperty("challenge").GetBoolean() && root.GetProperty("ready").GetString() == "complete";
                passed = page.EndsWith("/checkout", StringComparison.Ordinal)
                    ? cleared && root.GetProperty("stripe").GetBoolean() && root.GetProperty("stripeFrames").GetInt32() > 0
                    : cleared;
            }
            // Let late console errors land before counting.
            await Task.Delay(3000);
            int found;
            lock (errors)
            {
                found = errors.Count - before;
            }
            SpikeLog.Write($"checkout-result page={page} loaded={passed} console_errors={found} status={status}");
        }
        window.Close();
    }

    private static string Truncate(string text) => text.Length <= 400 ? text : text[..400] + "...";
}
#endif
