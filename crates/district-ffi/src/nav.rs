//! The navigation pane: its groups and entries, in District AI for Linux's
//! sidebar order and wording, and which one is selected.
//!
//! An entry is offered only when this version has its screen ([`built`]) and
//! the member's role in the open workspace may open it
//! ([`Capabilities::allows`]); the workspace's own areas also wait for a
//! workspace to be open. The five destinations of 1.0 are always offered, as
//! they were.

use district_core::{Capabilities, Route, SignedIn, WorkspaceSection, WorkspacesState};
use serde::Serialize;

use crate::{
    analytics, billing, blocked, desk, hq, marketplace, rooms, scheduling, settings, support,
    workflows,
};

/// What the navigation pane shows: its groups, top to bottom, each with at
/// least one entry.
#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct NavView {
    /// The groups. Empty outside a session.
    pub groups: Vec<NavGroupView>,
}

/// A group of entries.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct NavGroupView {
    /// Which group.
    pub section: NavSection,
    /// The heading above it, or `None` for a group set apart by its place
    /// alone.
    pub heading: Option<String>,
    /// Its entries, top to bottom.
    pub entries: Vec<NavEntryView>,
}

/// One entry of the navigation pane.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct NavEntryView {
    /// Where it goes (send `UiEvent::Navigate`).
    pub destination: NavDestination,
    /// What it says.
    pub label: String,
    /// Its icon: a glyph of the Segoe Fluent Icons font.
    pub glyph: String,
    /// Its name for screen readers and UI tests.
    pub automation_name: String,
    /// Whether it is the one to highlight: the entry of the screen showing, or
    /// of its nearest ancestor that has one.
    pub selected: bool,
}

/// A group of the navigation pane.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum NavSection {
    /// The destinations every member has: the overview, the inbox, calls and
    /// contacts.
    Main,
    /// The rest of the workspace, under its heading.
    Workspace,
    /// The account, which belongs to the person rather than the workspace: at
    /// the foot of the pane.
    Account,
}

/// Where an entry of the navigation pane goes.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum NavDestination {
    /// The open workspace's summary.
    Overview,
    /// Message threads.
    Inbox,
    /// The call log.
    Calls,
    /// Contacts.
    Contacts,
    /// District HQ, the workspace assistant.
    Hq,
    /// Call analytics and metered usage.
    Analytics,
    /// The workspace's phone numbers.
    Marketplace,
    /// The plan and invoices.
    Billing,
    /// Workflows.
    Workflows,
    /// Booking pages.
    Scheduling,
    /// The help desk.
    Desk,
    /// Support requests.
    Support,
    /// Meeting rooms.
    Rooms,
    /// The workspace settings hub.
    Settings,
    /// The account.
    Account,
}

impl NavDestination {
    /// The route the entry opens.
    pub(crate) fn route(self) -> Route {
        match self {
            Self::Overview => Route::Overview,
            Self::Inbox => Route::Inbox,
            Self::Calls => Route::Calls,
            Self::Contacts => Route::Contacts,
            Self::Hq => Route::Hq,
            Self::Analytics => Route::Analytics,
            Self::Marketplace => Route::Marketplace,
            Self::Billing => Route::Billing,
            Self::Workflows => Route::Workflows,
            Self::Scheduling => Route::Scheduling,
            Self::Desk => Route::Desk,
            Self::Support => Route::Support,
            Self::Rooms => Route::Rooms,
            Self::Settings => Route::Workspace(WorkspaceSection::Hub),
            Self::Account => Route::Account,
        }
    }
}

/// One row of the pane, before it is filtered.
struct Row {
    destination: NavDestination,
    section: NavSection,
    label: &'static str,
    glyph: &'static str,
}

