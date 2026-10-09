using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Workflows;

/// <summary>What kind of line of the workflows list an entry is.</summary>
public enum WorkflowEntryKind
{
    /// <summary>A workflow, with its switch.</summary>
    Workflow,

    /// <summary>One run of the open workflow, under it.</summary>
    Run,

    /// <summary>The end of the open workflow's runs: a note, a failure, "More runs" or "Try again".</summary>
    Footer,
}

/// <summary>
/// One line of the workflows list: a workflow, one of the open workflow's
/// runs, or the end of its runs. The list is flat, every value a plain one,
/// so the page binds it as it binds every other list (one template, no list
/// inside a list) and an unchanged line keeps its place and focus.
/// </summary>
/// <param name="Key">What the line is, unique in the list.</param>
/// <param name="Kind">Which kind of line it is.</param>
/// <param name="WorkflowId">The workflow it belongs to.</param>
/// <param name="Name">The workflow's name.</param>
/// <param name="Active">Whether the workflow is on (or the value asked for while a change is on its way).</param>
/// <param name="Detail">A workflow's trigger and last run (with the time in this computer's zone), or what a run's actions did.</param>
/// <param name="CanSwitch">Whether the workflow's switch works now.</param>
/// <param name="Switching">Whether the workflow is being turned on or off now.</param>
/// <param name="SwitchName">The switch's accessible name, "Turn on" and the name.</param>
/// <param name="Expanded">Whether the workflow's runs are open.</param>
/// <param name="Status">A run's status, as a word.</param>
/// <param name="Tone">How a run's status reads.</param>
/// <param name="When">When a run started, in this computer's zone.</param>
/// <param name="RunsLoading">Whether a page of runs is on its way.</param>
/// <param name="RunsNote">"Reading runs." or "This workflow has not run yet.", or empty.</param>
/// <param name="RunsFailure">Why the last page of runs failed, or empty.</param>
/// <param name="CanLoadMore">Whether "More runs" is offered.</param>
/// <param name="MoreLabel">"More runs".</param>
/// <param name="CanRetryRuns">Whether "Try again" is offered for a first page that failed.</param>
public sealed record WorkflowEntry(
    string Key,
    WorkflowEntryKind Kind,
    string WorkflowId,
    string Name,
    bool Active,
    string Detail,
    bool CanSwitch,
    bool Switching,
    string SwitchName,
    bool Expanded,
    string Status,
    RunTone Tone,
    string When,
    bool RunsLoading,
    string RunsNote,
    string RunsFailure,
    bool CanLoadMore,
    string MoreLabel,
    bool CanRetryRuns)
{
    /// <summary>Whether it is a workflow.</summary>
    public bool IsWorkflow => Kind == WorkflowEntryKind.Workflow;

    /// <summary>Whether it is a run.</summary>
    public bool IsRun => Kind == WorkflowEntryKind.Run;

    /// <summary>Whether it is the end of the runs.</summary>
    public bool IsFooter => Kind == WorkflowEntryKind.Footer;

    /// <summary>The words on the button that opens or closes the runs.</summary>
    public string RunsButtonLabel => Expanded ? "Hide runs" : "Show runs";

    /// <summary>That button's accessible name, which says whose runs.</summary>
    public string RunsButtonName => RunsButtonLabel + " of " + Name;

    /// <summary>Whether a run worked (the status shows in the success colour).</summary>
    public bool IsSuccess => IsRun && Tone == RunTone.Success;

    /// <summary>Whether some of a run worked (the caution colour).</summary>
    public bool IsWarning => IsRun && Tone == RunTone.Warning;

    /// <summary>Whether a run failed (the critical colour).</summary>
    public bool IsDanger => IsRun && Tone == RunTone.Danger;

    /// <summary>A run that is neither: skipped, or a word this build does not know (the quiet colour).</summary>
    public bool IsNeutral => IsRun && !IsSuccess && !IsWarning && !IsDanger;

    /// <summary>Whether there is a <see cref="RunsNote"/>.</summary>
    public bool HasRunsNote => RunsNote.Length > 0;

    /// <summary>Whether there is a <see cref="RunsFailure"/>.</summary>
    public bool HasRunsFailure => RunsFailure.Length > 0;

    /// <summary>What a screen reader says for the line.</summary>
    public string AccessibleName => Kind switch
    {
        WorkflowEntryKind.Workflow => Name + ", " + (Active ? "on" : "off") + ", " + Detail,
        WorkflowEntryKind.Run => string.Join(", ", new[] { Status, When, Detail }.Where(part => part.Length > 0)),
        _ => string.Join(", ", new[] { RunsNote, RunsFailure }.Where(part => part.Length > 0)),
    };

    /// <summary>The lines of <paramref name="row"/>: the workflow, then, while they are open, its runs and their end.</summary>
    internal static IEnumerable<WorkflowEntry> From(WorkflowRowView row)
    {
        var when = Display.When(row.LastRunAt);
        var workflow = new WorkflowEntry(
            "workflow:" + row.WorkflowId,
            WorkflowEntryKind.Workflow,
            row.WorkflowId,
            row.Name,
            row.Active,
            when.Length > 0 ? row.Detail + ", " + when : row.Detail,
            row.CanSwitch,
            row.Switching,
            row.SwitchName,
            row.Expanded,
            string.Empty,
            RunTone.Neutral,
            string.Empty,
            false,
            string.Empty,
            string.Empty,
            false,
            string.Empty,
            false);
        yield return workflow;
        if (row.Runs is not { } runs)
        {
            yield break;
        }
        foreach (var run in runs.Runs)
        {
            yield return workflow with
            {
                Key = "run:" + row.WorkflowId + ":" + run.RunId,
                Kind = WorkflowEntryKind.Run,
                Detail = run.Detail,
                Status = run.Status,
                Tone = run.Tone,
                When = Display.When(run.StartedAt),
            };
        }
        yield return workflow with
        {
            Key = "footer:" + row.WorkflowId,
            Kind = WorkflowEntryKind.Footer,
            Detail = string.Empty,
            RunsLoading = runs.Loading,
            RunsNote = runs.Note ?? string.Empty,
            RunsFailure = Display.Failure(runs.Failure),
            CanLoadMore = runs.CanLoadMore,
            MoreLabel = runs.MoreLabel,
            CanRetryRuns = runs.CanRetry,
        };
    }

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}
