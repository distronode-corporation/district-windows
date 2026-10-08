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
