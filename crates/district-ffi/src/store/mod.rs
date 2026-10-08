//! Where the session survives between runs: Credential Manager on Windows.
//!
//! [`CredentialStore`] implements the core's `SessionStore` over a [`Vault`], a
//! named-secret store with three operations. On Windows the vault is
//! [`WindowsVault`] (Credential Manager, through `keyring`); everywhere else,
//! and in this crate's tests, it is [`MemoryVault`]. The refresh-pending marker
//! is a file in the app's data directory, as on Linux
//! (`district_host::RefreshMarkerFile`): it holds a digest, never a token.
//!
//! Two credentials, one per item:
//!
//! - **session**: a small JSON object with the refresh token, its expiry and
//!   the device id (the same fields, and names, as the Linux app's keyring
//!   item; storage, not a wire format);
//! - **revoke-outbox**: a JSON array of the refresh tokens a sign-out could not
//!   get revoked yet. Credential Manager holds at most 2,560 bytes in one
//!   credential, so the outbox refuses an entry that would take it past
//!   [`OUTBOX_MAX_BYTES`]; the sign-out then reports the token as stranded,
//!   which is what the core says when an outbox cannot be written.

#[cfg(windows)]
mod windows;

use std::collections::HashMap;
use std::io;
use std::sync::{Arc, Mutex, PoisonError};

use district_auth::{
    PersistedSession, RefreshToken, SessionStore, StoreError, StoreErrorKind, TokenFingerprint,
};
use district_host::RefreshMarkerFile;
use serde::{Deserialize, Serialize};

#[cfg(windows)]
pub use self::windows::WindowsVault;

/// The most the revoke outbox's credential may hold:
/// `CRED_MAX_CREDENTIAL_BLOB_SIZE`, five 512-byte pages.
pub const OUTBOX_MAX_BYTES: usize = 2560;

/// The session's credential.
pub(crate) const SESSION: &str = "session";
/// The revoke outbox's credential.
pub(crate) const REVOKE_OUTBOX: &str = "revoke-outbox";

/// Named secrets: what [`CredentialStore`] needs of a secret store. Every call
/// blocks (Credential Manager's API is synchronous), so the store makes them on
/// Tokio's blocking pool.
pub trait Vault: Send + Sync + 'static {
    /// The secret called `name`, or `None` when there is none.
    fn read(&self, name: &str) -> Result<Option<Vec<u8>>, StoreError>;
    /// Writes `secret` as `name`, replacing any secret of that name.
    fn write(&self, name: &str, secret: &[u8]) -> Result<(), StoreError>;
    /// Removes `name`. Removing a secret that is not there is not an error.
    fn delete(&self, name: &str) -> Result<(), StoreError>;
}

/// A [`Vault`] in memory: the session ends with the process. What the Linux
/// build of this crate (the one DistrictAI.Core.Tests load) and the tests use.
#[derive(Clone, Debug, Default)]
pub struct MemoryVault {
    secrets: Arc<Mutex<HashMap<String, Vec<u8>>>>,
}

impl MemoryVault {
    /// An empty vault.
    pub fn new() -> Self {
        Self::default()
    }
}

impl Vault for MemoryVault {
    fn read(&self, name: &str) -> Result<Option<Vec<u8>>, StoreError> {
        let secrets = self.secrets.lock().unwrap_or_else(PoisonError::into_inner);
        Ok(secrets.get(name).cloned())
    }

    fn write(&self, name: &str, secret: &[u8]) -> Result<(), StoreError> {
        let mut secrets = self.secrets.lock().unwrap_or_else(PoisonError::into_inner);
        secrets.insert(name.to_owned(), secret.to_vec());
        Ok(())
    }

    fn delete(&self, name: &str) -> Result<(), StoreError> {
        let mut secrets = self.secrets.lock().unwrap_or_else(PoisonError::into_inner);
        secrets.remove(name);
        Ok(())
    }
}