/// Every entry, top to bottom: District AI for Linux's sidebar
/// (`routes.rs`), with its titles. The five of 1.0 keep 1.0's glyphs (the
/// `Symbol` values Home, Mail, Phone, People and Contact).
fn rows() -> [Row; 15] {
    use NavDestination as D;
    use NavSection::{Account, Main, Workspace};
    fn row(
        destination: NavDestination,
        section: NavSection,
        label: &'static str,
        glyph: &'static str,
    ) -> Row {
        Row {
            destination,
            section,
            label,
            glyph,
        }
    }
    [
        row(D::Overview, Main, "Overview", "\u{E80F}"),
        row(D::Inbox, Main, "Inbox", "\u{E715}"),
        row(D::Calls, Main, "Calls", "\u{E717}"),
        row(D::Contacts, Main, "Contacts", "\u{E716}"),
        row(D::Hq, Workspace, "District HQ", "\u{E8BD}"),
        row(D::Analytics, Workspace, "Analytics", "\u{E9D2}"),
        row(D::Marketplace, Workspace, "Phone numbers", "\u{E8EA}"),
        row(D::Billing, Workspace, "Billing", "\u{E8A5}"),
        row(D::Workflows, Workspace, "Workflows", "\u{E9F5}"),
        row(D::Scheduling, Workspace, "Booking pages", "\u{E787}"),
        row(D::Desk, Workspace, "Help desk", "\u{E897}"),
        row(D::Support, Workspace, "Support", "\u{E9CE}"),
        row(D::Rooms, Workspace, "Meeting rooms", "\u{E714}"),
        row(D::Settings, Workspace, "Workspace settings", "\u{E713}"),
        row(D::Account, Account, "Account", "\u{E77B}"),
    ]
}

/// The heading above `section`.
fn heading(section: NavSection) -> Option<String> {
    match section {
        NavSection::Workspace => Some("Workspace".to_owned()),
        NavSection::Main | NavSection::Account => None,
    }
}

/// Whether this version has a page for `route`: the areas of 1.0 always, the
/// others once their packet sets their module's `BUILT`. Every route, with
/// no catch-all, so a route the core adds fails the build here.
pub(crate) fn built(route: &Route) -> bool {
    match route {
        Route::Overview
        | Route::Inbox
        | Route::Thread { .. }
        | Route::Calls
        | Route::CallDetail { .. }
        | Route::Contacts
        | Route::ContactDetail { .. }
        | Route::Dialer
        | Route::Account
        | Route::Devices => true,
        Route::BlockedContacts => blocked::BUILT,
        Route::Hq => hq::BUILT,
        Route::Analytics => analytics::BUILT,
        Route::Marketplace => marketplace::BUILT,
        Route::Billing => billing::BUILT,
        Route::Workflows => workflows::BUILT,
        Route::Scheduling => scheduling::BUILT,
        Route::Desk | Route::DeskTicket { .. } | Route::DeskSettings => desk::BUILT,
        Route::Support | Route::SupportRequest { .. } => support::BUILT,
        Route::Rooms => rooms::BUILT,
        Route::Workspace(section) => match section {
            WorkspaceSection::Hub => settings::BUILT,
            WorkspaceSection::Persona => settings::persona::BUILT,
            WorkspaceSection::VoiceStudio => settings::voice_studio::BUILT,
            WorkspaceSection::CallHandling => settings::call_handling::BUILT,
            WorkspaceSection::Routing => settings::routing::BUILT,
            WorkspaceSection::Directory => settings::directory::BUILT,
            WorkspaceSection::Tools => settings::tools::BUILT,
            WorkspaceSection::Knowledge => settings::knowledge::BUILT,
            WorkspaceSection::Messaging => settings::messaging::BUILT,
            WorkspaceSection::Members => settings::members::BUILT,
            WorkspaceSection::Numbers => settings::numbers::BUILT,
        },
    }
}

/// The pane for `signed_in`, whose inbox has `unread` messages nobody has
/// read (in the inbox entry's name, as 1.0 had it).
pub(crate) fn nav_view(signed_in: &SignedIn, unread: u32) -> NavView {
    nav_for(
        &signed_in.route,
        &signed_in.capabilities(),
        matches!(signed_in.workspaces, WorkspacesState::Ready(_)),
        unread,
    )
}

