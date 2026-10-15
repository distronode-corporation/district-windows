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

## What's new in this version: 3.0.0

Partner Center, Store listings, "What's new in this version" (1500 characters), one text
per listing language: en-US below (567 characters) and, in the fr-CA listing, the French
below (713 characters). Paste each block as it is: the French holds no-break spaces
(U+00A0) before each colon and inside the guillemets.

The French is the programme's reviewed translation (monorepo
`i18n/fr-CA/work/S01/final.jsonl`, id `windows.msstore.whats_new_3_0_0`), with one line
left out in both languages: the draft said that calls ring and messages arrive while the
app is closed. That is not confirmed (Microsoft's mapping of this package for Windows push
is not confirmed, and the push credentials are not verified in production), so these
notes do not claim it.

en-US:

```text
District AI is now in Canadian French.
- New message: start a conversation from the Inbox, by text message or email.
- Phone numbers: reapply a number's call routing, release a number and run a paid number lookup, each asked first. A new Carrier tab shows the carrier accounts and SIP trunks, turns one-time passcodes on or off, and files the A2P 10DLC and toll-free registrations. Buying a number stays on the web.
- A call's details word two values as the web portal does: a negative sentiment reads "Friction", and a transfer that went through reads "Transferred".
```

fr-CA:

```text
District AI est maintenant en français canadien.
- Nouveau message : lancez une conversation depuis la boîte de réception, par texto ou par courriel.
- Numéros de téléphone : rétablissez l'acheminement d'un numéro, libérez un numéro et lancez une recherche payante sur un numéro, chaque fois après confirmation. Le nouvel onglet Transporteur affiche les comptes de transporteur et les raccordements SIP, active ou désactive les codes à usage unique, et dépose les inscriptions A2P 10DLC et de numéros sans frais. L'achat d'un numéro se fait toujours sur le Web.
- Le détail d'un appel présente deux valeurs comme le portail Web : un sentiment négatif s'affiche « Friction », et un transfert réussi, « Transféré ».
```