/// The core's `SessionStore` over a [`Vault`] and a marker file.
#[derive(Debug)]
pub struct CredentialStore<V> {
    vault: Arc<V>,
    marker: RefreshMarkerFile,
}

/// The session credential's secret.
#[derive(Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct StoredSession {
    refresh_token: String,
    refresh_token_expires_at: i64,
    device_id: String,
}

impl<V: Vault> CredentialStore<V> {
    /// A store over `vault`, with the refresh-pending marker in `marker`.
    pub fn new(vault: V, marker: RefreshMarkerFile) -> Self {
        Self {
            vault: Arc::new(vault),
            marker,
        }
    }

    async fn on_vault<T: Send + 'static>(
        &self,
        work: impl FnOnce(&V) -> Result<T, StoreError> + Send + 'static,
    ) -> Result<T, StoreError> {
        let vault = Arc::clone(&self.vault);
        tokio::task::spawn_blocking(move || work(&vault))
            .await
            .expect("vault tasks run to completion")
    }

    async fn on_marker<T: Send + 'static>(
        &self,
        work: impl FnOnce(RefreshMarkerFile) -> io::Result<T> + Send + 'static,
    ) -> Result<T, StoreError> {
        let marker = self.marker.clone();
        // The marker's writes are flushed to disk before they return, so they
        // run on the blocking pool rather than on an async worker.
        tokio::task::spawn_blocking(move || work(marker))
            .await
            .expect("marker file tasks run to completion")
            .map_err(marker_error)
    }

    async fn outbox(&self) -> Result<Vec<String>, StoreError> {
        self.on_vault(read_outbox).await
    }
}

fn read_outbox<V: Vault>(vault: &V) -> Result<Vec<String>, StoreError> {
    match vault.read(REVOKE_OUTBOX)? {
        None => Ok(Vec::new()),
        // The parser's own message could quote a token, so it is not kept.
        Some(bytes) => serde_json::from_slice(&bytes).map_err(|_| {
            StoreError::new(
                StoreErrorKind::Corrupt,
                "the revoke outbox is in an unknown format",
            )
        }),
    }
}

fn write_outbox<V: Vault>(vault: &V, tokens: &[String]) -> Result<(), StoreError> {
    if tokens.is_empty() {
        return vault.delete(REVOKE_OUTBOX);
    }
    let bytes = serde_json::to_vec(tokens).expect("a list of strings always serialises");
    if bytes.len() > OUTBOX_MAX_BYTES {
        return Err(StoreError::new(
            StoreErrorKind::Io,
            format!("the revoke outbox is full ({OUTBOX_MAX_BYTES} bytes)"),
        ));
    }
    vault.write(REVOKE_OUTBOX, &bytes)
}

impl<V: Vault> SessionStore for CredentialStore<V> {
    async fn load_session(&self) -> Result<Option<PersistedSession>, StoreError> {
        let Some(secret) = self.on_vault(|vault| vault.read(SESSION)).await? else {
            return Ok(None);
        };
        // The parser's own message could quote the secret, so it is not kept.
        let stored: StoredSession = serde_json::from_slice(&secret).map_err(|_| {
            StoreError::new(
                StoreErrorKind::Corrupt,
                "the saved sign-in is in an unknown format",
            )
        })?;
        // A record with no token can never refresh, and the service's refusal
        // of an empty one reads as "try again later": the app would look
        // signed in and load nothing, for good.
        if stored.refresh_token.is_empty() {
            return Err(StoreError::new(
                StoreErrorKind::Corrupt,
                "the saved sign-in has no refresh token",
            ));
        }
        Ok(Some(PersistedSession {
            refresh_token: RefreshToken::new(stored.refresh_token),
            refresh_token_expires_at_ms: stored.refresh_token_expires_at,
            device_id: stored.device_id,
        }))
    }

    async fn save_session(&self, session: &PersistedSession) -> Result<(), StoreError> {
        let stored = StoredSession {
            refresh_token: session.refresh_token.as_str().to_owned(),
            refresh_token_expires_at: session.refresh_token_expires_at_ms,
            device_id: session.device_id.clone(),
        };
        let bytes = serde_json::to_vec(&stored).expect("the session always serialises");
        self.on_vault(move |vault| vault.write(SESSION, &bytes))
            .await
    }

