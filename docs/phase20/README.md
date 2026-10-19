# Phase 20 — Delivery and monitoring operability and fairness

Local, synthetic data only; nothing leaves the process. This phase changes how the two background passes report and how delivery rows are scheduled. It adds **no** migration, **no** endpoint, **no** real channel and **no** consent rule, and it makes **no** claim of clinical validation, anonymity, regulatory compliance or production readiness. Delivery is still **at-least-once** and the only channel is still simulated.

## What changed

### A. Counts-only summary logs (both workers)
One structured line per pass, from `GuidanceDeliveryService.RunDueAsync` and `MonitoringService.RunOnceAsync` (source-generated `LoggerMessage`, constructor parameter `ILogger<…>? logger = null`, so tests and callers that do not pass one still work).

| Pass | Fields | Event ids |
|---|---|---|
| Delivery | `outcome`, `considered`, `confirmed`, `retrying`, `failed`, `skipped`, `deferred`, `recovered`, `unrecorded`, `errors` | 7101 summary, 7102 failed part-way, 7103 row left unchanged |
| Monitoring | `outcome`, `recovered`, `claimed`, `reassessed`, `unchanged`, `skipped`, `failed`, `exhausted`, `discarded` | 7111 summary, 7112 failed part-way |

`outcome` is `completed` (Information) only when there were no errors, no unrecorded outcomes (delivery) and no failed or discarded jobs (monitoring); otherwise `completed_with_errors` (Warning). A pass that stops part-way logs an Error line with the counts reached so far and the **exception type name only**; a cancelled pass logs `cancelled` (Information) with the counts reached so far. It never logs "completed" for a pass that did not complete. Monitoring tallies each job as it finishes, so a job whose processing threw is counted as failed instead of being lost when the pass stops.

**Privacy.** Lines contain integers and fixed words only: no patient, message, delivery or job identifiers, no message text, no exception messages, no clinical data. Tests assert that no GUID-like or long hex text appears and that an exception's message never reaches the log. Because no id is logged, a stuck row is found in the database (oldest `Scheduled` row with a past `next_attempt_at`), not in the log. No operator endpoint and no heartbeat were added. `DeliveryRunResult` gained optional `Errors` and `Deferred`; `MonitoringRunResult` is unchanged since Phase 19.

### B. A poisoned row does not stop the pass
Before the channel is called a delivery row is written for: the claim (recording the attempt), ending it (no channel / no message / resolved / patient missing) and deferring it (inactive patient). If the database **rejects** such a write (a `DbUpdateException` that is not an optimistic-concurrency conflict), or the patient lookup throws, the row is left exactly as it was (still `Scheduled`, not delivered, not failed, no audit), `Errors` is incremented, a Warning line `Guidance delivery row left unchanged and retryable: <phase> … (<exception type>)` is logged, and the pass continues with the next row. A row is examined once per pass, so there is no tight retry loop; the row is tried again at the next pass. A concurrency conflict stays quiet (the row belongs to another instance) and is **not** an error. The write of the channel's outcome is unchanged from Phase 19 (any `DbUpdateException` there is `Unrecorded`, the row stays `Attempted`, nothing is audited as delivered). Failures of audit writes and database outages are still raised out of the pass, logged as a failed pass.

*This supersedes Phase 19's rule that such rejections propagate.* Residual risk: if at least one full batch of rows is permanently poisoned, those rows are the oldest and still occupy the batch every pass (there is no per-row memory and no schema column to mark them).

### C. Bounded, progressive deferral and fair scheduling
**Interval.** A delivery whose patient is inactive stays `Scheduled` with `error_code = patient.inactive` and is never ended or abandoned for that reason. Its next check is `now + clamp(waited / 2, BackoffSeconds, ceiling)`, where `waited = now − scheduled_at` and `ceiling = min(MaxDeferralSeconds, 86400)`. Examples with the defaults: first check 60 s, after 4 h waiting the next check is in 2 h, after 48 h and ever after 24 h. **Hard maximum: 24 hours** (`GuidanceDeliveryService.MaxDeferralCeilingSeconds`; a larger configured `Guidance:Delivery:MaxDeferralSeconds` is treated as 24 h; a smaller one lowers it). Only existing columns are used (`scheduled_at`, `next_attempt_at`, `error_code`). Over 30 simulated days one deferred delivery costs fewer than 60 checks. Consequence to know: after a patient is reactivated the delivery goes out at its next scheduled check, which can be up to 24 hours later (or the configured ceiling).

**Selection per pass** (`SelectDueAsync`, at most two bounded queries and `take = clamp(batch, 1, 100)` rows, on the existing `(state, next_attempt_at)` index):
1. *Regular* due rows (any `error_code` other than `patient.inactive`), oldest `next_attempt_at` first (ties by id).
2. *Deferred* due rows (`error_code = patient.inactive`), oldest first.
3. Deferred rows are guaranteed `reserve = take / 4` slots (at least 1; none when `take = 1`) when that many are due; regular rows get the rest; capacity a group does not use goes to the other group. Regular rows are processed first.

