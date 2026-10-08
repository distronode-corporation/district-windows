//! The overview (src/overview.rs).

use district_live::{LiveError, LiveUpdate, WorkspaceUpdate};

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    overview_cases()
}

pub(crate) fn overview_loaded(session: Session) -> Session {
    session
        .answer(
            |e| matches!(e, Effect::LoadOverview { .. }),
            |ticket| Event::OverviewLoaded {
                ticket,
                result: Ok(contracts::decode(
                    "district-overview.json",
                    with(
                        contracts::json("district-overview.json"),
                        "workspaceId",
                        "ws-1",
                    ),
                )),
            },
        )
        .answer(
            |e| matches!(e, Effect::LoadUnreadCount { .. }),
            |ticket| Event::UnreadCountLoaded {
                ticket,
                result: Ok(unread_count()),
            },
        )
}

pub(crate) fn no_workspaces(degraded: &[&str]) -> WorkspaceListResponse {
    contracts::decode(
        "no workspaces",
        json!({
            "success": true, "workspaces": [], "total": 0, "limit": 100, "offset": 0,
            "degradedRegions": degraded, "inactiveCount": 0, "defaultWorkspaceId": null
        }),
    )
}

pub(crate) fn overview_cases() -> Vec<Case> {
    vec![
        ("signed-in-overview", signed_in()),
        (
            "overview-loaded-finish-setup",
            overview_loaded(signed_in()).answer(
                |e| matches!(e, Effect::LoadSetupStatus { .. }),
                |ticket| Event::SetupStatusLoaded {
                    ticket,
                    result: Ok(true),
                },
            ),
        ),
        (
            "overview-refreshing",
            overview_loaded(signed_in()).ui(UiEvent::Refresh),
        ),
        (
            "overview-live-stopped",
            overview_loaded(signed_in()).send(Event::Live(WorkspaceUpdate {
                workspace_id: "ws-1".to_owned(),
                update: LiveUpdate::Ended(Some(LiveError::Protocol)),
            })),
        ),
        (
            "overview-failed",
            signed_in().answer(
                |e| matches!(e, Effect::LoadOverview { .. }),
                |ticket| Event::OverviewLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        (
            "overview-no-workspace",
            signed_in_to(Ok(no_workspaces(&[]))),
        ),
        (
            "overview-regions-unreachable",
            signed_in_to(Ok(contracts::read("district-workspace-list-degraded.json"))),
        ),
        (
            "overview-workspaces-failed",
            signed_in_to(Err(server_error())),
        ),
        (
            "overview-switcher-partial",
            signed_in_to(Ok(contracts::read("district-workspace-list-partial.json"))),
        ),
    ]
}
