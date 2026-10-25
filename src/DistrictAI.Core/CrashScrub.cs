using System.Text;
using System.Text.Json.Nodes;

namespace DistrictAI.Core;

/// <summary>
/// What leaves the machine in a .NET crash report: the same scrub the core's
/// district-crash applies to the Rust side's reports (its src/scrub.rs), so
/// both say the same thing. The user's home directory never leaves (the name
/// in it is replaced by <see cref="User"/>), and neither does the machine's
/// name, a user or a request (where an address would be). The app's
/// <c>CrashReporter</c> hands every report, as JSON, to <see cref="Event"/> in
/// the Sentry SDK's <c>BeforeSend</c>, and drops the report when it cannot.
/// </summary>
public static class CrashScrub
{
    /// <summary>What stands in for the user's name in a home directory path.</summary>
    public const string User = "<user>";

    /// <summary>
    /// <paramref name="report"/>, a Sentry event as JSON, scrubbed in place:
    /// <c>server_name</c>, <c>user</c>, <c>request</c> and the device's
    /// <c>name</c> removed, then every home directory path in every string of
    /// it, keys included, passed through <see cref="HomePaths"/>. The walk is
    /// over the whole report rather than a list of fields, so a field the SDK
    /// adds later is scrubbed too.
    /// </summary>
    public static void Event(JsonObject report)
    {
        ArgumentNullException.ThrowIfNull(report);
        report.Remove("server_name");
        report.Remove("user");
        report.Remove("request");
        if (report["contexts"] is JsonObject contexts && contexts["device"] is JsonObject device)
        {
            device.Remove("name");
        }
        Walk(report);
    }

    /// <summary>
    /// <paramref name="text"/> with the user's name in every home directory
    /// path replaced by <see cref="User"/>: <c>/home/name</c> (Linux),
    /// <c>/Users/name</c> (macOS) and <c>drive:\Users\name</c> (Windows, either
    /// slash, any case). The name runs to the next path separator or to a
    /// character no user name may hold; on the Windows form a space does not
    /// end it (Windows names may hold one), on the others it does.
    /// </summary>
    public static string HomePaths(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        StringBuilder? clean = null;
        var copied = 0;
        var at = 0;
        while (at < text.Length)
        {
            if (HomeAt(text, at) is not { } home)
            {
                at++;
                continue;
            }
            var (start, windows) = home;
            var end = start;
            while (end < text.Length && !EndsName(text[end], windows))
            {
                end++;
            }
            if (end > start)
            {
                clean ??= new StringBuilder(text.Length);
                clean.Append(text, copied, start - copied).Append(User);
                copied = end;
            }
            at = end;
        }
        return clean is null ? text : clean.Append(text, copied, text.Length - copied).ToString();
    }

    private static void Walk(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject members:
                var entries = members.ToList();
                members.Clear();
                foreach (var (key, value) in entries)
                {
                    members[HomePaths(key)] = Clean(value);
                }
                break;
            case JsonArray items:
                for (var index = 0; index < items.Count; index++)
                {
                    if (items[index] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        items[index] = JsonValue.Create(HomePaths(text));
                    }
                    else
                    {
                        Walk(items[index]);
                    }
                }
                break;
            default:
                break;
        }
    }

    // A string, scrubbed; anything else, walked.
    private static JsonNode? Clean(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return JsonValue.Create(HomePaths(text));
        }
        Walk(node);
        return node;
    }

    // Where the user's name starts if a home directory begins at `at`, and
    // whether it is the Windows form.
    private static (int Start, bool Windows)? HomeAt(string text, int at)
    {
        var rest = text.AsSpan(at);
        if (rest.StartsWith("/home/", StringComparison.Ordinal))
        {
            return (at + 6, false);
        }
        if (rest.Length < 7)
        {
            return null;
        }
        var open = rest[0];
        var close = rest[6];
        var word = rest.Slice(1, 5);
        if (!(IsSeparator(open) && IsSeparator(close) && IsUsersIgnoringAsciiCase(word)))
        {
            return null;
        }
        var drive = at >= 2 && text[at - 1] == ':' && char.IsAsciiLetter(text[at - 2]);
        var windows = drive || open == '\\' || close == '\\';
        // Without a drive or a backslash it is macOS, whose folder is `Users`
        // exactly; a lower case `/users/` is more likely part of an address.
        return windows || word.SequenceEqual("Users") ? (at + 7, windows) : null;
    }

    // "users" in any ASCII case, and only ASCII: as the core compares it.
    private static bool IsUsersIgnoringAsciiCase(ReadOnlySpan<char> word)
    {
        const string Users = "users";
        for (var index = 0; index < Users.Length; index++)
        {
            var c = word[index];
            if (!char.IsAsciiLetter(c) || (char)(c | 0x20) != Users[index])
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsSeparator(char c) => c is '/' or '\\';

    // Whether `c` cannot be part of a user's name, so ends it. Windows refuses
    // " / \ [ ] : ; | = , + * ? < > in a name; Linux and macOS names hold no
    // space, quote or bracket either.
    private static bool EndsName(char c, bool windows) =>
        "/\\\"[]:;|=,+*?<>".Contains(c, StringComparison.Ordinal)
        || c < ' '
        || c == '\u007f'
        || (!windows && " '`()".Contains(c, StringComparison.Ordinal));
}
