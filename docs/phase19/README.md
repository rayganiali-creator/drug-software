# Phase 19 — Monitoring correctness, send-time eligibility, duplicate prevention, idempotent delivery

Local, synthetic, no network. Nothing here sends anything anywhere: the only channel is still `LocalSimulatedChannel` (labelled SIMULATED).
This phase is **not** a notification service, **not** clinically validated and **not** production-ready. Delivery stays **at-least-once**; exactly-once
delivery is **not** guaranteed and cannot be without channel-side idempotency support.

## What changed

### 1. Monitoring (`MonitoringService`)
| Change | Behaviour now |
|---|---|
| Release on cancellation | A job claimed in a pass but not yet started (waiting for the concurrency gate, or the pass was cancelled while still claiming) is handed back to `Idle`, due now, `LastOutcome = cancelled`, no failure counted. Ownership-checked and best effort; the lease and recovery remain the safety net. |
| Per-item lease times | Each claim reads the clock itself, so a slow batch does not give later jobs a lease that is already mostly spent. |
| Recovery audit | Each interrupted job is recovered and saved on its own; only a job whose save succeeded is counted as recovered and audited (`MONITORING_FAILED`, reason `interrupted`). A job another pass recovered first is not audited here. |
| `FinishAsync` | Returns whether the result was applied. |
| `MONITORING_RESULT_DISCARDED` | New audit action, result `Failure`, reason `ownership_lost`. Written **only** when the pass lost ownership and the job row was **not** updated. Metadata: `job_result=not_applied`, `computed_outcome`, `assessment_status` (`may_have_run` or `not_run`), `assessment_id`, `error`. It is never labelled reassessed, recovered or applied. Applied outcomes now carry `job_result=applied`. |
| Run result | `MonitoringRunResult.Discarded` (optional, default 0) counts such jobs; they are not counted under the other outcomes. |

The assessment itself is a separate, idempotent write and **may have run** even when the job row was not updated; the audit wording says exactly that.

### 2. Send-time eligibility (`GuidanceDeliveryService`)
Checked just before the attempt is recorded and the channel is called, from facts the system already holds. **No notification-consent rule exists or was invented** (see decision register D-1).

| Condition | Behaviour | Why |
|---|---|---|
| Message is `Resolved` | Delivery ends: `Failed`, `ErrorCode = message.resolved`, no attempt, no send | No allowed status move leaves `Resolved` (`GuidanceService.MoveAllowed`; tested), so nothing is lost by ending it. |
| Patient record missing | Delivery ends: `Failed`, `patient.missing` | Nothing to send to. |
| Patient `Inactive` | **Deferred**, not ended: stays `Scheduled`, `NextAttemptAt` moved by `BackoffSeconds` (Phase 19; **superseded by Phase 20**: progressive, at most 24 h, see `docs/phase20`), `ErrorCode = patient.inactive`, attempts not incremented | Patient status can move back to `Active`; a terminal `Failed` would never recover (a re-raised finding finds the existing open message and does not schedule a new delivery). |

Audit: `GUIDANCE_DELIVERY_SKIPPED` (reason code; metadata `channel`, `mode`, `outcome` = `deferred`/`ended`, `sent=false`; no clinical text, no contact data). A deferral is audited once per reason, not once per pass. `DeliveryRunResult.Skipped` counts them.

Web label: a `Failed` delivery whose code is `message.resolved` or `patient.missing` is shown as "No notification was sent because …" (neutral), not as "could not be delivered" (which stays for real failures). The API is unchanged. (Phase 19 left the mobile app on its generic failed wording; **Phase 20 fixed that** and extended the neutral wording to `channel.unavailable` and `message.missing`, see `docs/phase20`.)

Known residual window: the check happens before the attempt is written, so a status change in the milliseconds between the check and the channel call is not seen (the simulated channel sends nothing; a real channel would need its own check, see risks).

### 3. One open message per finding
- Provider: PostgreSQL (Npgsql). Message status is stored **as its name** (string conversion in `GuidanceDbContext.ConfigureConventions`), so the filter is `status <> 'Resolved'`. (The review draft said `status <> 4`; that was wrong for this mapping and is not used.)
- New partial unique index `ux_guidance_message_open_finding` on `(patient_id, origin_finding_key)` where `origin_finding_key IS NOT NULL AND status <> 'Resolved'`. Migration `20261019181036_AddOpenFindingUniqueness` (additive; Down drops it). The existing non-unique lookup index is kept.
- A resolved message followed by a new one for the same finding (data changed) stays legal: history rows are not constrained.
- On unique violation `23505` for that constraint, `CreateCoreAsync` re-reads and returns the existing open message (`Existing = true`). The message and its delivery are one `SaveChanges`, so the losing request stores neither; the `GUIDANCE_DELIVERY_SCHEDULED` audit is written only after a successful save. If the winner was resolved in between, `message.conflict` is returned and a retry decides again.

