# Contributing

Thanks for looking. 1.0.0 is released (see the README's
[Releases](README.md#releases)), and this file grows as each part lands.

## Layout

```
crates/district-ffi/          Rust: the boundary the C# app calls (state per screen, events in)
src/DistrictAI.Core/          C#: the generated bindings and the host that feeds the UI thread
src/DistrictAI.Presentation/  C#: the view models, OS-neutral, one folder per area
src/DistrictAI/               C#: the WinUI 3 app (pages, platform services) and its MSIX package
tests/                        C# tests; the core's own tests live with the core
scripts/                      Repository checks
```

`DistrictAI.Presentation` holds what each page shows and what its buttons send, in the
namespace `DistrictAI.ViewModels`: it copies the core's view records into bindable
properties and forwards the user's actions to the core as events. It references
`DistrictAI.Core` and CommunityToolkit.Mvvm and nothing from WinUI or the Windows App SDK,
so it builds and tests on Linux. What only Windows has reaches it through a small seam the
app fills: `ICoreSink` (the core, `CoreHost`), `IBrowser`, `IStartupTask` (start at
sign-in, `StartupRegistration`) and `TimeProvider` (the call bar's clock). Anything that is
only XAML (an alignment, a brush) stays in the page. `tests/DistrictAI.Presentation.Tests`
tests the view models against the core's generated records directly, with no native
library.

The core itself (the model, the API client, sign-in, live updates and the call engine) is
not in this repository. It is
[district-core-rust](https://github.com/distronode-corporation/district-core-rust), shared
with District AI for Linux, and this repository pins it by tag. A change to what the app
decides belongs there.

## Building

The app builds on Windows only, because the XAML compiler runs only on Windows. CI builds
every pull request on GitHub's Windows runners. The OS-neutral parts (the Rust boundary,
`DistrictAI.Core` and `DistrictAI.Presentation`) also build and test on Linux and macOS.

You need Rust (stable; the floor is `rust-version` in `Cargo.toml`) and the .NET SDK that
`global.json` names.

```
cargo test -p district-ffi                        # the boundary, its snapshot tests included
cargo build -p district-ffi                       # the native library DistrictAI.Core loads
dotnet test tests/DistrictAI.Core.Tests           # the C# library against it
dotnet test tests/DistrictAI.Presentation.Tests   # the view models (no native library)
```

On Windows, the app itself (x64):

```
set AWS_LC_SYS_PREBUILT_NASM=1
cargo build -p district-ffi --release
dotnet build src/DistrictAI -c Release -p:Platform=x64 -p:DistrictFfiProfile=release
```

### Building with calls

On Windows, `district-ffi` links the LiveKit call engine (district-call's `livekit`
feature), and with it libwebrtc: this project's own audio-only build, without the H.264
and H.265 codecs or FFmpeg. `scripts/fetch-libwebrtc.ps1` downloads the release it pins,
checks its SHA-256 and unpacks it; point `LK_CUSTOM_WEBRTC` at the directory it prints
before building, or the SDK downloads LiveKit's own prebuilt, which carries those codecs.

```
$env:LK_CUSTOM_WEBRTC = ./scripts/fetch-libwebrtc.ps1 $env:LOCALAPPDATA\district-libwebrtc
```

libwebrtc is built with the static C runtime, so `.cargo/config.toml` builds Rust with
`+crt-static` on Windows. The library itself is built by
`.github/workflows/libwebrtc-windows.yml` (`scripts/build-libwebrtc.ps1`), by hand, and
published as a release with a build provenance attestation; moving the pin is a pull
request that changes `scripts/fetch-libwebrtc.ps1`.

### Bindings

`src/DistrictAI.Core/Generated/district_ffi.cs` is written by
[uniffi-bindgen-cs](https://github.com/NordSecurity/uniffi-bindgen-cs) from the library's
own metadata, and committed. After changing anything `district-ffi` exports, run
`scripts/generate-bindings.sh` and commit the result; CI regenerates it and fails on any
difference. The generator's version and UniFFI's are pinned to each other exactly.

### Projections

What C# reads (`ShellView`, `ScreenView`) is written in `crates/district-ffi`, matching the
core's enums with no catch-all arm, so a core change that adds a state fails the build here.
`crates/district-ffi/tests/snapshots/` pins the JSON of each; after a deliberate change,
`UPDATE_SNAPSHOTS=1 cargo test -p district-ffi --test projections` writes them again.

### Flavours

The app builds in two flavours from the same sources, as two separate packages:

- **Store** (the default): `src/DistrictAI/Package.appxmanifest`, with the identity Partner
  Center reserved. The Store package and the sideload test build are this flavour.
- **GitHub**: `src/DistrictAI/Package.GitHub.appxmanifest`, built with
  `-p:DistrictFlavour=GitHub`, under its own identity (`Distronode.DistrictAI.GitHub`) and
  its own notification activator class. Its publisher is meant for SignPath Foundation's
  certificate and is provisional until that certificate is issued. Releases carry it
  unsigned, for the code-signing application only.

`scripts/check-flavours.py` keeps the two manifests the same app under two identities: they
must differ in the identity name, the publisher and the activator's COM class, may differ in
the display name, and must agree on everything else, the version included. CI runs it, and
its self-test, on every push and pull request; `windows-app` also builds and packages the
GitHub flavour, unsigned, so a flavour that breaks fails the pull request.

```
python3 scripts/check-flavours.py --self-test
python3 scripts/check-flavours.py
```

A change to `Package.appxmanifest` goes into `Package.GitHub.appxmanifest` in the same pull
request.

### Packages

Every NuGet version is in `Directory.Packages.props`, and each project has a
`packages.lock.json` that restore refuses to change. After changing a version, run
`dotnet restore -p:RestoreLockedMode=false` and commit the lock files with it.

### UI smoke tests

`tests/DistrictAI.UiTests` drives the installed app through UI Automation, with
[FlaUI](https://github.com/FlaUI/FlaUI): it starts the package by its application user
model ID and checks that it reaches the sign-in page, that a `districtai://` link opened
while it runs goes to the one instance, that closing the window hides it to the tray, and
that the tray menu's Quit ends it. Nothing signs in or reaches the service. The project
builds on any OS, but only Windows runs it; elsewhere `dotnet test` passes over it.

CI's `windows-app` job runs it on every push and pull request, against the test MSIX it
has just built, through `scripts/run-ui-tests.ps1`: the script trusts the package's
throwaway certificate for the run, installs the package and its framework dependencies,
runs the tests and always removes the package again. To run them on Windows, download the
`district-ai-test-msix` artifact of a CI run (or package the app yourself, as the job
does), then, in PowerShell 7 as administrator:

```
./scripts/run-ui-tests.ps1 -Packages <the unpacked artifact>
```

It refuses to run while the Store copy of District AI is installed, since both have the
same identity. With a package already installed by hand, `dotnet test
tests/DistrictAI.UiTests` runs the tests against it directly; they skip when it is not
installed.

### Scripted scenes and the area walk

`district-ffi` has a `scripted` feature (`crates/district-ffi/src/scripted.rs`): started
with `--district-scripted-scene=signed-in` on its command line, a build with it runs the
core against the core's own contract fixtures instead of the service, signed in to one
workspace ("Example Dental"), with no network and nothing stored. It exists so that
every page can be rendered and photographed without an account. Without the argument, a
scripted build runs as any other.

The feature never ships. `scripts/check-scripted.py` looks for its marker in a
`district_ffi.dll` or in the packages that hold one; CI runs it on the DLL and the test
MSIX it builds, and `release.yml` on every DLL and package a release carries.

CI's `windows-app` job also builds the DLL with the feature into a separate scripted test
MSIX, never uploaded, and runs the area walk against it (`SceneWalkTests`): every entry
the navigation pane offers is opened, and each page must show its heading, finish loading
and show no failure; a page a button opens (the blocked callers, from Contacts) is
walked too. Each page's client area is saved at 1920x1080 in the
`district-ai-screenshots` artifact, with the whole window once (`00-window.png`). The scenes' own Rust tests (`cargo test -p district-ffi --features scripted
--lib scripted`) check the same thing against the projections first, and that each
fixture still decodes. When an area is built, its effects need answers in `scripted.rs`
if the core sends ones it does not answer yet, and its page's heading a line in
`SceneWalkTests`.

To build and walk it on Windows:

```
cargo build -p district-ffi --release --features scripted
dotnet build src/DistrictAI -c Release -p:Platform=x64 -p:DistrictFfiProfile=release `
  -p:GenerateAppxPackageOnBuild=true -p:AppxPackageDir=<out>\ ...   # signed as CI signs it
./scripts/run-ui-tests.ps1 -Packages <out> -Scripted -Screenshots <folder>
```

## Public hygiene

This repository is public, and `scripts/check-public-hygiene.py` keeps it that way. CI runs
it on every push and pull request; run it yourself before you commit:

```
python3 scripts/check-public-hygiene.py --self-test
python3 scripts/check-public-hygiene.py
```

It refuses em and en dashes (use commas, periods or parentheses), phone numbers other than
the fictional +1 NPA 555-0100 to 555-0199, host names under our domains other than the
public website, and email addresses other than the project's contacts and example.com.

## Store copy

District AI 1.0 is listed in the Microsoft Store as a client for an existing account: nobody
signs up in the app and nothing is bought in it. `scripts/check-store-copy.py` keeps the
app's strings honest to that listing. It refuses "sign up", "create an account", "buy",
"price", "pricing", "subscribe" and "free trial" in the app's XAML text, its C# strings, any
`.resw` and `district-ffi`'s Rust strings (comments are not read). It also reads every string
value in `crates/district-ffi/tests/snapshots/*.json`, the text the core projects for each
screen state (plan names, prices), and reports a hit by file and JSON path. CI runs it on
every push and pull request:

```
python3 scripts/check-store-copy.py --self-test
python3 scripts/check-store-copy.py
```

A string that is genuinely fine goes in its `ALLOW` list by exact `path:line`, with the
reason.

## Releasing

A release is a `vX.Y.Z` tag on a commit already on `main`, and
`.github/workflows/release.yml` does the rest. `main` accepts only pull requests whose
checks pass, so tag a commit whose CI on `main` is green. Before tagging:

- `Version` in `src/DistrictAI/Package.appxmanifest` (and so in
  `Package.GitHub.appxmanifest`, which `check-flavours.py` holds to it) is `X.Y.Z.0`;
- `CHANGELOG.md` has a `## [X.Y.Z]` section, which becomes the release notes.

The workflow's first job refuses the tag when the tagged commit is not on `main` (CI never
tested it), when the manifest's `Version` does not match the tag, or when `CHANGELOG.md`
has no section for it. It then builds, with no caches, the Store package (an unsigned
`.msixupload`; the Store signs what it ships), a sideload test MSIX signed with a throwaway
certificate, and the GitHub flavour as an unsigned MSIX, checks each package's identity,
version and ReadyToRun code, and runs the Windows App Certification Kit on the test MSIX.
The next job checks the files are exactly the names a release carries and writes
`SHA256SUMS`. Only the last job can write: it attests every file's provenance, verifies the
attestations, creates a draft release, checks GitHub holds exactly the expected files, and
only then publishes it. The `.msixupload` is what goes to Partner Center; the README's
[Releases](README.md#releases) says what each file is for.

Pull requests that change the workflow or the packaging run the same build and
certification as a dry run, with nothing attested or released. CI's `windows-app` job runs
the certification kit on every pull request too, through `scripts/run-wack.ps1`.

### Testing the sideload package

The test MSIX is signed with a throwaway certificate and depends on the Windows App
Runtime, which a fresh Windows does not have. From a release, download
`DistrictAI_<v>_x64_sideload-test.msix`, `DistrictAI_<v>_sideload-test.cer` and
`DistrictAI_<v>_x64_sideload-dependencies.zip` into one folder, then run PowerShell as
administrator in that folder:

```
Import-Certificate -FilePath .\DistrictAI_<v>_sideload-test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Expand-Archive .\DistrictAI_<v>_x64_sideload-dependencies.zip deps
Get-ChildItem deps -Filter *.msix | ForEach-Object { Add-AppxPackage $_.FullName }
Add-AppxPackage .\DistrictAI_<v>_x64_sideload-test.msix
```

The test build has the Store flavour's identity. The GitHub flavour is a separate package;
once it is signed and installable, install one copy, not both.

## Commits and pull requests

Say why in the commit message, not only what. Keep a pull request to one concern. Every
pull request runs CI, and `main` accepts only pull requests whose checks pass.

## Areas

From 2.0 the app grows one area at a time (District HQ, analytics, phone numbers, billing,
workflows, booking pages, the help desk, support, meeting rooms, the blocked callers, the
reply box, the workspace settings hub and each of its sections), each built by its own pull
request, and several at once. So that two of them never edit the same file, each area
already has its own files, with a placeholder in each, and a pull request for an area
changes only these:

- `crates/district-ffi/src/<area>.rs`, or `src/settings/<section>.rs` for a settings section
  (`src/settings/mod.rs` is the hub's): its views, its `<Area>Action` and the core events
  each action is, and the projection of its screens;
- `crates/district-ffi/tests/projections/<area>.rs` (or `settings/<section>.rs`), and the
  snapshots its cases write under `tests/snapshots/`;
- `src/DistrictAI/Views/<Area>/**` (a settings section's under `Views/Settings/<Section>/`);
- `src/DistrictAI.Presentation/<Area>/**` and `tests/DistrictAI.Presentation.Tests/<Area>/**`,
  its view models and their tests (a settings section's under `Settings/<Section>/` in
  each).

Until then an area's screens are the "Not in this version yet" page and the navigation pane
does not offer it. It goes live when its pull request sets `BUILT = true` in its module:
the pane then offers it to every role `Capabilities::allows` (district-core's `role.rs`).

Setting `BUILT` puts the area in the scripted walk (see "Scripted scenes and the area
walk"), which every pull request must pass, so building an area also means:

- a level-one heading on its page equal to its pane entry's name, which the walk expects
  (`tests/DistrictAI.UiTests/SceneWalkTests.cs`; only a page whose heading differs goes in
  `_headings`, and a page a button opens rather than a pane entry goes in `_subPages`);
- an answer in `crates/district-ffi/src/scripted.rs` for each effect its screens send
  that the scene does not answer yet, from the core's fixtures. `cargo test -p district-ffi
  --features scripted --lib scripted` fails on a built screen left loading or failed.

These two are shared files an area's pull request does change.

The shared files are the manager's, and an area's pull request leaves them alone (say in
the pull request if one has to change, and why): `screen.rs`, `events.rs`, `shell.rs`,
`lib.rs`, `views.rs`, `core.rs` and `nav.rs` in `crates/district-ffi/src/`;
`MainWindow.*`, `App.*` and `Program.cs` in `src/DistrictAI/`;
`src/DistrictAI.Core/Generated/district_ffi.cs` (regenerated, never edited);
`Cargo.toml`, `Cargo.lock`, `Directory.Packages.props`; `.github/workflows/ci.yml` and
`release.yml`; `CHANGELOG.md`; and `Package*.appxmanifest`.

An area's own types still reach `district_ffi.cs`, so every area's pull request
regenerates it (`scripts/generate-bindings.sh`). When another lands first, rebase and
regenerate it; never merge it by hand. The snapshots record only which navigation entry is
selected (`nav_selected`), so building an area changes no other area's snapshots; the
pane's entries are pinned by `nav.rs`'s own tests.
