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

## What's new in this version: 3.0.2

Partner Center, Store listings, "What's new in this version" (1500 characters), one text
per listing language: en-US below (1092 characters) and, in the fr-CA listing, the French
below (1412 characters). Paste each block as it is: the French holds no-break spaces
(U+00A0) before each colon, inside the guillemets and inside the call length.

The French is new for 3.0.2 and is a draft, not yet reviewed by the manager: it is not
to be pasted until this paragraph records that review. The notes carry every
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
