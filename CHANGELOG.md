# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- The repository: licence, community files and the public hygiene check.
- `crates/district-ffi`: the boundary the app calls, over district-core-rust 1.2.0. One
  `Core` object (start, send, open a link, power changes, the window's frame and screen,
  shut down) with a C# callback for each new revision and for opening the browser; the
  sign-in page's projection, pinned by JSON snapshots; the session in Windows Credential
  Manager.
- `DistrictAI.Core`: the generated C# bindings and `CoreHost`, tested on Linux and Windows.
- `DistrictAI`: the WinUI 3 app, packaged as an MSIX, with single-instance activation, the
  `districtai` protocol, the notification activator and a sign-in page.
- The W2 spikes as CI jobs (`.github/workflows/spikes.yml`): async callbacks at volume,
  protocol redirect, Credential Manager under `taskkill /f`, the IncomingCall toast, Native
  AOT, the checkout in WebView2, and the Store and GitHub flavours side by side.
- The GitHub flavour's manifest (`Package.GitHub.appxmanifest`, its own identity and
  activator class) and the notice when both copies are installed.
