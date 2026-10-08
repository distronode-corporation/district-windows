//! The `districtai:` links Windows hands the app.

use url::Url;

/// The scheme the MSIX manifest registers.
pub const SCHEME: &str = "districtai";

/// What a `districtai:` link is for.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, uniffi::Enum)]
pub enum LinkKind {
    /// `districtai://auth`: the browser handing a sign-in back.
    Auth,
    /// `districtai://handoff`: the browser's answer to a hand-off to the web
    /// (the scheduling pages), checked by the core against its one-time nonce.
    Handoff,
    /// Anything else: another scheme, another host, or not a link at all.
    Unknown,
}

/// What `uri` is for. Only the scheme and the host decide; the query, which
/// carries the sign-in's code and state, is left to the core.
#[uniffi::export]
pub fn link_kind(uri: &str) -> LinkKind {
    let Ok(url) = Url::parse(uri) else {
        return LinkKind::Unknown;
    };
    if url.scheme() != SCHEME {
        return LinkKind::Unknown;
    }
    match url.host_str() {
        Some("auth") => LinkKind::Auth,
        Some("handoff") => LinkKind::Handoff,
        _ => LinkKind::Unknown,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_scheme_and_host_decide() {
        assert_eq!(
            link_kind("districtai://auth?code=c&state=s"),
            LinkKind::Auth
        );
        assert_eq!(link_kind("DistrictAI://auth"), LinkKind::Auth);
        assert_eq!(link_kind("districtai://handoff?n=1"), LinkKind::Handoff);
        assert_eq!(link_kind("districtai://other"), LinkKind::Unknown);
        assert_eq!(link_kind("districtai:auth"), LinkKind::Unknown);
        assert_eq!(link_kind("https://auth/"), LinkKind::Unknown);
        assert_eq!(link_kind("not a link"), LinkKind::Unknown);
    }
}
