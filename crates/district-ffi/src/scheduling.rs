//! Booking pages: where the workspace's booking pages stand, turning them on
//! where the service says the member may, and managing them on the web, as
//! District AI for Linux's booking pages page shows them (`pages/scheduling.rs`,
//! `ui/scheduling-page.ui`).
//!
//! Managing them goes through the core's hand-off: the press opens the
//! service's start page in the browser, the browser answers through
//! `districtai://handoff`, and the core asks for a one-time link bound to that
//! browser and opens it there, signed in. The link never reaches this crate's
//! views: they say only that it is being asked for. Whether "Turn on" is
//! offered is the service's answer (`can_manage`), never a role worked out
//! here.

use district_core::{
    Event, Model, Route, SchedulingEvent, SchedulingPresentation, SchedulingScreen,
    SchedulingStatus, SignedIn,
};
use serde::Serialize;

use crate::screen::ScreenView;
use crate::views::{FailureView, LoadStatus, failure};

/// Whether this version has the area's screens.
pub(crate) const BUILT: bool = true;

/// The page's heading, the navigation entry's.
pub const SCHEDULING_TITLE: &str = "Booking pages";

/// Where the booking pages stand, which picks the card's icon.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum SchedulingState {
    /// The workspace is not offered booking pages.
    NotOffered,
    /// Offered, never set up.
    NotSetUp,
    /// Being set up.
    Provisioning,
    /// Live.
    Live,
    /// The last setup failed (the service retries).
    SetupFailed,
    /// Switched off by a person.
    SwitchedOff,
    /// A state this build does not know.
    Unknown,
}

impl From<&SchedulingPresentation> for SchedulingState {
    fn from(shown: &SchedulingPresentation) -> Self {
        match shown {
            SchedulingPresentation::NotOffered => Self::NotOffered,
            SchedulingPresentation::NotSetUp => Self::NotSetUp,
            SchedulingPresentation::Provisioning(_) => Self::Provisioning,
            SchedulingPresentation::Live(_) => Self::Live,
            SchedulingPresentation::SetupFailed(_) => Self::SetupFailed,
            SchedulingPresentation::SwitchedOff(_) => Self::SwitchedOff,
            SchedulingPresentation::Unknown(_) => Self::Unknown,
        }
    }
}

/// The booking pages screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingView {
    /// The heading: "Booking pages".
    pub title: String,
    /// Where the read stands: a status page while loading or failed.
    pub status: LoadStatus,
    /// Where the booking pages stand, once read.
    pub state: Option<SchedulingState>,
    /// The card's sentence, in the core's words. Empty until read.
    pub message: String,
    /// The public booking page's address, when the service sent it.
    pub booking_url: Option<String>,
    /// When they were last live, as an ISO 8601 instant.
    pub last_ready_at: Option<String>,
    /// What went wrong with the last setup, while that is the state.
    pub problem: Option<String>,
    /// Whether the status is being read again, with it showing.
    pub refreshing: bool,
    /// What the last press came to, when it needs saying, beside the card.
    pub notice: Option<FailureView>,
    /// Whether "Turn on booking pages" is offered.
    pub offers_enable: bool,
    /// Whether turning them on is on its way (the button is off meanwhile).
    pub enabling: bool,
    /// Whether "Check again" is offered: only where reading again could
    /// change the answer.
    pub offers_check: bool,
    /// Whether "Manage on the web" is offered.
    pub offers_web: bool,
    /// Whether "Manage on the web" can be pressed: not while the link is
    /// being asked for. While the browser is awaited a press starts over.
    pub web_enabled: bool,
    /// Whether the hand-off to the web is under way (show progress).
    pub opening: bool,
}

impl Default for SchedulingView {
    fn default() -> Self {
        Self {
            title: SCHEDULING_TITLE.to_owned(),
            status: LoadStatus::Loading,
            state: None,
            message: String::new(),
            booking_url: None,
            last_ready_at: None,
            problem: None,
            refreshing: false,
            notice: None,
            offers_enable: false,
            enabling: false,
            offers_check: false,
            offers_web: false,
            web_enabled: false,
            opening: false,
        }
    }
}

/// Something the member did on the booking pages screen.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum SchedulingAction {
    /// Open the booking pages.
    Open,
    /// Manage the booking pages on the web dashboard, signed in.
    ManageOnWeb,
    /// Turn booking pages on.
    Enable,
    /// Read where they stand again ("Check again", "Try again").
    CheckAgain,
    /// Put the notice away.
    DismissNotice,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: SchedulingAction) -> Vec<Event> {
    vec![match action {
        SchedulingAction::Open => Event::Navigate(Route::Scheduling),
        SchedulingAction::ManageOnWeb => Event::Scheduling(SchedulingEvent::ManageOnWeb),
        SchedulingAction::Enable => Event::Scheduling(SchedulingEvent::Enable),
        SchedulingAction::CheckAgain => Event::Refresh,
        SchedulingAction::DismissNotice => Event::Scheduling(SchedulingEvent::DismissNotice),
    }]
}

/// The page of the booking pages, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Scheduling {
        view: scheduling_view(&signed_in.scheduling),
    }
}

fn scheduling_view(screen: &SchedulingScreen) -> SchedulingView {
    let mut view = SchedulingView {
        notice: failure(screen.notice.as_ref()),
        enabling: screen.enabling,
        opening: screen.opening(),
        web_enabled: !screen.minting(),
        ..SchedulingView::default()
    };
    match &screen.status {
        SchedulingStatus::NotLoaded | SchedulingStatus::Loading => {}
        SchedulingStatus::Failed(failure) => {
            view.status = LoadStatus::failed(SchedulingStatus::FAILED_TITLE, failure);
        }
        SchedulingStatus::Ready { status, refreshing } => {
            let shown = screen
                .status
                .presentation()
                .unwrap_or(SchedulingPresentation::NotOffered);
            let tenant = status.tenant.as_ref();
            view.status = LoadStatus::Ready;
            view.state = Some(SchedulingState::from(&shown));
            view.message = shown.message().to_owned();
            view.booking_url = tenant.and_then(|tenant| tenant.booking_url.clone());
            view.last_ready_at = tenant.and_then(|tenant| tenant.last_ready_at.clone());
            view.problem = tenant
                .filter(|_| matches!(shown, SchedulingPresentation::SetupFailed(_)))
                .and_then(|tenant| tenant.last_error.clone());
            view.refreshing = *refreshing;
            view.offers_enable = shown.offers_enable(status);
            view.offers_check = shown.offers_refresh();
            view.offers_web = shown.offers_web();
        }
    }
    view
}
