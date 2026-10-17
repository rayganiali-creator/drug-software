# Post-Phase-15 stabilization — change log

Scope: reduce the risks M-2, M-4, M-6, M-5 of the post-Phase-15 review. No Phase 16, no new features, no external service, no network. Synthetic data only.

## 1. M-2 — industry analytics differencing

**Verified:** each table was suppressed on its own and the window was free-form, so (a) "total from one table − visible cells of another" isolated a single hidden small group, and (b) shifting the window by days (or querying the still-filling current month at two times) isolated a day's reports.

**Mitigations (smallest defensible):**
1. *Complementary suppression* (`DisclosureRule.Apply`): if any group is hidden, the hidden groups together must hold at least *k* reports; otherwise the smallest visible groups are hidden too. A single small group can therefore no longer be recovered by subtraction.
2. *Month-aligned, closed windows only* (`IndustryInsightsService`): the requested dates are rounded to whole calendar months, the month in progress is excluded, at most 12 months. The response states the effective period. A one-day shift now returns the identical window; a repeated query on a later day returns the same closed months.
3. Existing: k on reports and on distinct reporters, k floor 3, no totals, audit line per read, organization/manufacturer scope.

**Not mitigated (residual — needs a privacy expert):**
* Differencing **between two different months windows** (e.g. Jan–Mar minus Jan–Feb) can still isolate a month's cell for a product/issue that is visible in both windows. Closing it needs a finer-grained rule (suppress on the finest cell, or fixed non-overlapping periods), or noise.
* **Late-arriving data:** a report can enter an already-closed month later (back-dated occurrence, delayed `Sent`); two queries at different times can differ by exactly that report.
* **Intersection attacks** across tables with different hidden sets, and **repeated querying**. The only mitigation here is the audit line per read; I did not verify that the insights route is covered by a rate limiter.
* Group size ≥ k is not anonymity: rare product × rare event × month can identify a person to someone who already knows them. No claim of anonymity or compliance is made.
* Manufacturer attribution is patient-asserted (counts are unverified).
* Whether any of this sharing is lawful, and the right *k* / noise, are decisions for a privacy/legal expert (matrix Q-14).

**Tests:** `Phase15DisclosureTests` (complementary rule, inclusive boundaries, 500-shape property check), `Phase15WindowTests` (month snapping, current month excluded and stable over time, day-shift attack, organization isolation between two linked organizations), existing `Phase15Tests`/`Phase15ApiTests`, web `phase15.test.tsx`.

## 2. M-4 — `analytics.read` / `signals.read`

**Verified (the review report was wrong here):** `analytics.read` is **used**: it guards `GET /analytics/summary` (a fixed demo payload, no stored or organization data) and is the web *area* gate for the industry/researcher area (UX only). `signals.read` is **unused** by any route or UI.

**Changes:** `signals.read` removed from the catalog and from both roles (generated C#/TS/Dart/JSON regenerated; 78 permissions). `analytics.read` kept, its catalog description now states it is a demo stub with no organization check and must never guard organization or patient-derived data. Added a route-inventory test that fails if any aggregate-kind permission guards a route other than `/analytics/summary`, a test that the stub payload is `demo:true` with no organization field, and negative tests that the Industry role cannot read another organization's insights and the Researcher role cannot read any organization's insights. `docs/phase3` tables updated.

## 3. M-6 — monitoring job across instances

**Verified:**
* **Claim race: not present.** The claim is an optimistic `RowVersion` update; a PostgreSQL test with two instances started together over 24 due jobs shows every job claimed and checked exactly once (it passed before any change).
* **Real gap found:** a pass whose lease expired and whose job was recovered and re-claimed by another instance could still write its late result over the new owner's claim (`FinishAsync` checked only `State == Running`, which is true again after a re-claim). The test reproduced it.

**Fix:** `FinishAsync` now requires the row's `RowVersion` to equal the version this pass wrote when it claimed; a stale pass changes nothing. **Tests:** `MonitoringMultiInstanceTests` (PostgreSQL only; `[PostgresFact]`, skipped without `MEDSMARTER_PG_TEST`).
**Not changed:** if a recovery save conflicts, the interrupted-audit lines may be written by both instances (duplicate audit line, no data effect).

## 4. M-5 — offline queue on the device

**Verified contents:** per account: operation id (random), planned-dose ids (`scheduleEntryId`, `patientMedicationId`), planned time, chosen status, queued time, state, attempt count, error code. No token, name, note or free text. The account's user id is part of the storage key.

**Encryption — not done, deliberately.** No secure-storage or cipher package is available offline in this environment, and adding one needs a download and OS keystore integration that cannot be verified here. Encrypting with a key kept in the same preferences file or in code would be false protection, so nothing was added. The payload stays minimal. Needs: a decision on a platform keystore plugin and device testing.

**Fixed (privacy lifecycle):** sign-out/expiry used to remove only the queue the current run had opened; a queue left by an earlier run stayed on disk. Now sign-out, expiry and starting signed out remove **every** stored queue entry. Trade-off stated: unsent reports are lost with it. Removing the preferences entry does not claim to erase all device traces (backups, flash, OS caches).
**Tests:** `phase14_test.dart` — purge without binding, other settings kept, controller start signed out, expiry and sign-out clear and isolate, plus the existing retry/conflict/bound/restart/account tests.

## 5. Files changed

See `POST_PHASE_15_STABILIZATION_REPORT.md` (delivered in chat, not committed) for the exact list and results.
