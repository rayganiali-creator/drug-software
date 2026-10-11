# 3. Change-aware reassessment and the local worker

## What is compared
Each assessment stores `InputFingerprint` (SHA-256 prefix over the exact record versions it used, per-category availability and staleness, and age) and `InputVersions` (kind, record id, version token: medication v, allergy v, the **reference medicine's version**, symptom severity/resolution/onset, profile v+age). Rule-set version is compared separately. `Change` records what differs from the previous assessment (added / removed / changed counts, rule-set changed).
A category-level "something changed" is **not** used as proof that a finding changed.

## `ReassessIfNeededAsync` (pure decision, tested)
| Situation | Result |
|---|---|
| fingerprint and rule set equal to the latest assessment, not due | `Unchanged` — no new assessment |
| …and the earlier run left guidance `Failed/Partial` | `GuidanceRepaired` — guidance re-raised idempotently, **no new assessment** |
| a record version changed / category availability or staleness changed | `Reassessed` |
| the rule set changed (a rule approved, retired, …) | `Reassessed` (`Change.RuleSetChanged`) |
| latest assessment older than `ReassessAtLeastEveryDays` (default 7) | `Reassessed` (configured reassessment due) |
History is never overwritten: every assessment stays; open messages are never closed (`openGuidanceNoLongerMatching` only reports them).

## Reactivation of a resolved finding
`GuidanceOrigin.SubjectsFingerprint` = hash of the exact records the finding is about (ids + versions + reference versions). A resolved finding is raised again **only if that fingerprint changes** (a *new* message; the resolved one is untouched). An unrelated change does not re-raise it. Without a fingerprint (older messages) the Phase 7 data-as-of rule still applies.

## The job (`clinical_rules.monitoring_job`, one row per enrolled patient)
Enrolment is the patient's own act (`POST /patients/{id}/safety/monitoring`) and needs an **active `Monitoring` consent** covering `medications`; each further category is evaluated separately at every check (withdrawn consent ⇒ `skipped.no_consent`, nothing read). The row holds ids, state, times and outcome codes only (no clinical content).
Fields: state (Idle/Running/Exhausted/Withdrawn), `LastAttemptAt`, `LastSuccessAt` (set **only** after a completed check), `LastOutcome`, `LastErrorCode`, `ConsecutiveFailures`, `NextCheckAt`, lease, row version.
Pass (`MonitoringService.RunOnceAsync`): ① recover expired leases (counted as a failed "interrupted" attempt) ② claim ≤ `BatchSize` due jobs one by one with optimistic concurrency (two passes cannot both claim a job — also tested on PostgreSQL) ③ check each with ≤ `MaxConcurrency` in parallel ④ write the result only if the pass still owns the row (a withdrawal or recovery in between wins).
Retries: transient failure → exponential backoff (`BackoffSeconds`, doubling, capped); `MaxAttempts` (5) failures in a row ⇒ **Exhausted**: no more automatic retries, visible in the status, re-enrolling resets it. An assessment whose guidance could not be created is **not a success** (`failed.transient / guidance.incomplete`) and is repaired by the retry. Cancellation hands the job back immediately, without counting a failure.

## The worker
`PeriodicWorker` (BuildingBlocks): one bounded pass per interval inside the API process, started only when `ClinicalRules:Monitoring:WorkerEnabled` / `Guidance:Delivery:WorkerEnabled` are true (default **false**; interval floor 5 s). A failing pass is logged by **name and exception type only** (never the message) and the loop continues; host shutdown cancels the pass.
**Limits (stated):** passes run only while the API process runs and only on data that has been entered; there is no queue, no leader election (a second instance is safe because claims are optimistic, but not coordinated), no push from devices, and nothing is "continuous" or "real-time". It predicts nothing.
