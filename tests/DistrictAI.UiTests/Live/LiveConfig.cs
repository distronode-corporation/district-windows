using System.Text.Json;
using System.Text.Json.Serialization;

namespace DistrictAI.UiTests.Live;

/// <summary>
/// live-config.json, which the harness writes in the live walk's folder: what
/// the QA workspace is called, the logins it brokers, and the names this run
/// gives what it makes. None of it is in this public repository.
/// <c>{"workspaceName","logins":["owner","viewer","nows"],"deskCustomerEmail","hqQuestion","roomName","runId"}</c>.
/// </summary>
internal sealed class LiveConfig
{
    /// <summary>The QA workspace's name: the owner's overview heading.</summary>
    [JsonPropertyName("workspaceName")]
    public string WorkspaceName { get; init; } = string.Empty;

    /// <summary>The logins the harness can sign in: owner, viewer, nows (no workspace).</summary>
    [JsonPropertyName("logins")]
    public List<string> Logins { get; init; } = [];

    /// <summary>The address a desk ticket is raised to (our own domain).</summary>
    [JsonPropertyName("deskCustomerEmail")]
    public string DeskCustomerEmail { get; init; } = string.Empty;

    /// <summary>The fixed, read-only question asked of HQ.</summary>
    [JsonPropertyName("hqQuestion")]
    public string HqQuestion { get; init; } = string.Empty;

    /// <summary>The room joined: win-smoke-&lt;run&gt;.</summary>
    [JsonPropertyName("roomName")]
    public string RoomName { get; init; } = string.Empty;

    /// <summary>This run's id, ws-...: everything made is named "win-smoke &lt;runId&gt;".</summary>
    [JsonPropertyName("runId")]
    public string RunId { get; init; } = string.Empty;

    /// <summary>The prefix of every name this run gives what it makes, so the harness's sweep finds leftovers.</summary>
    public string Prefix => $"win-smoke {RunId}";

    /// <summary>Whether the harness offers <paramref name="login"/>.</summary>
    public bool Offers(string login) => Logins.Contains(login, StringComparer.Ordinal);

    /// <summary>Reads <paramref name="path"/>; a missing or unreadable file is an empty config, which the checks report.</summary>
    public static LiveConfig Read(string path)
    {
        if (!File.Exists(path))
        {
            return new LiveConfig();
        }
        try
        {
            return JsonSerializer.Deserialize<LiveConfig>(File.ReadAllText(path)) ?? new LiveConfig();
        }
        catch (JsonException)
        {
            return new LiveConfig();
        }
    }
}
