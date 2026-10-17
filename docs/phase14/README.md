# Phase 14 — Offline behaviour, sync and notification reliability

Scope: what actually changed, what was proven, and what is deliberately not claimed.
No Redis, Kafka, cloud queue, paid push service, server or external call was added or used.

## 1. Mobile offline queue (Flutter)

Only one action can be queued: **reporting a planned dose** (Taken / Skipped / Missed). Everything else (medicines, batches,
adverse-event reports, consent, messages) needs the server and shows its "not connected" state. No other capability is invented.

| Item | Behaviour |
|---|---|
| Storage | `shared_preferences` on the device (the only local store available). It holds ids, the chosen status and the time the person made the report. **No token, no medicine name, no free text, no note.** It is plain preferences, not encrypted storage; this is why nothing sensitive beyond ids/status is put in it. |
| Scope | Keyed by account id. Another account never sees it. Sign-out, expiry, or starting the app signed out removes every stored queue entry (stabilization pass: it used to remove only the queue the current run had opened). |
| Bound | At most 50 operations. A 51st new dose is refused with a plain message; replacing the report for a dose already queued is allowed. |
| States | `pending` (not sent, saved on this phone only), `synced` (accepted by the server), `failed` (rejected, or retries exhausted), `conflict` (server holds a different, newer record). Each has a visible label; nothing is shown as saved before the server accepted it. |
| Idempotency | One live operation per planned dose; the newest local decision replaces an older queued one. Every operation has a local id. The server treats the same report for the same dose as a no-op (existing Phase 8 behaviour), so a lost response followed by a retry creates no duplicate and no new audit line. Overlapping syncs are blocked by a single-flight guard. |
| Retries | Bounded: 5 attempts while the server is unreachable, then `failed` (`retries_exhausted`); the person can retry (counter resets) or discard. A server *answer* (invalid, not found) is not retried. |
| Restart | The queue is reloaded from preferences; a damaged entry is dropped, never guessed at. |
| Sync trigger | When the Medicines screen opens with pending items, and on "Send now". There is **no background sync**: Flutter background execution is not implemented and is not claimed. |

### Conflict rule (server)

`LogIntakeCommand` has an optional `ClientRecordedAt` (when the person made the report on the device). If the server already
holds a **different** status for that dose whose `RecordedAt` is later than `ClientRecordedAt`, it answers
`409 intake.newer_record_exists` and changes nothing. The app moves the operation to `conflict` and the person decides
("Keep the saved record" discards the queued report). A report older than nothing, the same report, or a genuinely newer client
decision follows the normal rules (idempotent / versioned correction). Only **replays** of queued reports send
`ClientRecordedAt`; a live report does not (found in the post-Phase-15 review: a phone clock running behind would have made
the server treat a live correction as older than the record it had just saved).

Limits: the comparison trusts the device clock for `ClientRecordedAt`; a wrong device clock can mis-order two reports. The
conflict is surfaced, never resolved automatically in either direction beyond this rule.

## 2. Web

The web app has no offline mode and none is claimed. A failed request shows the existing error/retry state; no data is cached
or stored in the browser for later sending.

## 3. Notification reliability (backend)

States stay distinct (Phase 8): `Scheduled` → `Attempted` → `Confirmed` / `Failed`. Only a channel confirmation produces
`Confirmed`; startup recovery of an abandoned attempt never promotes it to `Confirmed`. The only channel is the local
**simulated** channel and every row says `Simulated`. No real push, SMS, email or browser push exists.

**Defect found and fixed (High):** two API instances could both claim the same due delivery and send it twice (read-then-write
claim, no concurrency control). Fix: an optimistic `Version` token on the delivery row, bumped on recovery, claim and finish;
a claim that loses the race is dropped without calling the channel. Regression test
(`Phase14Tests`, PostgreSQL only — the in-memory provider does not enforce concurrency tokens) runs two service instances
against one database and asserts exactly one channel call, one `Confirmed`, one attempt. It failed (2 calls) before the fix.
Migration `AddDeliveryClaimVersion` is additive.

Other properties (tests from Phase 8 still pass): bounded attempts with backoff, lease expiry for abandoned work, restart
recovery.

### Urgent findings do not depend on the worker

A manual assessment creates the in-app message **synchronously**; the periodic worker only handles routine delivery and
scheduled checks. There is still **no emergency delivery path**: no real channel exists and no sourced emergency criteria
exist, so messages stay capped below the Urgent level. This is a documented gap, not something simulated away.

## 4. What is not done

* Real push (FCM/APNs/web push) — needs a paid/external provider; not activated.
* Background sync on mobile; encrypted local storage (needs a plugin and a decision on key custody).
* Queueing for any action other than dose reports.
* Multi-instance coordination of the Phase 8 monitoring (reassessment) job: the delivery race is fixed and tested, the
  monitoring job has not been given a multi-instance test.

## 5. Tests

* .NET: `Phase14SyncTests` (2: conflict, idempotent replay + newer correction), `Phase14Tests` (PostgreSQL two-instance race).
* Flutter: `test/phase14_test.dart` (13: queue states, storage content, restart, single-flight, conflict, bounded retries,
  rejection, bound, sign-out, damaged storage, taking-screen offline → pending → sent).
