# Microsoft Store listing: in-app purchase disclosure and release notes

A draft for Partner Center, for District AI 2.0 for Windows, written to be pasted. It
covers what changes from 1.0's listing (a client for an existing account): from 2.0 a
person can create an account from the app and buy a District AI plan in it.

What is true of the app, and what the text below says:

- **Accounts.** The welcome screen offers "Create an account". It opens the District AI
  sign-in page in the person's own browser, where they create the account (Google,
  Microsoft, or an email address and a password) and come back to the app signed in. The
  app never sees the password.
- **Purchases.** District AI plans are subscriptions. They are sold by District AI
  through its own checkout, with Stripe as the payment processor, not through the
  Microsoft Store's commerce. The checkout names Stripe at the step where the person
  confirms the purchase, before anything is charged.
- **What is sold.** Subscriptions to District AI plans, billed on a recurring basis, and
  the usage they meter. Nothing is sold as a one-off digital item, and nothing in the app
  is a game.
- **Where it is managed.** A subscription is changed or cancelled with District AI (the
  app's billing screen, once K1 lands, or the District AI website), never through
  Microsoft.

Before pasting, check each point against the build being submitted (the billing and
checkout packets, K1 and K2) and against the current text of Microsoft Store Policy 10.8
(financial transactions) in Partner Center, which is the authority; this draft follows
its requirement that an app using its own payment processing for digital items says so,
identifies the processor at the transaction, and keeps purchases under the developer's
own terms.

## Partner Center fields

**Properties, "This product has in-app purchases":** yes.

**Pricing and availability, base price:** Free (the app is free to install; plans are
bought in the app through District AI's checkout).

## Description: the paragraph to add

> District AI is free to install. To use it you need a District AI account: sign in with
> one you have, or create one from the welcome screen, which opens the District AI
> sign-in page in your browser. District AI plans are subscriptions sold by District AI,
> not by Microsoft, through District AI's own secure checkout, with payments processed by
> Stripe. The checkout shows the plan, its price and how often it is billed, and names
> Stripe, before you confirm. You can change or cancel your plan at any time from the
> app's billing screen or on the District AI website.

## Notes for certification (Partner Center "Notes for certification")

> District AI for Windows is a client for the District AI service. It sells District AI
> subscriptions (not games, and no one-off digital items) through the developer's own
> checkout, with Stripe as the payment processor, as Store Policy 10.8 permits for apps
> other than games; the processor is named on the confirmation step of the checkout.
> Account creation happens in the browser, on the District AI sign-in page.
> [Test account: the reviewer account's email address, and where its password is kept,
> added at submission. Never put the password in this file.]

## Short in-app purchase line, if a field asks for one

> Subscriptions to District AI plans, sold by District AI through its own checkout
> (payments by Stripe).

## What's new in this version: 3.2.0

Partner Center, Store listings, "What's new in this version" (1500 characters), one text
per listing language: en-US below (776 characters) and, in the fr-CA listing, the French
below (1012 characters). Paste each block as it is: the French holds no-break spaces
(U+00A0) before each colon and inside the guillemets.

The notes carry every user-visible entry of the CHANGELOG's 3.2.0 section, District HQ by
voice first; "Built on district-core-rust 7.0.0" is left out, as it changes nothing on its
own. The French names on screen (« Démarrer le mode vocal », « Arrêter le mode vocal »,
« je confirme ») are the core's reviewed strings; the rest of the French is new for 3.2.0
and waits for the manager's review. 3.1.0 was the last published submission, so each text
below goes in alone.

en-US:

```text
District AI 3.2.0:
- District HQ by voice: press Start voice on District HQ and talk to the assistant. It first says where your voice is processed and starts nothing until you accept. Both sides' words join the conversation as they are spoken, and a change you ask for is a card, made only when you say "confirm" or click Confirm.
- Voice dock: the session goes on when you leave District HQ, with what the assistant is doing, the microphone, Stop voice and every change waiting under the call strip on every screen. Closing the window to the tray ends the session; minimising keeps it.
- District HQ: Try again after a question that failed sends your last typed question.
- Voice: the time to first word of an unsaved edit counts the assistant's measured thinking time again.
```

fr-CA:

```text
District AI 3.2.0 :
- District HQ par la voix : appuyez sur « Démarrer le mode vocal » dans District HQ et parlez à l'assistant. Il indique d'abord où votre voix est traitée et ne démarre rien avant que vous acceptiez. Les paroles des deux côtés s'ajoutent à la conversation au fil de l'échange, et un changement que vous demandez devient une carte, appliqué seulement quand vous dites « je confirme » ou cliquez sur Confirmer.
- Mode vocal sur les autres écrans : la session continue quand vous quittez District HQ. Ce que fait l'assistant, le microphone, « Arrêter le mode vocal » et chaque changement en attente restent affichés sous la barre d'appel, sur chaque écran. Fermer la fenêtre dans la zone de notification met fin à la session; la réduire la conserve.
- District HQ : « Réessayer » après une question qui a échoué envoie votre dernière question tapée.
- Voix : pour une modification non enregistrée, le délai avant le premier mot tient de nouveau compte du temps de réflexion mesuré de l'assistant.
```

## What's new in this version: 3.1.0

Partner Center, Store listings, "What's new in this version" (1500 characters), one text
per listing language: en-US below (1064 characters) and, in the fr-CA listing, the French
below (1489 characters). Paste each block as it is: the French holds no-break spaces
(U+00A0) before each colon and inside the guillemets, and its times keep a plain space
(« 9 h 30 », U+0020), as the app writes them.

The notes carry every user-visible entry of the CHANGELOG's 3.1.0 section, crash reports
first. The Security entry (webpki-root-certs 1.0.10) is left out: no build the app ships
compiles that crate. The crash-report line is the copy reviewed 2026-10-25, with the
default and what a report leaves out added from the same reviewed copy. The rest of the
French is new for 3.1.0, reviewed by the manager 2026-10-25. 3.0.3 was the last published
submission, so each text below goes in alone.

en-US:

```text
District AI 3.1.0:
- Crash reports: off until you allow them. The app asks once, with Don't send as the default, and you can change it on the Account screen. A report never holds your computer's name, your user name or your IP address.
- Calls: declining a call now tells the service, so once everyone it rang has declined, your receptionist takes a message right away.
- Calls: a call's outcome now reads "Callback requested", "Passed to a person" or "Information only", instead of "Callback", "Escalated" or "Info".
- Desk: the help desk is now called the Desk in the navigation pane, on its page and in its settings.
- Booking pages: your working hours read "9 h" or "9 h 30" in French, accept 9:30 as well as 09:30, and refuse a time past 23:59 before you save.
- Phone numbers: where numbers are bought is explained once, above the tabs, and the link to the web marketplace says it opens in your browser.
- Notifications: clicking a call or message notification left in Action Center after you quit District AI now opens the conversation or call it was about.
```

fr-CA:

```text
District AI 3.1.0 :
- Rapports de plantage : désactivés tant que vous ne les avez pas autorisés. L'application vous pose la question une fois, « Ne pas envoyer » est le choix par défaut, et vous pouvez changer ce choix à l'écran Compte. Un rapport ne contient ni le nom de votre ordinateur, ni votre nom d'utilisateur, ni votre adresse IP.
- Appels : quand vous refusez un appel, le service en est maintenant avisé. Dès que tous les membres sollicités l'ont refusé, votre réceptionniste prend un message sans attendre.
- Appels : le résultat d'un appel s'affiche maintenant « Rappel demandé », « Transmis à une personne » ou « Information seulement », au lieu de « À rappeler », « Transmis à l'équipe » ou « Information ».
- Comptoir : la section s'appelle maintenant « Comptoir » dans le volet de navigation, sur sa page et dans ses paramètres, au lieu de « Comptoir clients ».
- Pages de réservation : vos heures de disponibilité s'affichent maintenant « 9 h » ou « 9 h 30 », vous pouvez taper 9:30 aussi bien que 09:30, et une heure après 23 h 59 est refusée avant d'être enregistrée.
- Numéros de téléphone : l'endroit où acheter des numéros n'est plus expliqué qu'une fois, au-dessus des onglets, et le lien vers la place de marché Web indique qu'il s'ouvre dans votre navigateur.
- Notifications : un clic sur une notification d'appel ou de message restée dans le centre de notifications après que vous avez quitté District AI ouvre maintenant la conversation ou l'appel en question.
```

## What's new in this version: 3.0.3

Partner Center, Store listings, "What's new in this version" (1500 characters), one text
per listing language: en-US below (418 characters) and, in the fr-CA listing, the French
below (541 characters). Paste each block as it is: the French holds no-break spaces
(U+00A0) before each colon and inside the guillemets.

The notes carry every user-visible entry of the CHANGELOG's 3.0.3 section, the security
update first. The French is new for 3.0.3, reviewed by the manager 2026-10-23. 3.0.2 was
the last published submission, so each text below went in alone, as written here
(submission 1152921505702209071).

en-US:

```text
District AI 3.0.3:
- Security update: secure connections no longer trust a certificate chain that passes through one of the distrusted emSign certificate authorities, whose private keys are now public (rustls-webpki 0.103.16, RUSTSEC-2026-0382). The app and its shared core both carry the fix.
- Inbox: a voicemail is now labelled "Voicemail" in a conversation's channels and on its message, instead of "Text message".
```

fr-CA:

```text
District AI 3.0.3 :
- Mise à jour de sécurité : les connexions sécurisées ne font plus confiance à une chaîne de certificats qui passe par l'une des autorités de certification emSign retirées de la liste de confiance, dont les clés privées sont maintenant publiques (rustls-webpki 0.103.16, RUSTSEC-2026-0382). L'application et son noyau partagé intègrent tous deux le correctif.
- Boîte de réception : un message vocal porte maintenant la mention « Boîte vocale » dans les canaux d'une conversation et sur son message, au lieu de « Texto ».
```

## What's new in this version: 3.0.2

Partner Center, Store listings, "What's new in this version" (1500 characters), one text
per listing language: en-US below (1092 characters) and, in the fr-CA listing, the French
below (1412 characters). Paste each block as it is: the French holds no-break spaces
(U+00A0) before each colon, inside the guillemets and inside the call length.

The French is new for 3.0.2, reviewed by the manager 2026-10-16. The notes carry every
user-visible entry of the CHANGELOG's 3.0.2 section. If 3.0.1 never reached the Store,
the 3.0.1 text below followed by these lines without their first one would be 1835
and 2351 characters, over the limit: that case needs a shorter text of its own.

en-US:

```text
District AI 3.0.2:
- Workflows: in French, a run's skipped or failed action now gives its reason in French.
- In French, a call's length such as "1 min 5 s" no longer breaks across two lines.
- Contacts: when another contact already has the phone number or email address, the message now says so in French too, and no longer offers to try again.
- Inbox: the page is now headed "Inbox", like its entry in the navigation pane, instead of "Conversations".
- Help desk: it has its own icon in the navigation pane (a headset), and a ticket's row in the queue updates as soon as you reply or change its status.
- Call handling: the ring length slider shows its value, such as "20 seconds", as you move it.
- Booking pages: a form's error goes away as soon as you change the value, a webhook's new signing secret has its own warning, and in Team, someone who left with bookings still assigned is shown as a warning.
- In high contrast, messages sent from your workspace keep an outline, as received ones do.
- Persona: in French, both messages shown when you try your receptionist now say "aperçu".
```

fr-CA:

```text
District AI 3.0.2 :
- Flux de travail : dans une exécution, une action ignorée ou en échec indique maintenant sa raison en français, par exemple « ignoré (aucun destinataire) ».
- La durée d'un appel, comme « 1 min 5 s », ne se coupe plus sur deux lignes.
- Contacts : lorsqu'un autre contact a déjà ce numéro de téléphone ou cette adresse courriel, le message le dit maintenant en français et ne propose plus de réessayer.
- Boîte de réception : la page s'intitule maintenant « Boîte de réception », comme dans le volet de navigation, au lieu de « Conversations ».
- Comptoir clients : la section a sa propre icône dans le volet de navigation (un casque d'écoute), et la ligne d'un billet dans la file d'attente se met à jour dès que vous y répondez ou changez son état.
- Gestion des appels : le curseur de durée de sonnerie affiche sa valeur, par exemple « 20 secondes », pendant que vous le déplacez.
- Pages de réservation : l'erreur d'un formulaire disparaît dès que vous modifiez la valeur, le nouveau secret de signature d'un webhook a son propre avertissement, et dans Équipe, une personne partie à qui des rendez-vous sont encore attribués s'affiche comme un avertissement.
- En contraste élevé, les messages envoyés par votre espace de travail gardent un contour, comme les messages reçus.
- Persona : les deux messages affichés quand vous appelez votre réceptionniste parlent maintenant d'« aperçu ».
```

## What's new in this version: 3.0.1

Partner Center, Store listings, "What's new in this version" (1500 characters), one text
per listing language: en-US below (761 characters) and, in the fr-CA listing, the French
below (958 characters). Paste each block as it is: the French holds no-break spaces
(U+00A0) before each colon and inside the guillemets.

The French is new for 3.0.1, reviewed by the manager 2026-10-15 and 2026-10-16. The sideload
dependencies fix (the x86 Windows App Runtime) is left out: it concerns the GitHub
test build, not the Store copy. If 3.0.0 never reached the Store, the 3.0.0 text below
followed by these lines without their first one would be 1516 and 1934 characters,
over the limit: that case needs a shorter text of its own.

en-US:

```text
District AI 3.0.1:
- A call ringing on this computer stops as soon as it is answered on another device, the caller hangs up or the ring times out, and its notification goes away.
- Call routing rules: the caller details and comparisons show the web portal's labels, in English and in French.
- Phone numbers: monthly prices show two decimals and their currency.
- Screens fit large text and narrow windows better, and support requests show their status in your language.
- Booking pages: when your working hours, an event type's hosts or a webhook's deliveries can't be loaded, Try again loads them again.
- Analytics: the caller sentiment bands keep their colours in French.
- The app no longer closes unexpectedly while you edit your working hours exceptions.
```

fr-CA:

```text
District AI 3.0.1 :
- Un appel qui sonne sur cet ordinateur s'arrête dès qu'il est pris sur un autre appareil, que l'appelant raccroche ou que la sonnerie expire, et sa notification disparaît.
- Règles d'acheminement : les champs du contact et les comparaisons affichent les libellés du portail Web, en français comme en anglais.
- Numéros de téléphone : les prix mensuels s'affichent avec deux décimales et leur devise.
- Les écrans s'adaptent mieux au texte agrandi et aux fenêtres étroites, et les demandes de soutien affichent leur état dans votre langue.
- Pages de réservation : lorsque vos heures de disponibilité, les hôtes d'un type de rendez-vous ou les envois d'un webhook ne se chargent pas, « Réessayer » les charge de nouveau.
- Analytique : les barres du sentiment de l'appelant conservent leurs couleurs en français.
- L'application ne se ferme plus de façon inattendue pendant que vous modifiez les exceptions de vos heures de disponibilité.
```

## What's new in this version: 3.0.0

Partner Center, Store listings, "What's new in this version" (1500 characters), one text
per listing language: en-US below (773 characters) and, in the fr-CA listing, the French
below (995 characters). Paste each block as it is: the French holds no-break spaces
(U+00A0) before each colon and inside the guillemets.

The French is the programme's reviewed translation (monorepo
`i18n/fr-CA/work/S01/final.jsonl`, id `windows.msstore.whats_new_3_0_0`), with one line
left out in both languages: the draft said that calls ring and messages arrive while the
app is closed. That is not confirmed (Microsoft's mapping of this package for Windows push
is not confirmed, and the push credentials are not verified in production), so these
notes do not claim it. The booking pages line was added after that review, for the
scheduling administration that 3.0.0 makes native, and reviewed by the manager 2026-10-15.

en-US:

```text
District AI is now in Canadian French.
- Booking pages: every section is now in the app: Overview, Event types, Working hours, Bookings, Calendar, Team, Settings and Developer. Connecting a Google or Microsoft calendar finishes in your browser.
- New message: start a conversation from the Inbox, by text message or email.
- Phone numbers: reapply a number's call routing, release a number and run a paid number lookup, each asked first. A new Carrier tab shows the carrier accounts and SIP trunks, turns one-time passcodes on or off, and files the A2P 10DLC and toll-free registrations. Buying a number stays on the web.
- A call's details word two values as the web portal does: a negative sentiment reads "Friction", and a transfer that went through reads "Transferred".
```

fr-CA:

```text
District AI est maintenant en français canadien.
- Pages de réservation : chaque section est maintenant dans l'application (Vue d'ensemble, Types de rendez-vous, Heures de disponibilité, Rendez-vous, Calendrier, Équipe, Paramètres et Développeur). La connexion d'un calendrier Google ou Microsoft se termine dans votre navigateur.
- Nouveau message : lancez une conversation depuis la boîte de réception, par texto ou par courriel.
- Numéros de téléphone : rétablissez l'acheminement d'un numéro, libérez un numéro et lancez une recherche payante sur un numéro, chaque fois après confirmation. Le nouvel onglet Transporteur affiche les comptes de transporteur et les raccordements SIP, active ou désactive les codes à usage unique, et dépose les inscriptions A2P 10DLC et de numéros sans frais. L'achat d'un numéro se fait toujours sur le Web.
- Le détail d'un appel présente deux valeurs comme le portail Web : un sentiment négatif s'affiche « Friction », et un transfert réussi, « Transféré ».
```