/// The pane while `route` shows, for a member with `capabilities`, with a
/// workspace open when `workspace_ready`.
pub(crate) fn nav_for(
    route: &Route,
    capabilities: &Capabilities,
    workspace_ready: bool,
    unread: u32,
) -> NavView {
    let rows = rows();
    let offered: Vec<&Row> = rows
        .iter()
        .filter(|row| {
            let route = row.destination.route();
            built(&route)
                && (row.section != NavSection::Workspace || workspace_ready)
                && capabilities.allows(&route)
        })
        .collect();
    // The screen's own entry, or its nearest ancestor's.
    let selected = std::iter::successors(Some(route.clone()), Route::parent).find_map(|route| {
        offered
            .iter()
            .find(|row| row.destination.route() == route)
            .map(|row| row.destination)
    });
    let mut groups: Vec<NavGroupView> = Vec::new();
    for row in offered {
        let entry = NavEntryView {
            destination: row.destination,
            label: row.label.to_owned(),
            glyph: row.glyph.to_owned(),
            automation_name: match row.destination {
                NavDestination::Inbox if unread > 0 => format!("Inbox, {unread} unread"),
                _ => row.label.to_owned(),
            },
            selected: selected == Some(row.destination),
        };
        match groups.last_mut() {
            Some(group) if group.section == row.section => group.entries.push(entry),
            _ => groups.push(NavGroupView {
                section: row.section,
                heading: heading(row.section),
                entries: vec![entry],
            }),
        }
    }
    NavView { groups }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn every_route() -> Vec<Route> {
        let id = || "id-1".to_owned();
        let mut routes = vec![
            Route::Overview,
            Route::Inbox,
            Route::Thread { thread_key: id() },
            Route::Calls,
            Route::CallDetail { call_id: id() },
            Route::Contacts,
            Route::ContactDetail { contact_id: id() },
            Route::BlockedContacts,
            Route::Hq,
            Route::Analytics,
            Route::Marketplace,
            Route::Billing,
            Route::Workflows,
            Route::Scheduling,
            Route::Desk,
            Route::DeskTicket { ticket_id: id() },
            Route::DeskSettings,
            Route::Support,
            Route::SupportRequest { key: id() },
            Route::Rooms,
            Route::Dialer,
            Route::Account,
            Route::Devices,
        ];
        routes.extend(WorkspaceSection::ALL.map(Route::Workspace));
        routes
    }

    fn destinations(nav: &NavView) -> Vec<NavDestination> {
        nav.groups
            .iter()
            .flat_map(|group| group.entries.iter().map(|entry| entry.destination))
            .collect()
    }

    /// Each entry's label, glyph and name.
    fn faces(group: &NavGroupView) -> Vec<(&str, &str, &str)> {
        group
            .entries
            .iter()
            .map(|entry| {
                (
                    entry.label.as_str(),
                    entry.glyph.as_str(),
                    entry.automation_name.as_str(),
                )
            })
            .collect()
    }

    /// Every role, none included, with and without a workspace open.
    fn every_member() -> Vec<(Capabilities, bool)> {
        [None, Some("agency"), Some("client"), Some("viewer")]
            .into_iter()
            .flat_map(|role| [false, true].map(|ready| (Capabilities::for_role(role), ready)))
            .collect()
    }

    /// The pane opens with 1.0's four and ends with 1.0's account, with 1.0's
    /// glyphs and names, whatever the role and whether a workspace is open;
    /// between them is the Workspace group, offering exactly the areas built,
    /// open to the role, once a workspace is open. With no area of 2.0 built,
    /// that is 1.0's pane exactly.
    #[test]
    fn the_pane_is_1_0s_plus_what_is_built_and_allowed() {
        for (capabilities, ready) in every_member() {
            let nav = nav_for(&Route::Overview, &capabilities, ready, 0);
            let first = nav.groups.first().unwrap();
            let last = nav.groups.last().unwrap();
            assert_eq!(
                (first.section, first.heading.as_deref()),
                (NavSection::Main, None)
            );
            assert_eq!(
                faces(first),
                [
                    ("Overview", "\u{E80F}", "Overview"),
                    ("Inbox", "\u{E715}", "Inbox"),
                    ("Calls", "\u{E717}", "Calls"),
                    ("Contacts", "\u{E716}", "Contacts"),
                ]
            );
            assert_eq!(
                (last.section, last.heading.as_deref()),
                (NavSection::Account, None)
            );
            assert_eq!(faces(last), [("Account", "\u{E77B}", "Account")]);
            let workspace: Vec<NavDestination> = nav.groups[1..nav.groups.len() - 1]
                .iter()
                .inspect(|group| assert_eq!(group.heading.as_deref(), Some("Workspace")))
                .flat_map(|group| group.entries.iter().map(|entry| entry.destination))
                .collect();
            let expected: Vec<NavDestination> = rows()
                .iter()
                .filter(|row| row.section == NavSection::Workspace)
                .map(|row| row.destination)
                .filter(|destination| {
                    let route = destination.route();
                    ready && built(&route) && capabilities.allows(&route)
                })
                .collect();
            assert_eq!(workspace, expected, "{capabilities:?}, ready: {ready}");
        }
    }

    /// One entry is highlighted on every screen: the screen's own, or the
    /// nearest ancestor's that the pane offers.
    #[test]
    fn one_entry_is_selected_the_screen_s_or_an_ancestor_s() {
        let agency = Capabilities::for_role(Some("agency"));
        for route in every_route() {
            let nav = nav_for(&route, &agency, true, 0);
            let selected: Vec<Route> = nav
                .groups
                .iter()
                .flat_map(|group| group.entries.iter())
                .filter(|entry| entry.selected)
                .map(|entry| entry.destination.route())
                .collect();
            assert_eq!(selected.len(), 1, "{route:?}");
            assert!(
                std::iter::successors(Some(route.clone()), Route::parent)
                    .any(|ancestor| ancestor == selected[0]),
                "{route:?}"
            );
        }
    }

    #[test]
    fn the_inbox_entry_names_its_unread_count() {
        let nav = nav_for(&Route::Inbox, &Capabilities::default(), true, 3);
        let inbox = &nav.groups[0].entries[1];
        assert_eq!(inbox.destination, NavDestination::Inbox);
        assert_eq!(inbox.label, "Inbox");
        assert_eq!(inbox.automation_name, "Inbox, 3 unread");
        assert!(inbox.selected);
    }

    /// 1.0's routes have their pages, and the pane offers nothing whose page
    /// is not built.
    #[test]
    fn the_pane_offers_only_what_is_built() {
        let offered = destinations(&nav_for(
            &Route::Overview,
            &Capabilities::for_role(Some("agency")),
            true,
            0,
        ));
        for route in every_route() {
            let of_1_0 = matches!(
                route,
                Route::Overview
                    | Route::Inbox
                    | Route::Thread { .. }
                    | Route::Calls
                    | Route::CallDetail { .. }
                    | Route::Contacts
                    | Route::ContactDetail { .. }
                    | Route::Dialer
                    | Route::Account
                    | Route::Devices
            );
            let in_pane = offered
                .iter()
                .any(|destination| destination.route() == route);
            assert!(built(&route) || !(of_1_0 || in_pane), "{route:?}");
        }
    }

    /// Each destination has one row and opens a route of the core.
    #[test]
    fn every_destination_has_one_row_and_its_route() {
        let rows = rows();
        for (index, row) in rows.iter().enumerate() {
            assert!(
                rows[..index]
                    .iter()
                    .all(|earlier| earlier.destination != row.destination)
            );
            assert!(every_route().contains(&row.destination.route()));
        }
        assert_eq!(heading(NavSection::Workspace).as_deref(), Some("Workspace"));
    }
}
