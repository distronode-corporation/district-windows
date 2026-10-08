//! W2 spike 4: the Credential Manager session survives `taskkill /f` in the
//! middle of a refresh.
//!
//! A child process (this test binary again, running `spike_child`) holds a
//! session in Credential Manager and starts a refresh against a local server
//! that reads the request and never answers. Once the request has arrived (so
//! the refresh-pending marker is written: the core writes it before sending),
//! the parent kills the child with `taskkill /f`, then checks, from a fresh
//! store and a fresh coordinator:
//!
//! 1. the credential still reads back exactly as saved (the kill corrupted
//!    nothing);
//! 2. the marker names the stored token;
//! 3. the next start ends the session as an interrupted refresh, without
//!    sending the possibly spent token anywhere (the service would take a
//!    second presentation for theft and revoke the whole family).
//!
//! It runs three times, killing at 0, 100 and 1,000 ms after the request
//! lands. Windows only: it is about Credential Manager.

#![cfg(windows)]

use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::path::PathBuf;
use std::process::{Child, Command, Stdio};
use std::time::{Duration, Instant};

use district_api::{ApiConfig, ReauthReason, TokenError};
use district_auth::{
    NativeAuthApi, PersistedSession, RefreshToken, SessionStore, TokenRefreshCoordinator,
};
use district_ffi::{CredentialStore, Vault, WindowsVault, client_identity};
use district_host::RefreshMarkerFile;
use url::Url;

const CHILD_ENV: &str = "DISTRICT_SPIKE4_CHILD";

fn session(token: &str) -> PersistedSession {
    PersistedSession {
        refresh_token: RefreshToken::new(token),
        refresh_token_expires_at_ms: 4_000_000_000_000,
        device_id: "device-spike-4".to_owned(),
    }
}

fn store(prefix: &str, dir: &PathBuf) -> CredentialStore<WindowsVault> {
    CredentialStore::new(WindowsVault::with_prefix(prefix), RefreshMarkerFile::new(dir))
}

fn coordinator(
    prefix: &str,
    dir: &PathBuf,
    port: u16,
) -> TokenRefreshCoordinator<CredentialStore<WindowsVault>, NativeAuthApi> {
    let mut config = ApiConfig::new(client_identity("0.1.0"));
    config.base_url = Url::parse(&format!("http://127.0.0.1:{port}/")).unwrap();
    let api = NativeAuthApi::new(&config).unwrap();
    TokenRefreshCoordinator::new(store(prefix, dir), api)
}

/// Accepts one connection within `timeout` and reads its request head.
fn accept_request(listener: &TcpListener, timeout: Duration) -> Option<(TcpStream, String)> {
    listener.set_nonblocking(true).unwrap();
    let start = Instant::now();
    let mut stream = loop {
        match listener.accept() {
            Ok((stream, _)) => break stream,
            Err(_) if start.elapsed() < timeout => std::thread::sleep(Duration::from_millis(5)),
            Err(_) => return None,
        }
    };
    stream.set_nonblocking(false).unwrap();
    stream
        .set_read_timeout(Some(Duration::from_secs(10)))
        .unwrap();
    let mut head = Vec::new();
    let mut byte = [0u8; 1];
    while !head.ends_with(b"\r\n\r\n") {
        if stream.read(&mut byte).unwrap_or(0) == 0 {
            break;
        }
        head.push(byte[0]);
    }
    Some((stream, String::from_utf8_lossy(&head).into_owned()))
}

fn spawn_child(prefix: &str, dir: &PathBuf, port: u16) -> Child {
    Command::new(std::env::current_exe().unwrap())
        .args(["spike_child", "--exact", "--ignored", "--nocapture"])
        .env(CHILD_ENV, format!("{prefix}|{}|{port}", dir.display()))
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .unwrap()
}

fn record(line: &str) {
    println!("{line}");
    if let Ok(path) = std::env::var("SPIKE_RESULTS") {
        let mut file = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(path)
            .unwrap();
        writeln!(file, "{line}").unwrap();
    }
}