**Preflight (read-only) — run before applying the migration to any database:**
```sql
SELECT patient_id, origin_finding_key, count(*) AS open_rows, array_agg(id ORDER BY created_at) AS message_ids
FROM guidance.guidance_message
WHERE origin_finding_key IS NOT NULL AND status <> 'Resolved'
GROUP BY patient_id, origin_finding_key
HAVING count(*) > 1;
```
Result on the dedicated local scratch database (synthetic rows only, including a legitimate resolved+open pair): **0 duplicate groups**; the query was also shown to detect a deliberately duplicated synthetic pair (inside a rolled-back transaction). The migration was applied, reverted and re-applied on that scratch database. It was **not** applied to any shared or real database. If the preflight returns rows on a database, stop: do not delete, merge or rewrite; the owner decides per pair (the migration would fail rather than clean data).

### 4. Idempotency and failed final save
- `DeliveryRequest` gains an optional `IdempotencyKey` (the delivery row id, `N` format) — the same for every attempt of one delivery. The channel still receives ids only.
- If the channel was called but the outcome cannot be saved (concurrency loss or a rejected write), the row stays `Attempted`, nothing is audited as delivered, `DeliveryRunResult.Unrecorded` counts it, and recovery makes it due again with the **same** key. A channel that honours the key can drop the repeat; one that does not will receive it twice.
- Which database failures are handled and which are surfaced: writes made **before** the channel is called (claim, skip/defer, terminal "no channel / no message") treat only an optimistic-concurrency conflict as "the row is another instance's" and move on; any other `DbUpdateException` propagated out of the pass (Phase 19 behaviour; **superseded by Phase 20**, where such a rejection is counted as an error and logged and the pass continues with the other rows), the row is unchanged, nothing was sent and nothing was audited. Only the write of the channel's **outcome**, which happens after the call and cannot be undone, treats every `DbUpdateException` as "unrecorded" (counted, not audited). Other failures (for example a lost connection) propagate in both cases and the row stays `Attempted`, so recovery applies.
- Consequence in Phase 19 (fixed in Phase 20): a persistently rejected pre-call write for the oldest due row was raised on every pass and blocked the rows behind it.

## Tests (`tests/MedSmarter.Patients.Tests/Phase19Tests.cs`)
25 tests in `Phase19Tests.cs` (after the hardening step; first drafted as 15); 21 run without PostgreSQL, 4 are `[PostgresFact]` (the in-memory provider cannot enforce unique indexes or concurrency tokens; these tests do not claim it can): concurrent raising of one finding (12 racers → 1 message, 1 delivery, 1 scheduled event; the 23505 path was shown to be exercised by disabling the handler, which fails the test), the database refusing a second open message while allowing history and finding-less messages, re-raising after resolution, and concurrent recovery auditing once. One existing assertion (`DeliveryRequest` property names) was updated for the new key.

## Decision register
| # | Decision | Status |
|---|---|---|
| D-1 | Notification consent: option (c) — defer consent-gated delivery until a real channel and its legal/product basis are reviewed. `Monitoring` consent is not treated as consent to be contacted. | Decided by owner; not implemented by design |
| D-2 | `MONITORING_RESULT_DISCARDED`, result `Failure`, only when ownership was lost | Decided; implemented |
| D-3 | Inactive patient = defer; resolved message / missing patient = end | Chosen in implementation (narrowest safe); owner may revisit |
| D-4 | Partial unique index with the string status filter | Decided; implemented; preflight clean on synthetic scratch data only |
| D-5 | Idempotency key = delivery row id on `DeliveryRequest` (optional parameter) | Decided; implemented |

## Not done / remaining risks
- No real channel, contact data, emergency routing, notification preferences, quiet hours, operator endpoints/permissions, worker heartbeat, leader election (all excluded).
- (Phase 19 limitation, addressed in Phase 20) A deferred delivery for a permanently inactive patient was re-examined every `BackoffSeconds`. It is now re-examined progressively less often, at most every 24 hours; there is still no terminal state for it.
- A real channel will need its own send-time re-check and idempotency support; this phase only provides the key.
- A cancelled claim `SaveChanges` whose outcome is unknown cannot be released (the pass does not hold it); its lease expires and recovery counts it as an interrupted attempt.
- Cancellation during the final job-result save may leave an assessment without an audit line for the discarded/cancelled result; the job is released.
- Preflight and migration were exercised only on synthetic local scratch data.