    async fn clear_session(&self) -> Result<(), StoreError> {
        // The session first: a marker left behind by a failure after this names
        // nothing, while the other order could leave a possibly spent token
        // without the marker that says not to present it.
        self.on_vault(|vault| vault.delete(SESSION)).await?;
        self.clear_refresh_pending().await
    }

    async fn refresh_pending(&self) -> Result<Option<TokenFingerprint>, StoreError> {
        self.on_marker(|marker| marker.read()).await
    }

    async fn set_refresh_pending(&self, token: &TokenFingerprint) -> Result<(), StoreError> {
        let token = *token;
        self.on_marker(move |marker| marker.write(&token)).await
    }

    async fn clear_refresh_pending(&self) -> Result<(), StoreError> {
        self.on_marker(|marker| marker.clear()).await
    }

    async fn push_revoke(&self, token: &RefreshToken) -> Result<(), StoreError> {
        let token = token.as_str().to_owned();
        self.on_vault(move |vault| {
            let mut tokens = read_outbox(vault)?;
            if !tokens.contains(&token) {
                tokens.push(token);
            }
            write_outbox(vault, &tokens)
        })
        .await
    }

    async fn revoke_outbox(&self) -> Result<Vec<Result<RefreshToken, StoreError>>, StoreError> {
        Ok(self
            .outbox()
            .await?
            .into_iter()
            .map(|token| Ok(RefreshToken::new(token)))
            .collect())
    }

    async fn remove_revoke(&self, token: &RefreshToken) -> Result<(), StoreError> {
        let token = token.as_str().to_owned();
        self.on_vault(move |vault| {
            let mut tokens = read_outbox(vault)?;
            tokens.retain(|kept| *kept != token);
            write_outbox(vault, &tokens)
        })
        .await
    }
}

