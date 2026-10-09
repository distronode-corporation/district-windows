# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

<!-- manager: add the no-workspace plan chooser entry when that PR lands -->
- The window offers every area of District AI: District HQ, Analytics, Workflows, Booking
  pages, Help desk, Support, Meeting rooms, Phone numbers, Billing and Workspace settings
  join Overview, Inbox, Calls, Contacts and Account. Each area is offered only to the
  roles that may use it, and a viewer sees no control that would change anything.
- Inbox, the reply box: reply to a conversation from the app, with text and up to five
  images (JPEG, PNG, GIF or WebP, up to 5 MB each; images only on text-message
  conversations). Ctrl+Enter sends. "Draft a reply with AI" (billed, and it says so)
  writes a suggested reply into the box for you to read and edit before sending, marked
  "AI draft", with Report beside it. An unsent reply is kept and comes back when the
  conversation is opened again. A viewer, or a conversation with no address to reply
  to, gets no box and a line saying why.
- Calls, the live transcript: during a call, a "Live transcript" panel under the call
  strip shows what each side says as it is said, and the full transcript once the call
  ends. It follows new lines while you are at the end and stays put when you scroll up,
  and Narrator reads each finished line once. A call with no live transcript says that
  its transcript is in the call log afterwards.
- Contacts: add a contact (a name with a phone number or an email address), edit one,
  delete one, and block or unblock a caller. "Run research" (billed, and it says so)
  starts the AI research behind a contact's caller profile, and "Clear research" removes
  it. Delete, Block, Unblock and Clear research each ask first.
- The blocked callers list, from Contacts: who is blocked, with the number and when, and
  Unblock, asked first. A viewer reads the contacts and the list and changes nothing.
- District HQ: ask the workspace assistant in plain words. Answers show as formatted text
  (headings, lists, emphasis and links; only web links open, in your browser). A change
  the assistant proposes shows as a card that changes nothing until you press Confirm,
  and Dismiss drops it. Report on any answer. A viewer can ask, but is never offered
  Confirm.
- Analytics: the call figures for the last 7, 30 or 90 days with the change against the
  period before, the calls per day (or per week) as a chart, the call funnel, caller
  sentiment, this month's usage and the last three months' minutes and messages. Each
  chart is read by Narrator as one sentence, and the sentiment bands are told apart by
  pattern as well as colour, so they stay distinct in high contrast.
- Workflows: the workspace's workflows with their triggers and last runs, a switch to
  turn each one on or off, and each workflow's runs, page by page, with what each step
  did or why a run failed. The outbound campaign's card pauses or resumes it, asked
  first (resuming spends call and message credit, and the question says so). The
  campaign's other settings are changed on the web. A viewer reads everything and
  changes nothing.
- Booking pages: where the workspace's booking pages stand, turning them on, checking
  again while they are set up, and "Manage on the web", which opens them in your browser
  already signed in, without the app ever showing the sign-in link.
- Help desk: the queue with a status filter and counts, raising a ticket, a ticket's
  conversation with its customer and status, replying (the page says whether the
  customer was emailed) and changing the status. The desk's settings, including its
  name and its logo (PNG, JPEG or WebP, up to 5 MB, checked before anything is sent).
  A desk that is off offers to turn it on. Not offered to a viewer.
- Support: the workspace's support requests, open and resolved, each one's
  conversation, replying, and "Mark as resolved", asked first, and raising a new
  request. While a request you are writing is unsent, Report on AI content waits and says
  why, so it never replaces your draft. Not offered to a viewer.
- Meeting rooms, audio only: start or join a room by name, mute and unmute, see who is
  in it, copy a guest link for someone to join from the web (it goes to the clipboard and
  is never shown), and leave. Joining waits while a call holds the microphone. The
  Companion joins every room and writes up the minutes. The meetings list, newest first,
  with Rejoin for one still running, and each meeting's record: its minutes, action items
  and whole transcript, with Report on the minutes and on the action items. A room the
  service sets up with an encryption key is encrypted end to end between the District AI
  apps in it. A viewer joins to listen and gets no guest link. Nothing is recorded.
