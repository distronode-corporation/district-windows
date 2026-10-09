//! The two copies of the app: the Microsoft Store's and the GitHub one.
//!
//! They are separate packages with separate identities, and both register the
//! `districtai:` scheme the browser hands a sign-in back through. With both
//! installed for the same user, Windows may give that answer to the other
//! copy, which is not waiting for it, and the sign-in never finishes. So while
//! both are installed, browser sign-in is held with a plain explanation, and
//! the window says which copy this is. A session already signed in is not
//! touched: only starting a sign-in in the browser is held.
//!
//! What is installed is found on the C# side (the packages that handle the
//! scheme); deciding what that means is here, so it is tested on every
//! platform.

use serde::Serialize;

/// The Store copy's package name (`Package.appxmanifest`).
pub const STORE_PACKAGE_NAME: &str = "DistronodeCorporation.42101E4C5A5B6";
/// The GitHub copy's package name (`Package.GitHub.appxmanifest`).
pub const GITHUB_PACKAGE_NAME: &str = "Distronode.DistrictAI.GitHub";
/// The heading of the explanation, as the README and CONTRIBUTING put it.
pub const BOTH_TITLE: &str = "Install one, not both";

/// Which copy of the app a package is.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum CopyFlavour {
    /// The Microsoft Store's copy.
    Store,
    /// The GitHub copy.
    GitHub,
    /// Neither: a development build, or a package this build does not know.
    Other,
}

impl CopyFlavour {
    /// The copy whose package family name is `family` (`<name>_<publisher id>`).
    pub fn of(family: &str) -> Self {
        let name = family.split('_').next().unwrap_or_default();
        if name.eq_ignore_ascii_case(STORE_PACKAGE_NAME) {
            Self::Store
        } else if name.eq_ignore_ascii_case(GITHUB_PACKAGE_NAME) {
            Self::GitHub
        } else {
            Self::Other
        }
    }

    /// The copy, as a sentence names it.
    fn name(self) -> &'static str {
        match self {
            Self::Store => "the Microsoft Store copy",
            Self::GitHub => "the GitHub copy",
            Self::Other => "a development copy",
        }
    }
}

/// What the window says about the copies installed.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CopiesView {
    /// Which copy this is.
    pub this_copy: CopyFlavour,
    /// Whether another copy of the app is installed for this user, so the two
    /// share the sign-in scheme.
    pub both_installed: bool,
    /// Whether browser sign-in is held.
    pub sign_in_held: bool,
    /// The explanation's heading, while sign-in is held.
    pub title: Option<String>,
    /// The explanation, while sign-in is held.
    pub message: Option<String>,
    /// The line saying which copy this is, while another is installed.
    pub this_copy_line: Option<String>,
    /// The window's title: "District AI", naming the copy while another is
    /// installed.
    pub window_title: String,
}

/// What the window says, for this copy (`this_family`, its package family
/// name) and the packages that handle the `districtai:` scheme for this user
/// (`scheme_handlers`, package family names, this copy's own included or
/// not). A handler that is another copy of this app, of either flavour,
/// holds browser sign-in; anything else that handles the scheme is not a copy
/// of this app and is left alone.
#[uniffi::export]
pub fn copies_view(this_family: String, scheme_handlers: Vec<String>) -> CopiesView {
    let this_copy = CopyFlavour::of(&this_family);
    let other = scheme_handlers
        .iter()
        .filter(|family| !family.eq_ignore_ascii_case(&this_family))
        .map(|family| CopyFlavour::of(family))
        .find(|flavour| *flavour != CopyFlavour::Other);
    let Some(other) = other else {
        return CopiesView {
            this_copy,
            both_installed: false,
            sign_in_held: false,
            title: None,
            message: None,
            this_copy_line: None,
            window_title: "District AI".to_owned(),
        };
    };
    let this_name = this_copy.name();
    let label = match this_copy {
        CopyFlavour::Store => "Microsoft Store copy",
        CopyFlavour::GitHub => "GitHub copy",
        CopyFlavour::Other => "development copy",
    };
    CopiesView {
        this_copy,
        both_installed: true,
        sign_in_held: true,
        title: Some(BOTH_TITLE.to_owned()),
        message: Some(format!(
            "District AI is installed twice on this computer, {this_name} and {}. Both \
             answer the browser when you sign in, so signing in is held until one is \
             uninstalled. Uninstall one in Settings, Apps, then open the other.",
            other.name()
        )),
        this_copy_line: Some(format!("This is {this_name} of District AI.")),
        window_title: format!("District AI ({label})"),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const STORE: &str = "DistronodeCorporation.42101E4C5A5B6_m7gk2x0xkz9fa";
    const GITHUB: &str = "Distronode.DistrictAI.GitHub_4a1yh7x8ec0qe";

    #[test]
    fn a_family_name_names_its_copy() {
        assert_eq!(CopyFlavour::of(STORE), CopyFlavour::Store);
        assert_eq!(CopyFlavour::of(GITHUB), CopyFlavour::GitHub);
        assert_eq!(
            CopyFlavour::of("distronode.districtai.github_x"),
            CopyFlavour::GitHub
        );
        assert_eq!(CopyFlavour::of("DistrictAI.Tests_0"), CopyFlavour::Other);
        assert_eq!(CopyFlavour::of(""), CopyFlavour::Other);
    }

    #[test]
    fn alone_nothing_is_held() {
        for handlers in [
            vec![],
            vec![STORE.to_owned()],
            vec!["Contoso.Mail_1".to_owned()],
        ] {
            let view = copies_view(STORE.to_owned(), handlers);
            assert_eq!(view.this_copy, CopyFlavour::Store);
            assert!(!view.both_installed && !view.sign_in_held);
            assert_eq!(view.title, None);
            assert_eq!(view.message, None);
            assert_eq!(view.this_copy_line, None);
            assert_eq!(view.window_title, "District AI");
        }
    }

    #[test]
    fn with_both_installed_sign_in_is_held_and_the_copy_named() {
        let store = copies_view(STORE.to_owned(), vec![STORE.to_owned(), GITHUB.to_owned()]);
        assert!(store.both_installed && store.sign_in_held);
        assert_eq!(store.title.as_deref(), Some("Install one, not both"));
        let message = store.message.unwrap();
        assert!(
            message.contains("the Microsoft Store copy and the GitHub copy"),
            "{message}"
        );
        assert_eq!(
            store.this_copy_line.as_deref(),
            Some("This is the Microsoft Store copy of District AI.")
        );
        assert_eq!(store.window_title, "District AI (Microsoft Store copy)");

        let github = copies_view(GITHUB.to_owned(), vec![STORE.to_owned()]);
        assert_eq!(github.this_copy, CopyFlavour::GitHub);
        assert!(github.sign_in_held);
        assert_eq!(github.window_title, "District AI (GitHub copy)");
        assert!(
            github
                .message
                .unwrap()
                .contains("the GitHub copy and the Microsoft Store copy")
        );

        // A development build beside an installed copy is held too.
        let dev = copies_view("DistrictAI.Dev_1".to_owned(), vec![GITHUB.to_owned()]);
        assert_eq!(dev.this_copy, CopyFlavour::Other);
        assert!(dev.sign_in_held);
        assert_eq!(dev.window_title, "District AI (development copy)");
    }

    /// The family name compares without regard to case, as Windows does.
    #[test]
    fn this_copy_listed_in_another_case_is_still_this_copy() {
        let view = copies_view(STORE.to_owned(), vec![STORE.to_uppercase()]);
        assert!(!view.sign_in_held);
    }
}