/// A marker file failure as a [`StoreError`]. A marker that is not a
/// fingerprint is corrupt: what it named cannot be known.
fn marker_error(error: io::Error) -> StoreError {
    let kind = match error.kind() {
        io::ErrorKind::InvalidData => StoreErrorKind::Corrupt,
        _ => StoreErrorKind::Io,
    };
    StoreError::new(kind, format!("the refresh-pending marker: {error}"))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn store() -> (CredentialStore<MemoryVault>, MemoryVault, tempfile::TempDir) {
        let dir = tempfile::tempdir().unwrap();
        let vault = MemoryVault::new();
        let store = CredentialStore::new(vault.clone(), RefreshMarkerFile::new(dir.path()));
        (store, vault, dir)
    }

    fn session(token: &str) -> PersistedSession {
        PersistedSession {
            refresh_token: RefreshToken::new(token),
            refresh_token_expires_at_ms: 4_000_000_000_000,
            device_id: "device-windows-1".to_owned(),
        }
    }

    #[tokio::test]
    async fn the_session_round_trips_in_the_linux_apps_format() {
        let (store, vault, _dir) = store();
        assert_eq!(store.load_session().await.unwrap(), None);
        store.save_session(&session("rt-1")).await.unwrap();
        assert_eq!(store.load_session().await.unwrap(), Some(session("rt-1")));
        let raw = vault.read(SESSION).unwrap().unwrap();
        assert_eq!(
            String::from_utf8(raw).unwrap(),
            r#"{"refreshToken":"rt-1","refreshTokenExpiresAt":4000000000000,"deviceId":"device-windows-1"}"#
        );
    }

    #[tokio::test]
    async fn a_record_that_cannot_be_used_is_corrupt() {
        let (store, vault, _dir) = store();
        vault.write(SESSION, b"not json").unwrap();
        let error = store.load_session().await.unwrap_err();
        assert_eq!(error.kind, StoreErrorKind::Corrupt);
        assert!(!error.detail.contains("not json"));

        vault
            .write(
                SESSION,
                br#"{"refreshToken":"","refreshTokenExpiresAt":1,"deviceId":"d"}"#,
            )
            .unwrap();
        let error = store.load_session().await.unwrap_err();
        assert_eq!(error.kind, StoreErrorKind::Corrupt);
    }

    #[tokio::test]
    async fn clearing_takes_the_session_and_the_marker_and_leaves_the_outbox() {
        let (store, _vault, _dir) = store();
        let token = RefreshToken::new("rt-2");
        store.save_session(&session("rt-2")).await.unwrap();
        store
            .set_refresh_pending(&token.fingerprint())
            .await
            .unwrap();
        assert_eq!(
            store.refresh_pending().await.unwrap(),
            Some(token.fingerprint())
        );
        store.push_revoke(&token).await.unwrap();
        store.clear_session().await.unwrap();
        assert_eq!(store.load_session().await.unwrap(), None);
        assert_eq!(store.refresh_pending().await.unwrap(), None);
        assert_eq!(store.revoke_outbox().await.unwrap(), [Ok(token)]);
    }

    #[tokio::test]
    async fn the_marker_alone_comes_and_goes() {
        let (store, _vault, _dir) = store();
        let token = RefreshToken::new("rt-3");
        store
            .set_refresh_pending(&token.fingerprint())
            .await
            .unwrap();
        store.clear_refresh_pending().await.unwrap();
        assert_eq!(store.refresh_pending().await.unwrap(), None);
    }

    #[tokio::test]
    async fn the_outbox_keeps_one_entry_per_token_and_empties_to_nothing() {
        let (store, vault, _dir) = store();
        let first = RefreshToken::new("rt-a");
        let second = RefreshToken::new("rt-b");
        store.push_revoke(&first).await.unwrap();
        store.push_revoke(&first).await.unwrap();
        store.push_revoke(&second).await.unwrap();
        assert_eq!(
            store.revoke_outbox().await.unwrap(),
            [Ok(first.clone()), Ok(second.clone())]
        );
        store.remove_revoke(&first).await.unwrap();
        store.remove_revoke(&first).await.unwrap();
        assert_eq!(store.revoke_outbox().await.unwrap(), [Ok(second.clone())]);
        store.remove_revoke(&second).await.unwrap();
        assert!(store.revoke_outbox().await.unwrap().is_empty());
        assert_eq!(vault.read(REVOKE_OUTBOX).unwrap(), None);
    }

    #[tokio::test]
    async fn the_outbox_refuses_what_credential_manager_could_not_hold() {
        let (store, _vault, _dir) = store();
        let mut pushed = 0;
        let error = loop {
            let token = RefreshToken::new(format!("{pushed:0>120}"));
            match store.push_revoke(&token).await {
                Ok(()) => pushed += 1,
                Err(error) => break error,
            }
        };
        assert_eq!(error.kind, StoreErrorKind::Io);
        assert!(error.detail.contains("full"));
        // Everything accepted before the refusal is still there.
        assert_eq!(store.revoke_outbox().await.unwrap().len(), pushed);
        assert!(pushed >= 15, "only {pushed} tokens fitted");
    }

    #[tokio::test]
    async fn an_unreadable_outbox_is_corrupt() {
        let (store, vault, _dir) = store();
        vault.write(REVOKE_OUTBOX, b"{").unwrap();
        let error = store.revoke_outbox().await.unwrap_err();
        assert_eq!(error.kind, StoreErrorKind::Corrupt);
    }

    #[tokio::test]
    async fn a_marker_that_is_not_a_fingerprint_is_corrupt() {
        let (store, _vault, dir) = store();
        std::fs::write(dir.path().join(district_host::MARKER_FILE), "nonsense").unwrap();
        let error = store.refresh_pending().await.unwrap_err();
        assert_eq!(error.kind, StoreErrorKind::Corrupt);
        assert_eq!(
            marker_error(io::Error::other("disk")).kind,
            StoreErrorKind::Io
        );
    }
}