- Phone numbers: the workspace's numbers, and a search of the numbers for sale with what
  each can do and its monthly charge as the carrier quotes it. Read only: buying,
  releasing or changing a number is done on the web, which "Open the number marketplace
  on the web" opens for a member who may.
- Billing: the workspace's plan with its minutes, and the account's plans and invoices,
  read only, as District AI for Linux shows them. And, for a member whose role can change
  the plan, choosing a plan (Voice Solo, Starter, Pro or Studio, monthly or annual, with
  an optional promotion code) and managing billing inside the app: a confirmation step
  names Stripe before checkout opens, and the service's own checkout and billing pages
  open in a checkout window of their own, signed in afresh each time through a one-time
  link, in a WebView2 profile that starts empty and is deleted afterwards. A computer
  without the WebView2 runtime opens them in the browser instead, and the page says so.
- Workspace settings: the hub lists the sections your role may open, District Studio's
  first. Leaving a section with changes not saved asks "Discard your changes?" first.
  Integrations, Video and the outbound campaign's settings stay on the web.
- Persona: the receptionist's name, greeting and personality, its language and answer
  length, and Save, which sends only what changed. "Try this receptionist" places a real
  call to the receptionist with the settings on screen, saved or not; it is billed like
  any call, and the dialog says so before anything starts.
- Voice (Voice Studio): the voice engine the receptionist speaks through. Pick a starting
  recipe on the Stable or Latest models, change each part of the signal chain (its
  model, the voice and its tuning, with the advanced settings behind a disclosure), see
  the time to first word and where the call is processed, and save. Nothing on the page
  plays audio or costs anything.
- Call handling: who answers first (the receptionist, the receptionist then your
  devices, or your devices first) and how long your devices ring (5 to 30 seconds), for
  the whole workspace, and "Ring me for calls", your own. A viewer sees what is in force
  and why they are not rung.
- Call routing rules: which callers get which voice and instruction, rule by rule, with
  Add, Remove and Save. Save replaces every rule, so it asks first.
- Transfer directory: who a live caller can be put through to, by name and number, with
  Add, Remove and Save, asked first.
- Skills: what the receptionist may do on a call, a switch per skill, and outside research
  on contacts as a switch of its own.
- Knowledge: the documents the receptionist answers from, with where each stands, adding
  one by title and text (billed by its length, and the form says so) or by reading in a
  plain text file (.txt or .md, up to 1 MB), and deleting one, asked first. Where answers
  come from: your own knowledge base, or a linked support knowledge base, which sends
  callers' questions to Atlassian outside the region and is asked about first. A viewer
  reads the documents and changes nothing.
- Messaging accounts: the carrier accounts the workspace sends from and the default
  sender, each channel's sender (SMS, Voice, WhatsApp), the numbers held for the
  workspace, and the owner's mobile number. Add or edit an account with the carrier's
  keys, check the keys against the carrier before saving, and remove an account, asked
  first (its numbers are released). Keys are typed into password boxes and never shown
  again: editing an account starts with every key box empty, and blank keeps the saved
  key. A viewer reads the accounts and changes nothing.
- Members: who belongs to the workspace and what each role may do. An agency member can
  add a member by email address with a role, change a role and remove a member (asked
  first); adding someone sends no invitation, they get access when they sign in with that
  address. The service refuses a change that would leave no agency member. Agency and
  client members can rename the workspace. Not offered to a viewer.
- Account: "Purchases on this computer", "Sign in every time" (the default) or "Off",
  which hides every purchase action.
- Create an account from the welcome screen: the browser opens the District AI sign-in
  page, which offers to create one, and the new account comes back to the app signed in.
  Until it has a workspace, the overview says so.
- With both the Microsoft Store copy and the GitHub copy installed, the sign-in page
  holds browser sign-in and account creation and explains why (the browser could hand
  the sign-in back to the other copy), and the window title names which copy this is.
