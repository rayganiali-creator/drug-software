# 05 — Anticipatory-guidance contract

**Goal:** a calm, honest note about something worth a look. It is **not a diagnosis, not a treatment instruction and not a prediction with numbers.**
Status: contract + templates + policy + lifecycle **IMPLEMENTED**; the *producers* of messages (rules/AI deciding when to send) are **NOT BUILT** —
today messages are created from templates by the demo seeder and by tests.

## The six parts of a patient message
| # | Key | Purpose |
|---|---|---|
| 1 | `observed` | What we noticed, in the person's own data |
| 2 | `whyItMatters` | Why it matters, in one plain sentence |
| 3 | `suggestedAction` | What the person *can* do (never "stop/start/change the dose") |
| 4 | `whenToConsult` | When to talk to a doctor or pharmacist |
| 5 | `urgentSigns` | Signs needing quick help — **required** at level `Urgent`, absent otherwise |
| 6 | `basisAndConfidence` | What the note is based on and how sure we are |
A *professional* view adds `summary`, `technicalDetail`, `severityLabel`, `basis`, `confidence` and is returned only to a related professional with consent.

## Levels and lifecycle
Levels: `Information`, `FollowUp`, `ReviewSoon`, `Urgent`. Lifecycle: `Sent → Seen → Reviewed → Referred → Resolved`
(the patient may move to `Seen`/`Resolved`; `Reviewed`/`Referred` are professional steps; no step back).

## Tone policy (`PatientMessagePolicy`, enforced before a message is stored)
Rejects: scare words ("dangerous", "deadly"…), diagnosis phrases ("you have…", "this means you suffer…"), treatment orders ("stop taking", "increase the dose"),
percentages / odds, shouting (ALL CAPS other than the tokens `DEMO`/`MOCK`), clinical jargon, a missing part, and an `Urgent` message without the urgent-signs part.
fa and en are checked with separate word lists.

## Templates (`templates.json`, fa + en, reviewed text)
`data.insufficient`, `adherence.skipped_pattern`, `interaction.review`, `symptom.followup`, `symptom.severe_followup`. Parameters (`{medication}`, `{count}`…) are
substituted from the person's own records; an unknown template key is refused (`template.unknown`).

## Sample messages (English; Persian equivalents are in the same file) — DEMO
**Information — `data.insufficient`**
> *What we noticed:* We do not have enough information about your allergies to say anything reliable.
> *Why it matters:* Without enough information, any statement could be wrong, so we prefer not to guess.
> *What you can do:* If you like, add or update your information in the app.
> *When to talk to someone:* If you have questions about your allergies, you can ask your doctor or pharmacist at your next visit.
> *Basis:* There was not enough recorded information.

**FollowUp — `adherence.skipped_pattern`**
> *What we noticed:* You marked Demopril as skipped on 4 of the last 14 days.
> *Why it matters:* Taking a medicine regularly is how it is meant to work, so it is useful to notice when doses are being missed.
> *What you can do:* You could set a reminder in the app, or note down what makes the dose hard to take.
> *When to talk to someone:* Please mention it to your doctor or pharmacist at your next contact, especially if there is a reason you skip it.
> *Basis:* Your own dose log.

**ReviewSoon — `interaction.review`**
> *What we noticed:* Two of your medicines, Demopril and Nocturin, are listed together in the reference as worth a closer look.
> *Why it matters:* Some medicines can change how others work, so a professional usually checks combinations.
> *What you can do:* Keep taking your medicines as prescribed and write down any new effects you notice.
> *When to talk to someone:* Please ask your doctor or pharmacist to check this combination at your next contact, or sooner if you feel different from usual.
> *Basis:* The medication reference.

**Urgent — `symptom.severe_followup`**
> *What we noticed:* You recorded a strong symptom: dizziness.
> *Why it matters:* Strong symptoms deserve prompt attention from a professional, whatever the cause turns out to be.
> *What you can do:* Please contact your doctor or pharmacist today. If you cannot reach them, use your local urgent-care service.
> *When to talk to someone:* Contact a professional today rather than waiting for your next regular visit.
> *Signs that need quick help:* If you have trouble breathing, swelling of the face or throat, chest pain, or you faint, call your local emergency number right away.
> *Basis:* Your own symptom note.

`GET /guidance/samples?locale=fa|en` returns one DEMO sample per level (used by the "example messages" panel); samples carry a `demo: true` flag and a notice.
