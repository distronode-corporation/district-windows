# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Keyboard shortcuts in a call, from anywhere in the window: Ctrl+D turns the microphone
  off or on, and Ctrl+Shift+H hangs up. The buttons' tooltips name them.

## [1.1.0] - 2026-10-08

### Fixed

- The app carries its own .NET runtime, so it starts on a computer without the .NET Desktop
  Runtime. 1.0.0 needed it installed and did not start without it.

- When Windows announces that the computer is going to sleep or hibernate, District AI
  stops ringing on it first (ending any call under way), and rings on it again when it
  wakes. Before, a sleeping computer could stay listed to ring for up to ten minutes, so a
  caller could wait for a ring nobody heard, and after waking it could take minutes to
  ring again.

### Added

- Each release also carries the GitHub flavour of the app as an unsigned MSIX, for the
  code-signing application. It is not meant to be installed; the README says what each
  release file is for.

## [1.0.0] - 2026-10-08

The first release of District AI for Windows, for the Microsoft Store.

### Added

- Sign in with an existing District AI account (Google, Microsoft or email) in your own
  browser, so the app never sees your password. The session is kept in Windows
  Credential Manager and resumes when the app starts. Accounts are created on the web.
- The window: Overview, Inbox, Calls, Contacts and Account, with a back button, the
  workspace switcher, the unread count, and banners for notices and live updates.
- Overview: the workspace's figures, its recent calls, and a card that opens the web
  dashboard to finish setting up.
- Inbox, to read: conversations, search, and each conversation's messages and calls.
  Opening a conversation marks it read. Replies are sent from the web dashboard or the
  District AI phone apps.
- Calls: the call log, and each call's details, AI summary, analysis and transcript.
- Contacts, to read: the list and each contact's details and AI caller profile.
- Report on AI-generated content (a call's summary, a contact's profile, a call in a
  conversation): a support request that names the item by its id, with an optional note.
  Members whose role cannot raise support requests report on the web.
- Voice calls: place a call from the dialler or from a call or contact, and answer or
  decline an incoming call from the window or from its notification, with a ringtone.
  A call rings on this computer while the app is running, in the window or in the
  notification area. "Ring on this computer" is on the Account page.
- Account: the app's version, this device's and your user id, the devices signed in to
  your account (sign out one, or all of them), deleting your account on the web, and
  signing out.
- The notification area: closing the window keeps District AI running there, and its
  menu opens the window, signs out or quits.
- Start at sign-in, off until you turn it on.
- `crates/district-ffi`: the boundary the app calls, over district-core-rust 1.2.0, with
  a projection of every screen pinned by JSON snapshots, and the call engine.
- `DistrictAI.Core`: the generated C# bindings and `CoreHost`, tested on Linux and Windows.
- `DistrictAI`: the WinUI 3 app, packaged as an MSIX, with single-instance activation and
  the `districtai` protocol.
- The audio-only libwebrtc the call engine links, built from source without the H.264
  and H.265 codecs or FFmpeg, and published as a release with its digest and build
  provenance (`.github/workflows/libwebrtc-windows.yml`).
- Releases: the Store package, a sideload test package with its certificate, the Windows
  App Certification Kit's report, SHA256SUMS and build provenance attestations.
- The repository: licence, community files and the public hygiene and store-copy checks.
