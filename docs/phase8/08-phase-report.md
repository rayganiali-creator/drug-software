# Phase 8 — Report

See `README.md` for the document index. Summary of what was built, limits and decisions follows.

## Implemented
- Patient-reported adherence (Taken / Skipped / Missed), with source and timestamps, idempotent replay, instruction-gap display, and a summary that never counts silence as missed and never gives compensatory-dose advice.
- A local, off-by-default in-process periodic worker with durable `monitoring_job` rows (lease, optimistic claim, backoff, Exhausted state, last attempt/success/failure).
- Change-aware reassessment from per-record version fingerprints, record-level reactivation, input and rule versions stored per assessment.
- Guidance delivery state machine (Scheduled → Attempted → Confirmed / Failed), a **simulated-only** channel, provenance (automated vs clinician-reviewed).
- Web rule authoring/review workspace and server-side draft editing, history and approval blockers; two-person rule enforced by the API.
- Web and Flutter patient flows; web professional adherence view.

- Browser QA (Chromium, axe, en/fa × light/dark × desktop/phone): 219 checks, 0 failures; the one defect it found (delivery badge overflow in narrow RTL) was fixed.

## Limitations
- Not clinically validated, not continuous or real-time monitoring, not a prediction.
- No real notification channel; **no emergency delivery path** exists and urgent findings must not rely on the routine worker.
- Workers run only while the API process runs; no leader election across instances.
- No clinically approved rule exists; demonstration rules cannot become active.
- Persian clinical wording is unreviewed. Flutter has no rule UI and no professional adherence page.
- Docker integration tests were not run.

## Decisions needed
Real delivery channel and emergency path; clinical reviewers for rules and Persian wording; multi-instance deployment model.

## Recommended Phase 9
Emergency/urgent escalation design, a real (approved) notification channel behind the existing adapter, clinical review workflow with real sources, and multi-instance worker coordination.
