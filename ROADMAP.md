# Roadmap

The near-term plan for District AI for Windows. This repository has no open issues, so the list
is taken from the repository's own unfinished items, each named with where it comes from. It has
no dates: an item is done when the change that does it is merged and released, and it then leaves
this list for [CHANGELOG.md](CHANGELOG.md). Anything here can change; open an issue to discuss an
item or to propose a new one.

## Near term

- **Two-step verification and Delete account, released.** Both are merged since 3.4.0 (#145) and
  listed under `[Unreleased]` in [CHANGELOG.md](CHANGELOG.md). The plan is to ship them in the
  next release.
- **A signed GitHub build.** The GitHub flavour of the app is built and attached to each release
  unsigned, for the SignPath Foundation code-signing application; its publisher is provisional
  until that certificate is issued (README, "Releases"; CONTRIBUTING.md, "Flavours"). Until then
  the Microsoft Store is the way to install the app, and the sideload build is for testing only.

## Open questions

- **Using the app without a District AI account.** Today the app needs a District AI account to
  sign in. Whether it can work without one, and what that would look like, is an open question
  we are asking about before building anything (README, "Without an account with us";
  [the discussion](https://github.com/distronode-corporation/.github/discussions/1)).

## How the project is run

Who decides what goes on this list, and how, is in the organization's
[GOVERNANCE.md](https://github.com/distronode-corporation/.github/blob/main/GOVERNANCE.md).