- A new message gives a notification, and pressing it opens the conversation.
- The District icon in the title bar.

### Changed

- The shared core is district-core-rust 3.0.0, which adds buying in the app (on only in
  this app), after 2.0.0's live transcript of a call, the app's platform at sign-in (so
  the sign-in page can offer to create an account), and saving a reply still waiting to
  be saved when the app quits.

### Fixed

- The program file carries the app's version. 1.1.0's said 1.0.0.0.
- The workspace picker in the navigation pane no longer draws over the "District AI"
  title; it sits on its own line below it.

## [1.1.0] - 2026-10-08

### Fixed

- The app carries its own .NET runtime, so it starts on a computer without the .NET Desktop
  Runtime. 1.0.0 needed it installed and did not start without it.

- A `districtai://` link arriving while the app runs, as the browser's sign-in hands
  back, no longer risks closing the app. The link's details were read after the
  short-lived process that delivered it could already have exited, and the failure
  ended the app.

- When Windows announces that the computer is going to sleep or hibernate, District AI
  stops ringing on it first (ending any call under way), and rings on it again when it
  wakes. Before, a sleeping computer could stay listed to ring for up to ten minutes, so a
  caller could wait for a ring nobody heard, and after waking it could take minutes to
  ring again.

### Added

- Keyboard shortcuts in a call, from anywhere in the window: Ctrl+D turns the microphone
  off or on, and Ctrl+Shift+H hangs up. The buttons' tooltips name them.

- Each release also carries the GitHub flavour of the app as an unsigned MSIX, for the
  code-signing application. It is not meant to be installed; the README says what each
  release file is for.

## [1.0.0] - 2026-10-08

The first release of District AI for Windows, for the Microsoft Store.

### Added

- Sign in with an existing District AI account (Google, Microsoft or email) in your own
  browser, so the app never sees your password. The session is kept in Windows
  Credential Manager and resumes when the app starts. Accounts are created on the web.
- The window: Overview, Inbox, Calls, Contacts and Account, with a back button, the
  workspace switcher, the unread count, and banners for notices and live updates.
- Overview: the workspace's figures, its recent calls, and a card that opens the web
  dashboard to finish setting up.
- Inbox, to read: conversations, search, and each conversation's messages and calls.
  Opening a conversation marks it read. Replies are sent from the web dashboard or the
  District AI phone apps.
- Calls: the call log, and each call's details, AI summary, analysis and transcript.
- Contacts, to read: the list and each contact's details and AI caller profile.
- Report on AI-generated content (a call's summary, a contact's profile, a call in a
  conversation): a support request that names the item by its id, with an optional note.
  Members whose role cannot raise support requests report on the web.
- Voice calls: place a call from the dialler or from a call or contact, and answer or
  decline an incoming call from the window or from its notification, with a ringtone.
  A call rings on this computer while the app is running, in the window or in the
  notification area. "Ring on this computer" is on the Account page.
- Account: the app's version, this device's and your user id, the devices signed in to
  your account (sign out one, or all of them), deleting your account on the web, and
  signing out.
- The notification area: closing the window keeps District AI running there, and its
  menu opens the window, signs out or quits.
- Start at sign-in, off until you turn it on.
- `crates/district-ffi`: the boundary the app calls, over district-core-rust 1.2.0, with
  a projection of every screen pinned by JSON snapshots, and the call engine.
- `DistrictAI.Core`: the generated C# bindings and `CoreHost`, tested on Linux and Windows.
- `DistrictAI`: the WinUI 3 app, packaged as an MSIX, with single-instance activation and
  the `districtai` protocol.
- The audio-only libwebrtc the call engine links, built from source without the H.264
  and H.265 codecs or FFmpeg, and published as a release with its digest and build
  provenance (`.github/workflows/libwebrtc-windows.yml`).
- Releases: the Store package, a sideload test package with its certificate, the Windows
  App Certification Kit's report, SHA256SUMS and build provenance attestations.
- The repository: licence, community files and the public hygiene and store-copy checks.
