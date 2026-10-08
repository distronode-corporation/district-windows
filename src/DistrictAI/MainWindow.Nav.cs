using DistrictAI.Core.Ffi;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI;

/// <summary>
/// The navigation pane, built from the core's <see cref="NavView"/>: its
/// entries, the Workspace group's heading, the account at the foot, the
/// inbox's unread badge, and the entry to highlight. Which entries there are
/// is the core's decision (an area is offered once it is built and the
/// member's role allows it); the pane only draws them.
/// </summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<NavDestination, NavigationViewItem> _navItems = [];
    private readonly List<(NavSection Section, NavDestination Destination)> _navShape = [];
    private InfoBadge? _unreadBadge;

    /// <summary>
    /// Draws <paramref name="nav"/>. The items are made again only when the
    /// entries change, so a snapshot that changes nothing else leaves the
    /// pane's focus and animation alone.
    /// </summary>
    private void RenderNav(NavView nav, uint unread)
    {
        var shape = nav.Groups
            .SelectMany(group => group.Entries.Select(entry => (group.Section, entry.Destination)))
            .ToList();
        if (!shape.SequenceEqual(_navShape))
        {
            BuildNav(nav);
            _navShape.Clear();
            _navShape.AddRange(shape);
        }

        NavigationViewItem? selected = null;
        foreach (var entry in nav.Groups.SelectMany(group => group.Entries))
        {
            var item = _navItems[entry.Destination];
            item.Content = entry.Label;
            AutomationProperties.SetName(item, entry.AutomationName);
            if (entry.Selected)
            {
                selected = item;
            }
        }
        Nav.SelectedItem = selected;

        if (_unreadBadge is not null)
        {
            _unreadBadge.Value = (int)Math.Min(unread, int.MaxValue);
            _unreadBadge.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Makes the pane's items: the account group at its foot, the others in
    /// its menu, a group with a heading set apart by a line and its heading.
    /// </summary>
    private void BuildNav(NavView nav)
    {
        Nav.MenuItems.Clear();
        Nav.FooterMenuItems.Clear();
        _navItems.Clear();
        _unreadBadge = null;
        foreach (var group in nav.Groups)
        {
            var items = group.Section == NavSection.Account ? Nav.FooterMenuItems : Nav.MenuItems;
            if (group.Heading is { } heading)
            {
                if (items.Count > 0)
                {
                    items.Add(new NavigationViewItemSeparator());
                }
                items.Add(new NavigationViewItemHeader { Content = heading });
            }
            foreach (var entry in group.Entries)
            {
                var item = new NavigationViewItem
                {
                    Content = entry.Label,
                    Icon = new FontIcon { Glyph = entry.Glyph },
                    Tag = entry.Destination,
                };
                if (entry.Destination == NavDestination.Inbox)
                {
                    _unreadBadge = new InfoBadge { Visibility = Visibility.Collapsed };
                    item.InfoBadge = _unreadBadge;
                }
                items.Add(item);
                _navItems[entry.Destination] = item;
            }
        }
    }

    private void OnNavInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is NavDestination destination)
        {
            _core.Send(new UiEvent.Navigate(destination));
        }
    }
}
