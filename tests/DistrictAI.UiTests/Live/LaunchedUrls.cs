using System.Text.Json;

namespace DistrictAI.UiTests.Live;

/// <summary>
/// launched-urls.jsonl in the live walk's folder: the harness's URL shim
/// appends <c>{"ts":iso,"url":"..."}</c> for every link the app hands the
/// browser, and starts no browser. The walk reads the lines that came after
/// a press. A link read here is never logged or written anywhere.
/// </summary>
internal sealed class LaunchedUrls(string path)
{
    /// <summary>How many lines the file holds now: the mark to read after.</summary>
    public int Mark() => Lines().Length;

    /// <summary>The links recorded after <paramref name="mark"/>, oldest first.</summary>
    public Uri[] After(int mark) =>
        [.. Lines().Skip(mark).Select(Parse).OfType<Uri>()];

    /// <summary>
    /// The first link after <paramref name="mark"/> that <paramref name="wanted"/>
    /// accepts, waited for.
    /// </summary>
    public Uri WaitFor(int mark, Func<Uri, bool> wanted, TimeSpan timeout, string what) =>
        Wait.For(
            () => After(mark).FirstOrDefault(wanted),
            timeout,
            what,
            () => FormattableString.Invariant($"{After(mark).Length} link(s) recorded after the press, none of them {what} (hosts: {string.Join(", ", After(mark).Select(uri => uri.Host))})"));

    /// <summary>Whether <paramref name="uri"/> is on www.distronode.com under <paramref name="pathPrefix"/>.</summary>
    public static bool IsOurs(Uri uri, string pathPrefix) =>
        uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, "www.distronode.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith(pathPrefix, StringComparison.Ordinal);

    /// <summary>The value of <paramref name="name"/> in <paramref name="uri"/>'s query, or null.</summary>
    public static string? Query(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var key = Uri.UnescapeDataString(equals < 0 ? pair : pair[..equals]);
            if (key == name)
            {
                return equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            }
        }
        return null;
    }

    private string[] Lines()
    {
        if (!File.Exists(path))
        {
            return [];
        }
        // The shim may be appending: read what is there, shared.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static Uri? Parse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("url", out var url)
                && url.GetString() is { Length: > 0 } text
                && Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri : null;
        }
        catch (JsonException)
        {
            // A line still being written.
            return null;
        }
    }
}
