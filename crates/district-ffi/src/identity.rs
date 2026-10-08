//! How this app names itself to the service.

use district_model::{ClientIdentity, Platform};

/// The product token this app's `User-Agent` starts with.
pub const PRODUCT: &str = "DistrictAI-Windows";

/// This app as the service sees it: the Windows platform, this product, and
/// the app's own version (the package's, which C# reads from the MSIX
/// manifest), not the version of whichever crate builds a request.
pub fn client_identity(app_version: &str) -> ClientIdentity {
    ClientIdentity::new(Platform::Windows, PRODUCT, app_version)
}

#[cfg(test)]
mod tests {
    use district_api::ApiConfig;

    use super::*;

    /// The wire values the server accepts for this app since it learned the
    /// `windows` platform: the platform at sign-in and on the presence row, and
    /// the product in the `User-Agent`.
    #[test]
    fn the_identity_is_windows_at_the_version_given() {
        let identity = client_identity("0.1.0");
        assert_eq!(identity.platform, Platform::Windows);
        assert_eq!(identity.platform.wire(), "windows");
        assert_eq!(
            ApiConfig::new(identity).user_agent(),
            "DistrictAI-Windows/0.1.0"
        );
    }
}