With the default batch of 20: 5 reserved slots. 100 overdue inactive deliveries plus 5 fresh ones → the 5 fresh deliveries are all attempted in the first pass (plus 15 deferred rows are re-checked); a full batch of fresh rows still leaves 5 slots for deferred rows; with no fresh rows the whole batch goes to deferred rows. Deterministic, uses no shared state, so any number of instances compute the same selection and the existing row version still decides who claims a row.

### F. Stale error code
When an attempt starts, an `error_code` of `patient.inactive` is cleared in the same write that records the attempt. Any other code (for example `interrupted`) stays until the attempt has an outcome, and a failing attempt then shows its own new code (`channel.transient`), never the old one. API contract unchanged.

### D. Delivery-status wording (web and Flutter)
A delivery in state `Failed` with one of these codes was never attempted; it is shown with a neutral badge, not as "could not be delivered". Same keys and codes on both clients; mobile string files are byte-identical copies of the web ones (existing parity test).

| Code | English | Persian (approved) |
|---|---|---|
| `message.resolved` | This message no longer needs to be sent. | این پیام دیگر نیاز به ارسال ندارد. |
| `patient.missing` | Not sent: patient information is unavailable. | ارسال انجام نشد؛ اطلاعات بیمار در دسترس نیست. |
| `channel.unavailable` | Not sent: the sending service is unavailable. | ارسال انجام نشد؛ سرویس ارسال در دسترس نیست. |
| `message.missing` | Not sent: the message is unavailable. | ارسال انجام نشد؛ پیام موردنظر در دسترس نیست. |

Real failures (`channel.refused`, `channel.transient_exhausted`, `interrupted`, no code) keep "The notification could not be delivered. The message is still here."; a state other than `Failed` ignores a stale code. Replaces the Phase 19 web strings `rec.deliv.skippedResolved` / `skippedNoPatient`.

### E. CI migration-drift coverage
`.github/workflows/ci.yml` step "Check migrations are up to date with the model" now lists all ten contexts: Platform, Medications, Audit, Consent, Patients, ProductTrace, Guidance **plus ClinicalRules, ADR, Pharmacies**. Same command and conventions. Run locally for all ten: no pending model changes. The actual CI run was **not** observed.

### G. Index-filter guard
`Phase20Tests` pins the open-finding index: unique, columns `(PatientId, OriginFindingKey)`, filter exactly `origin_finding_key IS NOT NULL AND status <> 'Resolved'` in the model, the model snapshot and migration `AddOpenFindingUniqueness`; `Status` is stored as text and the literal equals the enum member name. Changing the predicate in the model, the snapshot or the migration fails a test. No migration change.

## Tests
`tests/MedSmarter.Patients.Tests/Phase20Tests.cs`: 31 test cases (30 run without PostgreSQL; 1 is `[PostgresFact]`: two concurrent instances with a deferred backlog deliver each of 30 fresh rows exactly once while 60 deferred rows stay recoverable). Covered: pass summaries (clean, with errors, unrecorded, failed part-way, cancelled) for both workers and their privacy; poisoned row + five healthy rows; persistent poison over three passes (one rejected write per pass, no duplicates); rejected end/defer writes; failing patient lookup; concurrency conflict stays quiet; interval bounds and monotonicity; 30 simulated days; quota rules (100 inactive + 5 fresh, full fresh batch, no fresh rows); inactive → active → retry clearing the code; guard for the index predicate. Four Phase 19 tests that asserted "a rejected pre-send write throws" were changed to assert "counted, nothing sent or audited, pass continues". Web: `phase8.test.tsx` (4 codes in English and Persian, real failures, stale code); Flutter: `phase8_test.dart` (same). Each fix was also checked by temporarily reverting it: the corresponding tests failed.

## Decision register
| # | Decision | Status |
|---|---|---|
| D-6 | Maximum deferral interval 24 hours | Approved; implemented |
| D-7 | A rejected pre-send write is counted, logged and skipped for this pass (supersedes Phase 19 "propagate") | Approved; implemented |
| D-8 | Structured logs are the operator visibility; no endpoint or heartbeat | Approved; implemented |
| D-9 | Neutral wording for the four never-attempted codes, approved Persian text | Approved; implemented |
| D-10 | No terminal retry cap for inactive patients | Approved; none added |

## Not done / deferred
Operator endpoints, permissions and worker heartbeat; transactional audit (audit is still written after commit, so a crash or cancellation between the commit and the audit write can leave a change without its audit line — see Phase 19 notes and the post-Phase-19 review); real channels; notification consent; emergency routing; alert prioritisation; a terminal state or retry cap for deferred deliveries; per-row memory of poisoned rows; any change to the Docker integration tests (not run). The CI drift check was run locally only.
