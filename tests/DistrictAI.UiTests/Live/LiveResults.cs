using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DistrictAI.UiTests.Live;

/// <summary>What one check of the live walk came to.</summary>
internal enum CheckResult
{
    /// <summary>Checked, and it held.</summary>
    Pass,
    /// <summary>Checked, and it did not hold.</summary>
    Fail,
    /// <summary>Not checked: a precondition was missing or time ran out (the detail says which).</summary>
    NotAutomated,
    /// <summary>Tried, and reported for a person to judge (screenshots, or a part of the platform the VM cannot vouch for).</summary>
    Probe,
}

/// <summary>One check's record in live-results.json.</summary>
internal sealed class CheckRecord
{
    /// <summary>The checklist's item id.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>PASS, FAIL, NOT_AUTOMATED or PROBE.</summary>
    [JsonPropertyName("result")]
    public string Result { get; set; } = "NOT_AUTOMATED";

    /// <summary>What was seen, at most 400 characters, links taken out.</summary>
    [JsonPropertyName("detail")]
    public string Detail { get; set; } = string.Empty;

    /// <summary>How long the check took, in whole seconds.</summary>
    [JsonPropertyName("seconds")]
    public int Seconds { get; set; }

    /// <summary>The check's screenshots, by file name under screenshots/.</summary>
    [JsonPropertyName("screenshots")]
    public List<string> Screenshots { get; } = [];
}

/// <summary>
/// live-results.json in the live walk's folder, rewritten whole after every
/// check (to a temporary file, then moved over it), so a walk cut short
/// still leaves every check it finished:
/// <c>{"schema":1,"started":iso,"finished":iso|null,"checks":[{"id","result","detail","screenshots"}]}</c>.
/// </summary>
internal sealed class LiveResults(string path)
{
    /// <summary>The file's shape; the harness refuses another.</summary>
    public const int Schema = 1;

    /// <summary>The longest detail kept.</summary>
    public const int DetailLimit = 400;

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    private string _started = Now();
    private readonly List<CheckRecord> _checks = [];
    private string? _finished;

    /// <summary>
    /// The results at <paramref name="path"/>, carrying on from what the
    /// file already holds (the two-copies run comes after the walk's).
    /// </summary>
    public static LiveResults Open(string path)
    {
        var results = new LiveResults(path);
        if (!File.Exists(path))
        {
            return results;
        }
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.TryGetProperty("started", out var started) && started.GetString() is { } text)
            {
                results._started = text;
            }
            if (root.TryGetProperty("checks", out var checks))
            {
                foreach (var check in checks.EnumerateArray())
                {
                    var record = results.For(check.GetProperty("id").GetString() ?? "?");
                    record.Result = check.GetProperty("result").GetString() ?? "NOT_AUTOMATED";
                    record.Detail = check.GetProperty("detail").GetString() ?? string.Empty;
                    record.Seconds = check.TryGetProperty("seconds", out var seconds) && seconds.TryGetInt32(out var whole) ? whole : 0;
                    if (check.TryGetProperty("screenshots", out var shots))
                    {
                        record.Screenshots.AddRange(shots.EnumerateArray().Select(shot => shot.GetString() ?? string.Empty).Where(shot => shot.Length > 0));
                    }
                }
            }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            InstalledApp.Log($"{Path.GetFileName(path)} could not be read ({error.Message}): starting it again");
            results._checks.Clear();
        }
        return results;
    }

    /// <summary>The checks recorded so far, in order.</summary>
    public IReadOnlyList<CheckRecord> Checks => _checks;

    /// <summary>The record for <paramref name="id"/>, made (NOT_AUTOMATED, no detail) if there is none yet.</summary>
    public CheckRecord For(string id)
    {
        if (_checks.FirstOrDefault(check => check.Id == id) is { } found)
        {
            return found;
        }
        var made = new CheckRecord { Id = id };
        _checks.Add(made);
        return made;
    }

    /// <summary>Takes <paramref name="id"/>'s record out (a step that folds into another check), and writes the file.</summary>
    public void Remove(string id)
    {
        _ = _checks.RemoveAll(check => check.Id == id);
        Write();
    }

    /// <summary>Sets <paramref name="id"/>'s result and detail, and writes the file.</summary>
    public CheckRecord Record(string id, CheckResult result, string detail, TimeSpan? took = null)
    {
        var check = For(id);
        check.Seconds = (int)Math.Round((took ?? TimeSpan.Zero).TotalSeconds);
        check.Result = Wire(result);
        check.Detail = Clean(detail);
        Write();
        return check;
    }

    /// <summary>Marks the walk finished, and writes the file.</summary>
    public void Finish()
    {
        _finished = Now();
        Write();
    }

    /// <summary>Writes the file as it stands.</summary>
    public void Write()
    {
        var document = new Dictionary<string, object?>
        {
            ["schema"] = Schema,
            ["started"] = _started,
            ["finished"] = _finished,
            ["checks"] = _checks,
        };
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, _json));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The wire name of <paramref name="result"/>.</summary>
    public static string Wire(CheckResult result) => result switch
    {
        CheckResult.Pass => "PASS",
        CheckResult.Fail => "FAIL",
        CheckResult.Probe => "PROBE",
        _ => "NOT_AUTOMATED",
    };

    /// <summary>A detail as the file holds it: one line, links taken out, at most <see cref="DetailLimit"/> characters.</summary>
    public static string Clean(string detail)
    {
        var line = InstalledApp.Redact(detail).ReplaceLineEndings(" ").Trim();
        return line.Length <= DetailLimit ? line : string.Concat(line.AsSpan(0, DetailLimit - 3), "...");
    }

    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
