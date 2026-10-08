# Contributing

Thanks for looking. 1.0.0 is released (see the README's
[Releases](README.md#releases)), and this file grows as each part lands.

## Layout

```
crates/district-ffi/          Rust: the boundary the C# app calls (state per screen, events in)
src/DistrictAI.Core/          C#: the generated bindings and the host that feeds the UI thread
src/DistrictAI/               C#: the WinUI 3 app and its MSIX package
tests/                        C# tests; the core's own tests live with the core
scripts/                      Repository checks
```

The core itself (the model, the API client, sign-in, live updates and the call engine) is
not in this repository. It is
[district-core-rust](https://github.com/distronode-corporation/district-core-rust), shared
with District AI for Linux, and this repository pins it by tag. A change to what the app
decides belongs there.

## Building

The app builds on Windows only, because the XAML compiler runs only on Windows. CI builds
every pull request on GitHub's Windows runners. The OS-neutral parts (the Rust boundary and
`DistrictAI.Core`) also build and test on Linux and macOS.

You need Rust (stable; the floor is `rust-version` in `Cargo.toml`) and the .NET SDK that
`global.json` names.

```
cargo test -p district-ffi                        # the boundary, its snapshot tests included
cargo build -p district-ffi                       # the native library DistrictAI.Core loads
dotnet test tests/DistrictAI.Core.Tests           # the C# library against it
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
`.resw` and `district-ffi`'s Rust strings (comments are not read). CI runs it on every push
and pull request:

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
