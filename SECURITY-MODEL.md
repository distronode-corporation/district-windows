# Security model

District AI for Windows holds a District AI session and shows a workspace's calls, messages,
contacts and settings, and it rings and joins calls. This document is the assurance case: what
the app protects, who it defends against, what it trusts, and how the code holds each line, with
the tests that pin each control. The rules in short are in [SECURITY.md](SECURITY.md#the-security-model-in-short),
and how the app is built is in the README's [How it is built](README.md#how-it-is-built) and in
[CONTRIBUTING.md](CONTRIBUTING.md#layout).

Everything the app decides (sign-in, tokens, the API client, live updates, calls) is in
[District AI core for Rust](https://github.com/distronode-corporation/district-core-rust), which
this app pins by tag (`v13.0.0` in `Cargo.toml`) and shares with District AI for Linux. Where a
test lives there, the table says "core:" before its path, at that tag. The core's own security
model is its [SECURITY-MODEL.md](https://github.com/distronode-corporation/district-core-rust/blob/main/SECURITY-MODEL.md).

## Assets

- **The session**: the refresh token in Windows Credential Manager, and the access token in
  memory.
- **Customer data**: callers' numbers, transcripts, messages, contacts and tickets, all of which
  belong to the workspace and its customers.
- **Billed actions**: placing a call, an AI reply, a plan bought in the app.
- **The microphone**, and a call's audio and encryption keys.

## Threat model

| Actor | What they can do | What stops them |
|---|---|---|
| Another program on the same computer | Open a `districtai:` link with a forged sign-in callback. | PKCE and a random `state` checked by the core on every sign-in; only the scheme and host of a link decide what it is for, and the rest is left to the core. |
| Anyone on the network path | Watch or alter traffic. | TLS with certificate verification on every connection the core makes. |
| A hostile or compromised server response | Return any text, any link, any redirect. | No redirects followed; text from the service is shown as text; only a web page becomes a link. |
| A customer or a caller | Put any text in a message, a name or a transcript. | Shown as text; raw HTML and entities stay text. |
| A thief of a refresh token, or a crash mid-refresh | Present a token that was already spent. | Refreshes one at a time; a fingerprint marker written before sending; a restart that finds it signs in again rather than replay. |
| Someone who reads logs or crash reports | Read whatever the app wrote down. | The app writes no log, and everything below warning level is compiled out of the Rust side; crash reports are off until allowed and hold no computer name, user name or IP address. |

Out of scope: Windows Credential Manager's own model (anything running as your user can read
your credentials), the Microsoft Store's signing of what it ships, and the District AI service
itself (report a vulnerability in it through [SECURITY.md](SECURITY.md) all the same).

## Trust boundaries

| Party | Trusted? | Why |
|---|---|---|
| The person at the keyboard | **Yes** | Every billed or destructive action waits for their press. |
| Windows and the user account, Credential Manager included | **Yes** | They hold the refresh token. |
| The District AI service | Yes, for what it answers about the account | It decides roles and data; the app still renders its text as text and never follows its redirects. |
| Other programs and links handed to the app | **No** | Sign-in needs PKCE and `state`; a link is classified by scheme and host only. |
| Customers, callers and model output | **No** | Shown as text. |
| The network | **No** | TLS with verification on every connection. |

## Secure design principles applied

- **Least privilege.** The package asks for the microphone, the first time a call needs it, and
  runs as a full-trust desktop app (`runFullTrust`, as a WinUI 3 app does) with no administrator
  rights and no Windows service or privileged helper (`src/DistrictAI/Package.appxmanifest`). The Rust
  workspace forbids `unsafe` code (`Cargo.toml`, and `scripts/check-unsafe.py` in CI).
- **Fail closed.** A sign-in callback with the wrong `state` is refused before anything in it is
  used; a stored session that cannot be read is treated as corrupt, not guessed at; a crash
  reporting address it will not use is an error, not a fallback.
- **Complete mediation and economy of mechanism.** The C# side renders state and forwards what
  you do; it decides nothing. One core decides for the Windows and Linux apps, and this
  repository adds the Windows adapters and the window.
- **Safe defaults.** Crash reports are off until you allow them, with Don't send as the default
  answer, and a build from source never sends one. Start at sign-in is off until turned on.
- **Separation of secrets.** The refresh token lives only in Credential Manager, the access token
  and call credentials only in memory.

## Common weaknesses countered

Mapped to the CWE Top 25 and the OWASP Top 10. Paths are in this repository unless they start
with "core:".

| Weakness | How it is countered | Tests |
|---|---|---|
| Authentication bypass by a forged or replayed callback (CWE-287, CWE-294, OWASP A07) | OAuth 2.0 with PKCE and a random `state`; a link is sorted by scheme and host only. | core: `crates/district-auth/tests/login.rs` (`a_state_that_is_not_this_attempts_is_refused_before_anything_else`); `crates/district-ffi/src/link.rs` (`the_scheme_and_host_decide`) |
| Insufficiently protected credentials (CWE-522) | No redirects followed, so the bearer token never travels to another host; the live credential goes in a header and a server that echoes it is refused. | core: `crates/district-api/tests/responses.rs` (`a_redirect_is_an_error_and_is_never_followed`), core: `crates/district-live/tests/connection.rs` (`a_server_that_selects_the_credential_is_refused`) |
| Cleartext storage and logging of secrets (CWE-312, CWE-532, OWASP A09) | Refresh token in Credential Manager only; nothing below warning level can be logged. | `crates/district-ffi/src/store/windows.rs` (`credential_manager_keeps_reads_and_forgets`, on the Windows job), `crates/district-ffi/src/store/mod.rs` (`clearing_takes_the_session_and_the_marker_and_leaves_the_outbox`), `crates/district-ffi/src/lib.rs` (`log_ceiling`), core: `crates/district-live/tests/rules.rs` (`every_error_explains_itself_without_the_credential`) |
| Replay of a spent refresh token (CWE-294) | Refresh fingerprint marker, one refresh at a time, sign in again rather than replay. | core: `crates/district-auth/tests/coordinator.rs` (`a_restart_while_the_successor_is_unsaved_ends_in_sign_in_not_replay`); `crates/district-ffi/src/store/mod.rs` (`a_marker_that_is_not_a_fingerprint_is_corrupt`) |
| Injection through rich text (CWE-79 class, CWE-116) | District HQ's rich text is parsed into runs; raw HTML and entities stay text. | `crates/district-ffi/src/rich_text.rs` (`raw_html_and_entities_stay_text`) |
| Opening untrusted links (CWE-601) | Only a web page becomes a link, opened in the system browser. | `crates/district-ffi/src/rich_text.rs` (`only_a_web_page_becomes_a_link`) |
| Unrestricted upload of a file of dangerous type (CWE-434) | A picked file is typed by its first bytes, never by its name, and its name loses its folder and control characters. | `crates/district-ffi/src/files.rs` (`the_name_does_not_decide_the_type`, `hostile_or_short_heads_are_unknown`, `the_name_loses_its_folder_and_control_characters`) |
| Exposure of personal data in crash reports (CWE-200, CWE-359) | Off without consent or without a reporting address; an address it will not use is an error. | `crates/district-ffi/src/crash.rs` (`off_without_a_dsn_or_without_consent`, `a_dsn_it_will_not_use_is_an_error`, `granted_with_a_dsn_is_on_until_denied`) |
| Trusting a push channel it did not ask for (CWE-345) | Only a Windows push (WNS) channel is accepted, and the registration never shows its channel. | `crates/district-ffi/src/push.rs` (`only_a_wns_channel_is_a_channel`, `the_registration_never_shows_its_channel`) |
| Improper certificate validation (CWE-295) | The core's TLS client verifies certificates and has no option to skip it. | Mechanism only, in the core: no code path builds a client without verification. |
| Vulnerable and outdated components (OWASP A06) | `cargo deny` against `deny.toml`, dependency review, Dependabot, locked NuGet restores, and zizmor on the workflows. | `.github/workflows/ci.yml` (`cargo-deny`, `dependency-review`, `zizmor`), `.github/dependabot.yml` |

## Releases

Each release is built by `.github/workflows/release.yml` from a tagged commit, and every file it
carries has a signed provenance attestation made through Sigstore by that workflow; the README's
[Verifying a download](README.md#verifying-a-download) shows how to check one. The Store package
is signed by the Microsoft Store; the sideload test build is signed with a throwaway certificate
made for that release, and is for testing only.

## Reporting

Vulnerabilities are reported privately, as [SECURITY.md](SECURITY.md) describes. How the project
is run is in the organization's
[GOVERNANCE.md](https://github.com/distronode-corporation/.github/blob/main/GOVERNANCE.md).
