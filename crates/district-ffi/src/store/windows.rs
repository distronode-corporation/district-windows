//! Credential Manager, through `keyring`'s `windows-native` store.

use district_auth::{StoreError, StoreErrorKind};
use keyring::Entry;

use super::Vault;

/// The start of every credential the app keeps. Credential Manager shows the
/// whole target name (for example
/// `DistrictAI/Distronode.DistrictAI_8wekyb3d8bbwe/session`) in its list.
pub const APP_TARGET: &str = "DistrictAI";

/// A [`Vault`] in Windows Credential Manager: one generic credential per name,
/// kept for this user on this machine (`CRED_PERSIST_LOCAL_MACHINE`, which is
/// what `keyring` uses), so it survives a restart and does not roam.
#[derive(Clone, Debug)]
pub struct WindowsVault {
    prefix: String,
}

impl WindowsVault {
    /// The credentials of the copy installed as `package_family`. Credential
    /// Manager is per user, not per package, so two copies of the app (the
    /// Store's and the GitHub one) never read each other's session.
    pub fn for_package(package_family: &str) -> Self {
        Self::with_prefix(format!("{APP_TARGET}/{package_family}"))
    }

    /// Credentials under another prefix, so a test never touches the app's.
    pub fn with_prefix(prefix: impl Into<String>) -> Self {
        Self {
            prefix: prefix.into(),
        }
    }

    fn entry(&self, name: &str) -> Result<Entry, StoreError> {
        let target = format!("{}/{name}", self.prefix);
        Entry::new_with_target(&target, &self.prefix, name).map_err(store_error)
    }
}

impl Vault for WindowsVault {
    fn read(&self, name: &str) -> Result<Option<Vec<u8>>, StoreError> {
        match self.entry(name)?.get_secret() {
            Ok(secret) => Ok(Some(secret)),
            Err(keyring::Error::NoEntry) => Ok(None),
            Err(error) => Err(store_error(error)),
        }
    }

    fn write(&self, name: &str, secret: &[u8]) -> Result<(), StoreError> {
        self.entry(name)?.set_secret(secret).map_err(store_error)
    }

    fn delete(&self, name: &str) -> Result<(), StoreError> {
        match self.entry(name)?.delete_credential() {
            Ok(()) | Err(keyring::Error::NoEntry) => Ok(()),
            Err(error) => Err(store_error(error)),
        }
    }
}

/// A `keyring` failure as a [`StoreError`]. Its messages name the credential,
/// never the secret.
fn store_error(error: keyring::Error) -> StoreError {
    let kind = match &error {
        keyring::Error::BadEncoding(_) => StoreErrorKind::Corrupt,
        keyring::Error::NoStorageAccess(_) | keyring::Error::PlatformFailure(_) => {
            StoreErrorKind::Unavailable
        }
        _ => StoreErrorKind::Io,
    };
    StoreError::new(kind, error.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The real Credential Manager, under a prefix of its own. Runs on the
    /// Windows CI job.
    #[test]
    fn credential_manager_keeps_reads_and_forgets() {
        let vault = WindowsVault::with_prefix(format!("DistrictAI-test-{}", std::process::id()));
        assert_eq!(vault.read("item").unwrap(), None);
        vault.write("item", b"one").unwrap();
        assert_eq!(vault.read("item").unwrap().as_deref(), Some(&b"one"[..]));
        vault.write("item", b"two").unwrap();
        assert_eq!(vault.read("item").unwrap().as_deref(), Some(&b"two"[..]));
        vault.delete("item").unwrap();
        vault.delete("item").unwrap();
        assert_eq!(vault.read("item").unwrap(), None);
        assert_eq!(
            WindowsVault::for_package("Family_1").prefix,
            "DistrictAI/Family_1"
        );
    }

    #[test]
    fn credential_manager_holds_a_full_outbox() {
        let vault =
            WindowsVault::with_prefix(format!("DistrictAI-test-full-{}", std::process::id()));
        let full = vec![b'x'; super::super::OUTBOX_MAX_BYTES];
        vault.write("outbox", &full).unwrap();
        assert_eq!(vault.read("outbox").unwrap(), Some(full));
        vault.delete("outbox").unwrap();
    }
}
