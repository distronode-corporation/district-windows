# Architecture

District AI for Windows is a WinUI 3 app over a Rust core. The solution
(`DistrictAI.slnx`, x64) has three source projects and three test projects, and
the Cargo workspace has one crate.

## Layers

```
DistrictAI                 WinUI 3 app (net10.0-windows10.0.26100.0, WinExe)
  |  references                          |
  v                                      |  references (App builds CoreHost)
DistrictAI.Presentation    view models   |  (net10.0, OS-neutral)
  |  references                          |
  v                                      v
DistrictAI.Core            C# host over the FFI (net10.0, OS-neutral)
  |  P/Invoke (UniFFI bindings)
  v
crates/district-ffi        Rust cdylib: district_ffi.dll / libdistrict_ffi.so
  |  Cargo git dependencies
  v
district-core-rust         shared Rust core (district-core, -api, -auth, ...)
```

Each layer calls only the layers below it: the app references both Presentation and Core (`App` constructs `CoreHost` and wires it to the view models), and Presentation references Core. Only the top layer is Windows-specific.

## Projects

| Project | Path | What it holds |
| --- | --- | --- |
| `DistrictAI` | `src/DistrictAI/` | `App`, `MainWindow` and its partial files, the pages (`Views/`), and `Platform/`: tray icon, WNS push, clipboard (including copies of secrets), file picker, calls, checkout, crash reporting (Sentry). Packaged as MSIX (`Package.appxmanifest`, plus `Package.GitHub.appxmanifest` for the GitHub flavour). |
| `DistrictAI.Presentation` | `src/DistrictAI.Presentation/` | One view model per page, grouped by area (Calls, Scheduling, Settings, ...). Uses CommunityToolkit.Mvvm. Reaches Windows only through small interfaces the app implements (`ICoreSink`, `IBrowser`, `IStartupTask`) and `TimeProvider`. |
| `DistrictAI.Core` | `src/DistrictAI.Core/` | `CoreHost` (owns the FFI `Core` object and dispatches its updates to the UI thread), crash scrubbing, power transitions, UI faults, and the generated bindings in `Generated/district_ffi.cs`. |
| `district-ffi` | `crates/district-ffi/` | The boundary the C# side calls: one `Core` object, a view of each screen, and the Windows session store (Credential Manager). Built as `cdylib` (for C#) and `lib` (for its own tests). The `scripted` feature is for test builds only. |
| `DistrictAI.Core.Tests` | `tests/DistrictAI.Core.Tests/` | xUnit v3 tests of `DistrictAI.Core` against the real native library. Run on Linux and Windows. |
| `DistrictAI.Presentation.Tests` | `tests/DistrictAI.Presentation.Tests/` | xUnit v3 tests of the view models, with fakes for the core. Run on Linux and Windows. |
| `DistrictAI.UiTests` | `tests/DistrictAI.UiTests/` | FlaUI smoke tests and scene walks against the installed MSIX. Windows only. |

## Generated code

- `src/DistrictAI.Core/Generated/district_ffi.cs`: from `district-ffi` by
  uniffi-bindgen-cs (`scripts/generate-bindings.sh`; CI's `bindings` job fails
  if the committed file differs).
- Strings: `scripts/gen-l10n.py` generates code from the catalogs in
  `crates/*/l10n/*.toml` and `src/*/l10n/*.toml` with the vendored
  `tools/l10n/l10n.py`. The French sign-off record is `i18n/fr-CA/signoff.json`.

## Build-wide settings

`Directory.Build.props` applies to every C# project: warnings as errors,
nullable on, analyzers at `latest-recommended`, `Deterministic`, and
`ContinuousIntegrationBuild` when `CI=true`. `Directory.Packages.props` pins
every package version centrally, and each project restores from its committed
`packages.lock.json` in locked mode.
