# 1. Architecture

```
patient (web / Flutter)                       professional (web)
  │ record a dose (Taken/Skipped/Missed)        │ read-only adherence, status of scheduled checks, rule workspace
  ▼                                             ▼
Patients: intake log (patient-reported, source)  ── adherence summary (instruction gaps listed, never guessed)
                     │
    ┌────────────────┴───────────────────────────────────────────────┐
    │ ClinicalRules                                                   │
    │  MonitoringService (durable job row per enrolled patient)       │
    │   └─ per due job: consent check (purpose "Monitoring", per      │
    │      category) → ClinicalSafetyService.ReassessIfNeededAsync    │
    │         build snapshot + record-version fingerprint             │
    │         equal to latest & rules unchanged & not due → no-op     │
    │         else engine → assessment (+ input versions, change)     │
    │         → guidance for ACTIONABLE findings (idempotent)         │
    └───────────────────────────────┬─────────────────────────────────┘
                                    ▼
Guidance: message (+ origin, subjects fingerprint) ── delivery row (Scheduled → Attempted → Confirmed | Failed)
                                    ▼
                IGuidanceDeliveryChannel — the only one is LocalSimulatedChannel (sends nothing)
```
New/changed pieces: `BuildingBlocks.PeriodicWorker` (one bounded pass per interval, in-process, off by default) · `Patients` intake source + Missed + summary · `Guidance` delivery table, channel abstraction, runner, reactivation fingerprint · `ClinicalRules` input fingerprint/versions on assessments, `monitoring_job`, `MonitoringService`, rule draft edit/history/blockers · web (adherence, scheduled checks, delivery labels, rule workspace) · Flutter (taking, messages, safety).
Migrations: `Patients.AddIntakeSource`, `Guidance.AddGuidanceDelivery`, `ClinicalRules.Phase8Monitoring` (all additive).
No new service, no queue product, no new infrastructure, no hosting provider assumption: workers are `BackgroundService`s inside the existing API process.

## Transaction boundaries and recovery (stated, not hidden)
Each arrow above is a separate store and a separate write; nothing spans them. The order is chosen so that a crash never *advances* a state falsely:
* job: claim (state Running + lease) → check → result write. Crash between: lease expires → recovery counts a failed attempt ("interrupted"), never a success.
* assessment: store (guidance state pessimistically "Failed") → guidance → link-state update. Crash between: the retry sees equal inputs, finds the incomplete guidance, repairs it **without a second assessment**.
* delivery: "Attempted" is written **before** the channel is called. Crash between: the row says "attempted", never "confirmed"; recovery reschedules it (counted) and gives up after the attempt limit.
