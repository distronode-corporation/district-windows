# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Crash reports, off until you allow them: the app asks once, with Don't send as the
  default, and Send crash reports on the Account screen changes it. A report holds the
  error, where it happened and the versions, never your computer's name, your user name
  or your IP address.

### Changed

- Declining a call tells the service. Until now Decline only stopped the ring here, so
  the caller waited out the whole ring; now, once every member it rang has declined,
  the receptionist takes a message at once. Only a press of Decline (in the window or
  on the call's notification) is sent: a ring that runs out, one the service withdraws,
  and one dropped because the computer sleeps, the app quits or you sign out still send
  nothing. A ring every member declined, on this computer or another device, stops and
  goes away with no missed call. This comes with district-core-rust 6.0.0.
- A call's outcome reads "Callback requested" (« Rappel demandé »), "Passed to a person"
  (« Transmis à une personne ») and "Information only" (« Information seulement »), where
  it read "Callback", "Escalated" and "Info".
- The shared core is district-core-rust 6.1.0. In English the Desk is called the Desk
  ("The Desk is off", "Turn on the Desk", "Could not load the Desk"). The working hours
  editor shows French times as « 9 h » and « 9 h 30 », reads `9:30` or `9 h 30` as well
  as `09:30`, still saves HH:MM, and refuses a time past 23:59 before the save. The link
  to the web marketplace is captioned "Opens in your browser."; the note above the tabs
  is the one place that says where numbers are bought.

### Fixed

- Clicking a call or message notification left in Action Center after District AI was
  quit (or the computer restarted) opens the conversation or call it was about, where it
  only showed the window. The notification now carries what it opens (ids only). Its
  Answer and Decline buttons still do nothing then, since that call stopped ringing when
  the app quit.

## [3.0.3] - 2026-10-22

### Security

- A security update for secure (TLS) connections: rustls-webpki 0.103.16 in Cargo.lock,
  for RUSTSEC-2026-0382 (CVE-2026-111943). 0.103.15 could accept a certificate chain
  through one of the distrusted emSign intermediates, whose private keys are now public.
- The shared core is district-core-rust 5.0.1, the core's security release for the same
  advisory (its own Cargo.lock takes rustls-webpki 0.103.16, as this app's does); it
  changes nothing the app calls or shows.

### Fixed

- Inbox: a voicemail is called "Voicemail" (« Boîte vocale ») in a conversation's
  channels and on its message in the thread, where it read "Text message" (« Texto »).

## [3.0.2] - 2026-10-16

### Fixed

- In French, a workflow run's skipped or failed action says why in French: the
  service's reason opens with a code (`no-recipient: …`), and the line shows that code's
  label (« ignoré (aucun destinataire) », « HTTP 503 » for a webhook's error); a reason
  with no label leaves the parenthesis out, where the English sentence used to show.
  Each action reads « Envoyer un texto : OK », with French typography's space before the
  colon and OK in capitals. English is unchanged.
- In high contrast, a message this workspace sent keeps an edge, as received ones do.
- French: the voice audition is called « aperçu » in both its sentences (« L'aperçu est
  terminé. »).
- The shared core is district-core-rust 5.0.0, which keeps a French call length whole
  (« 1 min 5 s », every space no-break), gives a webhook's new signing secret its own
  warning, says in French that another contact already holds a number or address (the
  service's 409) and offers no retry for it, keeps a ticket's row in the queue in step
  with a reply or a status change, and clears a booking form's complaint as soon as
  the value is changed.

### Changed

- The Inbox page is headed "Inbox" (« Boîte de réception »), as its entry in the pane
  is, instead of "Conversations".
- Help desk has its own icon in the pane, a headset, instead of a question mark like
  Support's.
- The ring length slider in Call handling shows its value ("20 seconds") under it as it
  moves.
- Booking pages, Team: someone who left with bookings still assigned is a warning
  InfoBar instead of a card with red text.

## [3.0.1] - 2026-10-15

### Fixed

- A call ringing on this computer stops as soon as the service says its ring ended,
  instead of ringing on for up to thirty seconds. Answered on another device (your
  other computer or phone, or a colleague's), the banner reads "Answered on another
  device" and "That call was answered on another device."; a caller who hangs up
  ends it as before; a ring the service gave up on is a missed call, as at the
  deadline. The ringtone stops, Answer and Decline go, and the call's notification
  is taken away. The word comes over the live connection and, in the Microsoft Store
  build, as a Windows push (`call_ring_cancelled`), so it also stops a ring a push
  started, and a pushed call whose ring has ended before the app is ready never
  rings. A call answered on this computer is never touched, and the local
  thirty-second deadline stays as the fallback.
- Call routing rules: the caller detail and comparison pickers, and the line under
  each rule, show the web portal's labels in English and Canadian French ("Caller
  type (business/consumer)", "Valeur estimée", "Égal à"), not the stored key
  reworded in English. A detail or comparison the builder does not offer still shows
  as stored.
- The sideload test build's `sideload-dependencies.zip` carries the x86 Windows App
  Runtime beside the x64 one, of the same version. Without it a fresh Windows Server
  2025 refused to install the test build (0x80073CF9).

- Phone numbers: a number's monthly price is written as every other price in the app,
  with two decimals and its currency (`$2.50 a month`, `2,50 $ par mois`, `1,15 $ US
  par mois`), where it read `2.5 a month` with no currency.
- The dialler's example number no longer breaks after `+1`.
- Booking pages, Working hours: the app no longer closes now and then while the date
  overrides' editor is open (seen when it opened again, after a delete and after Add
  override, in any theme and language, and in 3.0.0 too). Its list followed the
  overrides as they changed, inside the redraw, and could fail there while the dialog
  was opening or laying out; it now shows a copy of them, set once the dialog has
  drawn, and a closed editor's list follows nothing.
- Booking pages: when the working hours' editors, an event type's hosts dialog or a
  webhook's deliveries cannot be read, the failure now offers Try again, which reads
  them again (the people who could be added as hosts too, while the hosts stay
  editable). It was only shown before, and the dialog had to be closed.
- Support: a request's status, in the list and on the request, is shown in the app's
  language ("In progress", « En cours »), not in the support desk's own word.
- Analytics: the caller sentiment bands keep their colours and patterns in French; a
  band's tone now follows the service's name for it, never its translated label.
- Screens fit large text and narrow windows better: cards take fewer, wider columns
  as the text grows, a day and its buttons share a line while they fit, a dialog's
  button labels wrap instead of being cut, and tab strips show their tab again when
  their page returns.
- Call routing rules: the keys a rule keeps beyond the builder's are named exactly as
  stored (`action, match, target`), not reworded in English.

### Changed

- Checkout and Manage billing open a page only through the service's browser-bound
  hand-off (security sweep S33). The checkout window opens the service's start page,
  hands its `districtai://handoff` answer to the app, and only then asks for the
  checkout or billing link, bound to that answer's nonce; when the app has to use your
  browser instead, the answer comes back through the `districtai:` link. When no
  answer arrives within thirty seconds, Billing now says "Your browser didn't respond to
  District AI in time, so the page wasn't opened. Try again, and let your browser
  open District AI if it asks." instead of opening a link bound to no browser.
- The shared core is district-core-rust 4.0.2, which reads the service's
  `call_ring_cancelled`, binds every hand-off link to a browser, writes the
  dialler's hint with no-break spaces, labels support statuses in both languages,
  names each sentiment band, and offers Try again for the booking pages' dialog reads.
- An exception that would end the app is first written to its trace, with its type,
  HResult, message, stack and inner exceptions (a run started with a scripted scene
  keeps the trace in a file), and the UI tests read a crash's dump for it.

## [3.0.0] - 2026-10-15

### Added

- District AI speaks Canadian French (fr-CA): the app's screens, dialogs and
  notifications, and the words the shared core writes. A computer whose first English or
  French Windows display language is French, in any region (fr-CA, fr-FR, fr-BE, fr-CH),
  gets the app in Canadian French; any other gets English. Dates, times, call lengths,
  prices and numbers read the French way (`15 août 2026`, `1 min 5 s`, `249,00 $ US`).
  Text the service writes, such as AI summaries, stays as it was written. The package
  declares fr-CA, with its description in French.
- Start a new conversation from the Inbox: "New message" picks a recent contact or takes
  a number or address, chooses text message or email (with a subject), and opens the
  conversation once it is sent.
- Manage your workspace's phone numbers: reapply a number's call routing, release a
  number (asked first), and run a paid number lookup (asked first). A new Carrier tab
  shows the carrier accounts, the SIP trunks (and creates one), turns one-time passcodes
  on or off, and files the A2P 10DLC and toll-free registrations. Buying a number stays on
  the web.
- The Microsoft Store build registers for Windows push notifications, so that calls can
  ring and messages arrive while the app is closed. Delivery to a closed app is not
  confirmed yet. The GitHub build rings only while it runs.
- Run your booking pages from the app: every section of the scheduler's administration
  is native, under Booking pages, as in District AI for iPhone.
  - Overview: the booking desk at a glance and what is coming up.
  - Event types: create one, edit its details, turn it on or off or archive it, send
    test emails, choose its hosts and how bookings are routed among them, and edit its
    booking questions. A delete is asked first.
  - Working hours: the weekly hours and the date overrides (a day off or custom hours).
  - Bookings: upcoming, past and cancelled, a page at a time; cancel one with a reason,
    move it to a free time, or change its host, each confirmed in its own dialog.
  - Calendar: the connected calendars, which are checked for conflicts and which one
    receives new bookings, connecting a CalDAV calendar, and disconnecting one (asked
    first). Connecting Google or Microsoft finishes in your browser.
  - Team: the members and where they stand with the scheduler, the teams, their names
    and hosts, and archiving an account once nothing stands in the way. Removals and
    deletes are asked first.
  - Settings: the booking page (business name, logo and banner, links, language), the
    booking assistant, your profile and picture, and your email notifications. Images
    save as soon as they are chosen; removing one is asked first.
  - Developer: API keys (each shown once), connected apps, and webhooks with their
    deliveries and signing secret (shown once). Revoking and deleting are asked first.

### Changed

- The shared core is district-core-rust 4.0.1, which keeps every word it shows in a
  catalog, in English and Canadian French. Every request names the app's language
  (`Accept-Language: fr-CA` or `en`), so what the service writes in an answer is in it,
  and after each sign-in and at start-up the account is told the app's language.
- A carrier account's balance reads as money (`Balance $12.50`, was `Balance 12.50 USD`),
  and a SIP trunk's status by its label (`Active`, was `active`).
- A booking webhook offers the four booking events the web portal offers (created,
  cancelled, rescheduled, reminder). The recording, transcript and notes events are no
  longer offered, since no meeting is recorded; a webhook that already sends one keeps it
  when saved. A delivery whose status this version does not know reads "Other", not the
  stored word.
- Reporting AI-generated content goes through the shared core, so the Windows and Linux
  apps send the same report. The dialog and what the support desk receives are unchanged.
- A call's details word two values as the web portal does: a negative sentiment reads
  "Friction", and a transfer that went through reads "Transferred".
- Booking pages no longer offer "Manage on the web": everything they manage is in the
  app now.

### Fixed

- Buttons inside some lists did nothing when pressed: the call-handling modes, a
  messaging account's Make default, Edit and Remove, a channel's sender, a workflow's
  runs, its on and off switch and "More runs", and Voice Studio's "Use default".

## [2.0.1] - 2026-10-13

### Changed

- The shared core is district-core-rust 3.1.1.

### Fixed

- The Billing page names your plan ("Voice Pro") instead of showing its internal key
  ("VoicePro").

## [2.0.0] - 2026-10-12

### Added

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
  and whole transcript, with Report on the minutes and on the action items. A viewer
  joins to listen and gets no guest link. The audio is not recorded; the transcript and
  the minutes are kept with the meeting.
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
  first); adding someone sends no invitation; they get access when they sign in with that
  address. The service refuses a change that would leave no agency member. Agency and
  client members can rename the workspace. Not offered to a viewer.
- Account: "Purchases on this computer", "Sign in every time" (the default) or "Off",
  which hides every purchase action.
- Create an account from the welcome screen: the browser opens the District AI sign-in
  page, which offers to create one, and the new account comes back to the app signed in.
  While it has no workspace, the overview offers the plans, as Billing does: choosing
  one opens checkout, and once the checkout window closes the app looks for the new
  workspace for about half a minute, offering no plans meanwhile so nobody pays twice,
  and opens it without a restart. A workspace that takes longer appears when the
  overview is opened again.
- With both the Microsoft Store copy and the GitHub copy installed, the sign-in page
  holds browser sign-in and account creation and explains why (the browser could hand
  the sign-in back to the other copy), and the window title names which copy this is.
- A new message gives a notification, and pressing it opens the conversation.
- The District icon in the title bar.

### Changed

- The shared core is district-core-rust 3.1.0: 3.1.0 sets up a new account's first
  workspace through checkout, 3.0.0 adds buying in the app (on only in this app), and
  2.0.0 brings the live transcript of a call, the app's platform at sign-in (so the
  sign-in page can offer to create an account), and saving a reply still waiting to be
  saved when the app quits.

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
