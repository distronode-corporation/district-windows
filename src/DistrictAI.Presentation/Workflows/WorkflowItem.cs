using System.Collections.ObjectModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Workflows;

/// <summary>One workflow: its switch, the line under it, and its runs while they are open.</summary>
/// <param name="WorkflowId">The workflow.</param>
/// <param name="Name">Its name.</param>
/// <param name="Active">Whether it is on (or the value asked for while a change is on its way).</param>
/// <param name="Detail">What starts it and how its last run went, with the time in this computer's zone.</param>
/// <param name="CanSwitch">Whether its switch works now.</param>
/// <param name="Switching">Whether it is being turned on or off now.</param>
/// <param name="SwitchName">The switch's accessible name, "Turn on" and the name.</param>
/// <param name="Expanded">Whether its runs are open.</param>
/// <param name="Runs">
/// The runs read, newest first, while they are open: the same collection
/// type every other list in the app binds, made once per snapshot and never
/// changed.
/// </param>
/// <param name="RunsLoading">Whether a page of runs is on its way.</param>
/// <param name="RunsNote">"Reading runs." or "This workflow has not run yet.", or empty.</param>
/// <param name="RunsFailure">Why the last page of runs failed, or empty.</param>
/// <param name="CanLoadMore">Whether "More runs" is offered.</param>
/// <param name="MoreLabel">"More runs".</param>
/// <param name="CanRetryRuns">Whether "Try again" is offered for a first page that failed.</param>
public sealed record WorkflowItem(
    string WorkflowId,
    string Name,
    bool Active,
    string Detail,
    bool CanSwitch,
    bool Switching,
    string SwitchName,
    bool Expanded,
    ObservableCollection<RunItem> Runs,
    bool RunsLoading,
    string RunsNote,
    string RunsFailure,
    bool CanLoadMore,
    string MoreLabel,
    bool CanRetryRuns)
{
    /// <summary>The words on the button that opens or closes the runs.</summary>
    public string RunsButtonLabel => Expanded ? "Hide runs" : "Show runs";

    /// <summary>That button's accessible name, which says whose runs.</summary>
    public string RunsButtonName => RunsButtonLabel + " of " + Name;

    /// <summary>Whether there is a <see cref="RunsNote"/>.</summary>
    public bool HasRunsNote => RunsNote.Length > 0;

    /// <summary>Whether there is a <see cref="RunsFailure"/>.</summary>
    public bool HasRunsFailure => RunsFailure.Length > 0;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Name + ", " + (Active ? "on" : "off") + ", " + Detail;

    internal static WorkflowItem From(WorkflowRowView row)
    {
        var when = Display.When(row.LastRunAt);
        var runs = row.Runs;
        return new(
            row.WorkflowId,
            row.Name,
            row.Active,
            when.Length > 0 ? row.Detail + ", " + when : row.Detail,
            row.CanSwitch,
            row.Switching,
            row.SwitchName,
            row.Expanded,
            new(runs is null ? [] : [.. runs.Runs.Select(RunItem.From)]),
            runs?.Loading ?? false,
            runs?.Note ?? string.Empty,
            Display.Failure(runs?.Failure),
            runs?.CanLoadMore ?? false,
            runs?.MoreLabel ?? string.Empty,
            runs?.CanRetry ?? false);
    }

    /// <summary>Equal when every field is, the runs compared one by one, so an unchanged row keeps its place and focus.</summary>
    public bool Equals(WorkflowItem? other) =>
        other is not null
        && WorkflowId == other.WorkflowId
        && Name == other.Name
        && Active == other.Active
        && Detail == other.Detail
        && CanSwitch == other.CanSwitch
        && Switching == other.Switching
        && SwitchName == other.SwitchName
        && Expanded == other.Expanded
        && Runs.SequenceEqual(other.Runs)
        && RunsLoading == other.RunsLoading
        && RunsNote == other.RunsNote
        && RunsFailure == other.RunsFailure
        && CanLoadMore == other.CanLoadMore
        && MoreLabel == other.MoreLabel
        && CanRetryRuns == other.CanRetryRuns;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(WorkflowId, Active, Detail, Expanded, Runs.Count);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>One run of a workflow.</summary>
/// <param name="RunId">The run.</param>
/// <param name="Status">How it went, as a word.</param>
/// <param name="Tone">How the status reads.</param>
/// <param name="When">When it started, in this computer's zone.</param>
/// <param name="Detail">What each action did, and why the run failed.</param>
public sealed record RunItem(string RunId, string Status, RunTone Tone, string When, string Detail)
{
    /// <summary>Whether it worked (the status shows in the success colour).</summary>
    public bool IsSuccess => Tone == RunTone.Success;

    /// <summary>Whether some of it worked (the caution colour).</summary>
    public bool IsWarning => Tone == RunTone.Warning;

    /// <summary>Whether it failed (the critical colour).</summary>
    public bool IsDanger => Tone == RunTone.Danger;

    /// <summary>Neither: skipped, or a word this build does not know (the quiet colour).</summary>
    public bool IsNeutral => !IsSuccess && !IsWarning && !IsDanger;

    /// <summary>What a screen reader says for the run.</summary>
    public string AccessibleName => Detail.Length > 0 ? Status + ", " + When + ", " + Detail : Status + ", " + When;

    internal static RunItem From(RunView run) =>
        new(run.RunId, run.Status, run.Tone, Display.When(run.StartedAt), run.Detail);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}
