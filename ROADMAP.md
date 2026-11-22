# Roadmap

The near-term plan for District AI for Windows, and what it will not do. It is taken from the
repository's open issues and its own unfinished items, each named with where it comes from. It
covers the next twelve months, to November 2027. Items have no dates of their own: an item is
done when the change that does it is merged and released, and it then leaves this list for
[CHANGELOG.md](CHANGELOG.md). Anything here can change; open an issue to discuss an item or to
propose a new one.

## Near term

- **A signed GitHub build.** The GitHub flavour of the app is built and attached to each release
  unsigned, for the SignPath Foundation code-signing application; its publisher is provisional
  until that certificate is issued (README, "Releases"; CONTRIBUTING.md, "Flavours"). Until then
  the Microsoft Store is the way to install the app, and the sideload build is for testing only.
- **Off chrono** ([#147](https://github.com/distronode-corporation/district-windows/issues/147)).
  `chrono`, which RUSTSEC-2026-0543 marks unmaintained, reaches every build through the calls
  stack the core brings in, and is tolerated in `deny.toml` until then. It leaves `Cargo.lock`
  once the LiveKit crates drop it, district-core-rust takes them, and this repository moves to
  that core tag.

## Open questions

- **Using the app without a District AI account.** Today the app needs a District AI account to
  sign in. Whether it can work without one, and what that would look like, is an open question
  we are asking about before building anything (README, "Without an account with us";
  [the discussion](https://github.com/distronode-corporation/.github/discussions/1)).

## Not planned

What the app will not do, as the README and [SECURITY.md](SECURITY.md) set it out.

- **Decisions in the C# side.** The C# side renders state and forwards what you do; everything
  the app decides stays in the shared Rust core, which is what keeps the Windows and Linux apps
  behaving the same (README, "How it is built").
- **A log file, or crash reports by default.** The app writes no log of its own, and sends a
  crash report only after you allow it (SECURITY.md, "The security model, in short").
- **The District AI service.** The service the app talks to is not open source and is not part
  of this repository (README, "What it is for").

A change to any of these would be proposed as an issue first.

## How the project is run

Who decides what goes on this list, and how, is in the organization's
[GOVERNANCE.md](https://github.com/distronode-corporation/.github/blob/main/GOVERNANCE.md).
