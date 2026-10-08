# District AI for Windows

The native Windows client for [District AI](https://www.distronode.com), the AI voice
receptionist from Distronode. It is a WinUI 3 app written in C#, and everything it decides
(signing in, what each screen shows, live updates, calls) comes from the same Rust core as
[District AI for Linux](https://github.com/distronode-corporation/district-linux).

> **Status: in development.** There is no release yet, and nothing here is ready to install.
> This README describes what the app is being built to do; it says so wherever a part does
> not exist yet.

## What it is for

The app is a desktop window onto a District AI workspace: the inbox, calls and their
transcripts, contacts, District HQ, analytics and the workspace's settings. Calls can ring
on the desktop and be answered there, as on the Linux and Mac apps. It mirrors the
destinations of the Android app.

You need a District AI account to use it.

## Platform

| | |
|---|---|
| Windows | Windows 10 version 2004 or later, and Windows 11, on x64. Windows 11 on ARM runs the x64 build |
| Distribution | The Microsoft Store (planned for 1.0) |
| Language | English |

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
