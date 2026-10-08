# Security policy

## Reporting a vulnerability

Please report it privately. Do not open a public issue, discussion or pull request for it.

- Use GitHub's [private vulnerability reporting](https://github.com/distronode-corporation/district-windows/security/advisories/new), or
- if you cannot use GitHub, email opensource@distronode.com.

Include what you found, how to reproduce it, and what you think the impact is. We will
acknowledge the report, keep you informed while we fix it, and credit you in the advisory
unless you would rather we did not.

A vulnerability in the District AI service itself (rather than in this app) is welcome
through the same channels; we will route it.

## Supported versions

There is no release yet. Once there is, only the latest release receives security fixes.

## The security model, in short

These are the rules the app is being built to. Each will be stated in full, with the code
that enforces it, before the first release.

- **Sign-in** happens in your browser, with OAuth 2.0 and PKCE. The app never sees your
  password. The verifier lives only in the memory of the running app, and a second copy of
  the app that Windows starts to deliver the sign-in link hands it to the first and exits.
- **Tokens.** The refresh token is kept in Windows Credential Manager, never in a file. The
  access token lives only in memory and lasts ten minutes.
- **Logging.** The app writes no log of its own, and its Rust core is compiled so that
  nothing below warning level can be logged at all, because some libraries it depends on
  would otherwise log secrets at debug level.
- **Calls** are end-to-end encrypted between participants.
