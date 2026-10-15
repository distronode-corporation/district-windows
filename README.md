# District AI for Windows

[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/distronode-corporation/district-windows/badge)](https://scorecard.dev/viewer/?uri=github.com/distronode-corporation/district-windows)

The native Windows client for [District AI](https://www.distronode.com), the AI voice
receptionist from Distronode. It is a WinUI 3 app written in C#, and everything it decides
(signing in, what each screen shows, live updates, calls) comes from the same Rust core as
[District AI for Linux](https://github.com/distronode-corporation/district-linux).

> **Status: District AI is on the
> [Microsoft Store](https://apps.microsoft.com/detail/9P9F0NNFJ3CP).** Install it from
> there, or with `winget install --id 9P9F0NNFJ3CP --source msstore`. Each release on
> [GitHub Releases](https://github.com/distronode-corporation/district-windows/releases)
> also carries the Store package and a test build for sideloading, for testing (see
> [Releases](#releases)).

**Links:** [project page](https://www.distronode.com/open-source/district-windows),
[GitLab mirror](https://gitlab.com/distronode-corporation/district-windows)
(read-only mirror; issues and pull requests live on GitHub),
[CHANGELOG](CHANGELOG.md).

## What it is for

The app is a desktop window onto a District AI workspace: the inbox, calls and their
transcripts, contacts, District HQ, analytics and the workspace's settings. Calls can ring
on the desktop and be answered there, as on the Linux and Mac apps. Calls and messages
also reach it when the app is closed (the Microsoft Store build, through Windows push
notifications); the GitHub build rings only while it runs. It mirrors the destinations of
the Android app.

Signing in needs a District AI account, and this is the one District AI app where you can
create an account and buy a plan from the app itself. The app is open source; the District
AI service it talks to is not.

**Without an account with us.** We want the District AI apps to work without an account
with us too. We have not worked out what that looks like or whether it can work, and the
answer depends on what people would use them with, so we are asking before we build
anything:
[tell us what you would connect them to](https://github.com/distronode-corporation/.github/discussions/1).

## Platform

| | |
|---|---|
| Windows | Windows 10 version 2004 or later, and Windows 11, on x64. Windows 11 on ARM runs the x64 build |
| Distribution | The [Microsoft Store](https://apps.microsoft.com/detail/9P9F0NNFJ3CP) (`winget install --id 9P9F0NNFJ3CP --source msstore`); [GitHub Releases](https://github.com/distronode-corporation/district-windows/releases) for the Store package and a sideload test build, for testing |
| Language | English and Canadian French (fr-CA). The app speaks French when the first English or French language in your Windows display languages is French, in any region (fr-CA, fr-FR, fr-BE, fr-CH), and English otherwise |

## Releases

Each release on
[GitHub Releases](https://github.com/distronode-corporation/district-windows/releases) is
built by `.github/workflows/release.yml` from a tagged commit on `main`, and carries:

| File | What it is for |
|---|---|
| `DistrictAI_<version>_x64.msixupload` | The Store package, unsigned, as it goes to Partner Center. The Store signs what it ships; you cannot install this file yourself |
| `DistrictAI_<version>_x64_sideload-test.msix` | A test build for sideloading, signed with a throwaway certificate made for that release |
| `DistrictAI_<version>_sideload-test.cer` | The public half of that certificate, which Windows must trust before it installs the test build |
| `DistrictAI_<version>_x64_sideload-dependencies.zip` | The framework packages the test build needs (the Windows App Runtime), which a fresh Windows does not have |
| `DistrictAI_<version>_x64_github-unsigned.msix` | The GitHub flavour of the app, unsigned, for the SignPath Foundation code-signing application. An unsigned package does not install normally, so it is not for testers. Releases after 1.0.0 carry it |
| `DistrictAI_<version>_wack-report.xml` | The Windows App Certification Kit's report on the package |
| `district-windows-<version>-source.zip` | The source at the tagged commit |
| `SHA256SUMS` | The SHA-256 of each file above |
| `district-ai-windows_<version>.intoto.jsonl` | The signed provenance attestation over those files |

### Installing the sideload test build

The test build is for testing; to use District AI, install it from the Microsoft Store.
The test build has the same identity as the Store copy, so remove the Store copy first.
Download the `.msix`, the `.cer` and the dependencies `.zip` of one release into a
folder, open PowerShell as administrator in that folder, and run (with `<v>` the version,
for example `1.1.0`):

```
Import-Certificate -FilePath .\DistrictAI_<v>_sideload-test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Expand-Archive .\DistrictAI_<v>_x64_sideload-dependencies.zip deps
Get-ChildItem deps -Filter *.msix | ForEach-Object { Add-AppxPackage $_.FullName }
Add-AppxPackage .\DistrictAI_<v>_x64_sideload-test.msix
```

The first line makes this computer trust packages signed with that release's test
certificate. To undo it, uninstall the app and delete the certificate from Local Machine,
Trusted People (`certlm.msc`).

### Verifying a download

`SHA256SUMS` names every file of the release. In the folder you downloaded into (with
`--ignore-missing` if you took only some of the files):

```
sha256sum -c SHA256SUMS
```

Each file also has a signed provenance attestation, which shows it was built by this
repository's release workflow. With the [GitHub CLI](https://cli.github.com):

```
gh attestation verify <file> --repo distronode-corporation/district-windows
```

## How it is built

| Layer | What |
|---|---|
| Window and controls | WinUI 3 on the Windows App SDK, C# on .NET 10 |
| Everything the app decides | The shared Rust core (model, API client, sign-in, live updates, call engine), called from C# through bindings generated with [UniFFI](https://github.com/mozilla/uniffi-rs) and [uniffi-bindgen-cs](https://github.com/NordSecurity/uniffi-bindgen-cs) |
| Calls | The core's call engine on the LiveKit Rust SDK, linked against an audio-only build of libwebrtc (no H.264, H.265 or FFmpeg) |

The C# side renders state and forwards what you do. It does not decide anything, which is
what keeps the Windows and Linux apps behaving the same.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Questions and ideas go to
[Discussions](https://github.com/distronode-corporation/district-windows/discussions), bugs
to [Issues](https://github.com/distronode-corporation/district-windows/issues), and security
reports to [SECURITY.md](SECURITY.md), never a public issue.

## Licence and trademarks

The code is licensed under the [Apache License 2.0](LICENSE); see [NOTICE](NOTICE).

The licence covers the code, not the names. "District AI", "Distronode" and the app's icon
are trademarks of Distronode Corporation. A fork that you distribute must use its own name,
its own icon and its own package identity.
