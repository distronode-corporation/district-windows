# Contributing

Thanks for looking. The app is in early development, so the layout below is the plan, and
this file grows as each part lands.

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

## Commits and pull requests

Say why in the commit message, not only what. Keep a pull request to one concern. Every
pull request runs CI, and `main` accepts only pull requests whose checks pass.