#[test]
fn spike_4_the_session_survives_taskkill_mid_refresh() {
    let runtime = tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
        .unwrap();
    let mut failures = Vec::new();
    for (round, delay_ms) in [0u64, 100, 1_000].into_iter().enumerate() {
        let prefix = format!("DistrictAI-spike4-{}-{round}", std::process::id());
        let dir = tempfile::tempdir().unwrap();
        let dir = dir.path().to_path_buf();
        let token = format!("rt-spike-4-{round}");
        let saved = session(&token);
        runtime.block_on(store(&prefix, &dir).save_session(&saved)).unwrap();

        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        let started = Instant::now();
        let mut child = spawn_child(&prefix, &dir, port);
        let Some((_held, head)) = accept_request(&listener, Duration::from_secs(60)) else {
            child.kill().ok();
            failures.push(format!("round {round}: no refresh request arrived"));
            continue;
        };
        let arrived = started.elapsed();
        std::thread::sleep(Duration::from_millis(delay_ms));
        let killed = Command::new("taskkill")
            .args(["/f", "/pid", &child.id().to_string()])
            .stdout(Stdio::null())
            .status()
            .unwrap();
        let exit = child.wait().unwrap();

        // A fresh store, as the next start would open it.
        let reopened = store(&prefix, &dir);
        let loaded = runtime.block_on(reopened.load_session());
        let marker = runtime.block_on(reopened.refresh_pending());

        // A fresh coordinator against a server that counts every connection.
        let watcher = TcpListener::bind("127.0.0.1:0").unwrap();
        let watcher_port = watcher.local_addr().unwrap().port();
        let restored = runtime.block_on(async {
            coordinator(&prefix, &dir, watcher_port).restore().await
        });
        let presented_again = accept_request(&watcher, Duration::from_millis(500)).is_some();
        let after = runtime.block_on(reopened.load_session());

        let ok = killed.success()
            && loaded.as_ref() == Ok(&Some(saved.clone()))
            && marker == Ok(Some(saved.refresh_token.fingerprint()))
            && restored == Err(TokenError::SignInRequired(ReauthReason::InterruptedRefresh))
            && !presented_again
            && after == Ok(None);
        record(&format!(
            "| spike 4 round {round} | {} | request `{}` arrived {} ms after the child started; killed {delay_ms} ms later (taskkill {killed}, child {exit}); credential read back intact: {}; marker names the token: {}; next start: {restored:?}; token presented again: {presented_again}; session cleared after: {} |",
            if ok { "pass" } else { "FAIL" },
            head.lines().next().unwrap_or(""),
            arrived.as_millis(),
            loaded.as_ref() == Ok(&Some(saved.clone())),
            marker == Ok(Some(saved.refresh_token.fingerprint())),
            after == Ok(None),
        ));
        if !ok {
            failures.push(format!("round {round}"));
        }
        let vault = WindowsVault::with_prefix(&prefix);
        vault.delete("session").ok();
        vault.delete("revoke-outbox").ok();
    }
    assert!(failures.is_empty(), "failed: {failures:?}");
}

/// The child: starts a refresh and waits on it until killed. Does nothing
/// unless the parent set its environment.
#[test]
#[ignore = "run only as the child of spike_4_the_session_survives_taskkill_mid_refresh"]
fn spike_child() {
    let Ok(spec) = std::env::var(CHILD_ENV) else {
        return;
    };
    let mut parts = spec.split('|');
    let prefix = parts.next().unwrap().to_owned();
    let dir = PathBuf::from(parts.next().unwrap());
    let port: u16 = parts.next().unwrap().parse().unwrap();
    let runtime = tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
        .unwrap();
    runtime.block_on(async {
        let coordinator = coordinator(&prefix, &dir, port);
        // Never answered: the parent kills this process while it waits.
        let _ = coordinator.access_token().await;
    });
}
