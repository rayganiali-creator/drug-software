# Phase 12 — Flutter mobile completion (bounded)

> Not production-ready. The mobile app talks to the same API as the web; it holds no business rule and no secret.

## 1. Inspection (actual code)
The Flutter app already had (Phases 5–8): profile, records, medicines taken (schedule, adherence incl. patient-reported Taken/Skipped/Missed), batches, **sharing/consents**, **messages with delivery labels (simulated vs real)**, safety assessments with monitoring status, the grounded assistant, check-in, drugs, generated access catalog and localization. The one patient capability **missing** relative to web/backend was **adverse-event reporting and viewing** (Phase 9). Professional mobile screens (pharmacy safety log, ADR review) were **not** added: the product role of the mobile app is the patient; professional workflows stay on web where review, notes and classification are designed and tested.

## 2. Implemented
* `RecordsClient`: `adverseEvents`, `adverseEvent`, `createAdverseEvent`, `addAdverseEventFollowUp`, `newRequestId` (idempotency key per form).
* `AdverseEventsScreen` (list + new report) and `AdverseEventDetailScreen` (original as written, what is not known, history visible to the patient, follow-up), reached from Profile → "Adverse events" (`/profile/adverse-events[/:id]`).
* Shows the "signal, not proof" and "internal record, not submitted anywhere" notices, "not an emergency service / sends no urgent alerts" (no emergency criteria invented), the unreviewed-Persian banner, `Not submitted anywhere`; dates left empty are sent as `null`; medicines are names as given (no matching); the category and professional notes are never sent to a patient by the server so nothing can display them.
* States: loading, empty, error with retry, not-connected, unauthorized (via the existing `RecordsLoader`/`RecordsErrorPanel`), network failure text; Persian RTL and English LTR.
* Strings come from the existing generated `adr.*` keys (single source `design/i18n/strings.mjs`); nothing generated was edited by hand.

## 3. Deferred / not done (documented)
* **Patient/batch/product picker.** On mobile the patient already picks from **their own** medicines and batches (existing screens); the cross-patient picker is a professional/pharmacy need and was addressed on web in Phase 13 only where an authorized endpoint exists. No unauthorized search endpoint was invented.
* Own-medicine linking inside the ADR form (names only on mobile for now; the server accepts links to the patient's own records).
* Professional mobile screens; Flutter notification inbox beyond the existing Messages screen (delivery labels there already distinguish simulated from real).
* Offline queue and stale-data banners: Phase 14.

## 4. Tests
`test/phase12_test.dart` — 6 tests: list with notices and no category; empty state; failed load → retry; form sends nulls, request id and is disabled until something is written; detail shows original / missing / history; Persian banner and no raw keys. One existing test (`app_test.dart` language switch) needed a taller test surface because the Profile list gained one tile (layout, not behaviour).
Exact suite results are in the repository-wide review. `flutter analyze`: clean.

## 5. Platform builds
* **Linux desktop debug build: run, succeeded** (`flutter build linux --debug`; clang, cmake, ninja were already installed).
* **Not run:** Android and iOS/macOS/Windows builds (SDKs/tooling not installed; none was installed for this phase) — **platform support for them is therefore not claimed from this phase**.

## 6. Dependencies for later phases
Phase 14 adds the offline queue and freshness banners on top of these screens.
