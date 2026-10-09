using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Settings;

/// <summary>One row of the hub: a section to open.</summary>
/// <param name="Section">The section it opens.</param>
/// <param name="Title">Its title.</param>
/// <param name="Subtitle">What the section holds.</param>
public sealed record SettingsRowItem(SettingsSection Section, string Title, string Subtitle)
{
    /// <summary>What a screen reader says for the row: its title, then what it holds.</summary>
    public string AccessibleName => Title + ". " + Subtitle;
}

/// <summary>A group of the hub, under its heading.</summary>
/// <param name="Heading">The heading ("District Studio", "Workspace").</param>
/// <param name="Rows">Its rows, in the core's order.</param>
public sealed record SettingsGroupItem(string Heading, IReadOnlyList<SettingsRowItem> Rows)
{
    /// <summary>The same group when it has the same heading and the same rows.</summary>
    public bool Equals(SettingsGroupItem? other) =>
        other is not null && Heading == other.Heading && Rows.SequenceEqual(other.Rows);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Heading, Rows.Count);
}

/// <summary>
/// The workspace settings hub: the sections the member's role may open, in the
/// core's groups and order (District Studio first), and the core's note under
/// them. A row opens its section.
/// </summary>
public sealed partial class SettingsHubViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The groups, District Studio's first.</summary>
    public ObservableCollection<SettingsGroupItem> Groups { get; } = [];

    /// <summary>The core's view of the screen, as last shown.</summary>
    [ObservableProperty]
    public partial SettingsHubView? View { get; set; }

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>The note under the list.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    internal void Attach(PageContext context) => _context = context;

    internal void Show(SettingsHubView view)
    {
        View = view;
        Title = view.Title;
        Note = view.Note;
        Display.Sync(Groups, [.. view.Groups.Select(group => new SettingsGroupItem(
            group.Heading,
            [.. group.Rows.Select(row => new SettingsRowItem(row.Section, row.Title, row.Subtitle))]))]);
    }

    /// <summary>Opens the section <paramref name="row"/> names.</summary>
    internal void Open(SettingsRowItem row) => Send(new SettingsAction.OpenSection(row.Section));

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(SettingsAction action) => _context?.Send(new UiEvent.Settings(action));
}
