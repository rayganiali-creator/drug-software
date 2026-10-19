# Phase 18 — Complete and safe access to the adverse-event queue

Status: implemented and reviewed. Owner-directed scope (after the Phase 17 assessment). Synthetic data only; no network, service, dependency or migration was added. Persian wording is unreviewed; nothing here is clinical validation, compliance or production readiness.

## 1. The problem Phase 17 left
`GET /adverse-events/queue` read the newest 200 open records of ALL patients, and only afterwards checked relationship and consent per patient. A reviewer's older readable records could therefore be unreachable, whatever the page said. Phase 17 made that visible (header + warning); Phase 18 removes it.

## 2. What changed
* **Patients are decided first, records second.** `AdrEndpoints` (queue handler): candidates = `IUserIdentityService.ListRelatedPatientsAsync(actor, includeDisabled: true)` (active care relationships only); each candidate (not the reviewer, professional role) is decided by `IAccessAuthorizer.AuthorizeManyAsync(actor, adr.review, Patient(id,"adr")…)`, which gives every patient exactly the decision and the audit entry `AuthorizeAsync` would give (relationship, consent, organization; only the relationship and consent reads are shared across the candidates). A read failure, a cancellation, or an answer of the wrong size fails the request with a server error and returns nothing; only the allowed ids are passed to the new `IAdverseEventService.ListQueueAsync`, whose query starts with `SubjectId IN (allowed)`. The old unscoped `ListByStatusAsync` no longer exists; the service answers an empty page for an empty subject list (there is no "all"). The repository already used this shape for `GET /manufacturer-reports/pending-review` (related patients → authorizer → scoped query); the ADR queue now follows it.
* **Cursor paging**, ordered by `LastActivityAt` descending then `Id` descending (unique tie-breaker), seek predicate `(LastActivityAt < t) OR (LastActivityAt = t AND Id < id)`. One extra row is read to know whether another page exists.
* **Authorization and consent are re-evaluated on every page request.** The cursor never grants anything.
* No new endpoint, permission, schema or migration. One additive, optional parameter (`includeDisabled`, default false) on `ListRelatedPatientsAsync`; its existing callers (care list, pending reviews) are unchanged.
* `Cache-Control: no-store` is now set on the queue response (it carries record excerpts and the count). The project's pattern (`ctx.Response.Headers.CacheControl = "no-store"`, as in `Security/Authorization.cs`) was reused; previously no data endpoint set it.

## 3. API contract (`GET /adverse-events/queue`)
Permission `adr.review` (unchanged); a caller without it gets the uniform 403 and none of the headers below. Body: unchanged JSON array of `AdverseEventSummaryDto`.

| Parameter | Rule |
|---|---|
| `status` | unchanged (comma list; unknown tokens ignored; empty = Submitted, InReview, InformationRequested) |
| `limit` | absent → **50**; integer 1–100. Anything else (empty, `0`, `101`, `-1`, `abc`, `1.5`, `+5`, ` 5`, repeated, overflow) → `400`, code **`limit.invalid`** (rejected, never silently clamped) |
| `cursor` | absent → first page. Present → must be ≤ 200 chars of `[A-Za-z0-9_.-]`, version `v1.`, well formed, issued to THIS caller for THIS status set; anything else, and a repeated parameter → `400`, code **`cursor.invalid`** (one code for every reason) |

| Header | Meaning |
|---|---|
| `X-Result-Truncated` | `true` only when at least one more authorized record can be fetched; otherwise `false` (last page, empty page, exactly-full last page). Clients treat only an exact `false` as complete |
| `X-Next-Cursor` | sent only with `X-Result-Truncated: true`; absent on the last and on empty pages |
| `X-Total-Count` | first page only (request without `cursor`): the number of records with the requested statuses whose patient the caller was authorized for in this request, counted by the server over the same predicate. Never sent on cursor pages, never for a refused caller |

All three are exposed through CORS (`Program.cs`). They are set only after the permission check. Audit: still exactly one `ADR_ACCESSED` line per request (count of rows returned); no cursor or ids are logged.

### 3.1 Compatibility with existing clients (recorded before the default changed)
* **Callers found:** the web SPA (`web/src/adr/api.ts`) is the only client of this endpoint in the repository (`mobile/lib` has no call; `ai/` none). It is updated here to page.
* **A client that sends no parameters** gets a bare array, at most 50 authorized records, and `X-Result-Truncated: true` when more exist (the Phase 17 warning, now literally true). Before, such a client received up to 200 *global* records filtered to what it may read: for a reviewer with more than 50 readable open records an old, cached web bundle therefore shows fewer rows until it reloads. This is a real, bounded loss for stale bundles only; it was judged acceptable because (a) the old list was incomplete in an unpredictable way, (b) the stale client is told the list is incomplete, (c) the maximum page size is fixed at 100 by the owner, so a larger legacy default is not allowed. **Needs the owner's explicit acknowledgement.**
* Unknown extra headers are ignored by clients that do not read them.

## 4. Cursor protection (analysis, no signature)
Content: version, `LastActivityAt` UTC ticks, record `Id`, status-set mask, requesting user's id (base64url, no patient data). It is a position, not a permission.
* *What editing the fields can do:* the position is used only as a comparison value (`(LastActivityAt, Id) <` that value). The record named by the id is never looked up, so an id of an unreadable record behaves exactly like a random id (tested). The status set used by the query comes from the request, not from the cursor, and a cursor made for another set is rejected, so editing the mask cannot widen the filter. Editing the user id to one's own id gives the caller nothing they did not have. The only effect of a forged position is that the caller skips or repeats part of THEIR OWN authorized list. The authorized subjects are recomputed per request, so no forged position can reach a record outside them (tested with positions 0, max, the id of an unreadable record, and the all-F id).
* *Cross-user / cross-scope use:* rejected with the same `cursor.invalid` (the caller cannot tell why); even without that check the cursor would only be a position in the other caller's own set.
* *Size and format:* length cap and character set are checked before decoding; ticks, ids, mask are parsed strictly (no signs, spaces); 4 fields exactly.
* *Why no HMAC:* nothing confidential or privileged is inside; the repository has no key facility for this (the only HMAC is the patient-identifier hash key used for a different purpose) and the owner asked for no new secret. If a future cursor ever carried anything sensitive, signing would be required.
* *Precision:* the cursor is built from values read back from the database (PostgreSQL stores microseconds), so rounding cannot create a gap or a repeat. Ordering of `Id` is whatever the database orders by; the walk tests compare against the database's own order and passed on both providers.

## 5. Changes while someone pages (documented behaviour, tested)
| Event during a walk | Result |
|---|---|
| New report arrives | not part of this walk; first on a fresh first page |
| Record already shown gets activity | not shown again (it moved toward the top); no duplicate |
| Record not yet reached gets activity | jumps ahead of the cursor → **left out of this walk**, and is at the very top of a fresh first page: temporary, never permanent |
| Record leaves the open statuses | disappears |
| Consent revoked / relationship ended between two requests | next request returns none of that patient's records, no cursor, no total |
| Several records share one instant | strict order by `Id`; every record is visited exactly once |
Why `LastActivityAt` stays the order: "recent activity first" is the meaning of the queue and the existing index `(Status, LastActivityAt)` matches it. Ordering by the immutable `ReportedAt` would remove the skip but a new follow-up on an old report would no longer rise to the top, which is the main reason to look at the queue; no real problem in the repository justified that change. The page therefore says "List as of HH:MM. Reports change while you work: refresh to see new or updated ones" under the list and offers **Refresh** always; the end of the list reads "End of the list as of HH:MM…".

## 6. Audit: what the new path writes
`AccessAuthorizer.AuthorizeAsync` writes `ACCESS_DENIED` for every denial (`AccessAuthorizer.cs`). Before: one per *distinct patient in the capped global page* the reviewer could not read, including patients the reviewer has no relationship with, up to 200 per request. Now: the authorizer runs only for patients the reviewer is related to, so a denial is written only for **related patients without consent**, once per request each (pinned by `Phase18ApiTests.Audit_*`: related-without-consent patient → exactly 1 per request; unrelated patient → 0; exactly one `ADR_ACCESSED` per request). No audit line was removed or weakened and nothing was deduplicated. Residual: a reviewer related to many patients who withheld consent writes that many `ACCESS_DENIED` lines per request, and the dashboard tile plus the queue page each make a request. The candidates are authorized in one batch (`AuthorizeManyAsync`): the relationship and consent reads are shared, the denial events are unchanged (one per denied patient, same content and order). With many denied patients the audit appends dominate the time (about 2.5 ms each in a local synthetic test; not a production figure). Actual behaviour on failure (tested in `AuthorizationBatchFaultTests`): if a read fails or is cancelled before the batch's decisions are complete, **no** denial events of that batch are written and the request returns a server error; if an audit write fails, the denial events before it were written and the failing one and the rest were not, and the request returns a server error. Reducing the audit cost further (an aggregated denial line, or several entries per transaction) would change the audit model and needs a separate decision.

## 7. Disabled accounts
Three different things were separated (`Domain.cs`, `CareRelationshipService.cs`): the user **account** state (`UserStatusValue.Disabled`), the **patient record** state (`PatientStatus`), and the **care relationship**. Dynamic relationships already ignore an inactive patient *record* (`ActiveForPatientAsync`/`ActiveForProviderAsync`), so that case never reached the queue and is unchanged. The authorizer and the old queue ignored the *account* state; `ListRelatedPatientsAsync` skipped disabled accounts. To keep the old behaviour (an open report must not vanish because an account is disabled) the new optional parameter `includeDisabled` keeps them, and also keeps a patient with an active dynamic relationship who has no identity row (the authorizer decides from the relationship alone). Parity tests: for every Physician/Pharmacist demo account the paged queue equals the old rule (per-patient `AuthorizeAsync` over all open records, no cap); a disabled account keeps its records while the care list still leaves it out.

## 8. Dashboard count
`X-Total-Count` is the only source of the number on the tiles (`useOpenAdrCount` asks for one row and reads the header). Not an integer, absent, negative, with a sign, or too long → a dash and "The exact count is not available here. Open the queue." The count is computed by the server with `COUNT(*)` over the same predicate as the page, in the same request, after the same per-patient authorization (so it contains nothing about unreadable patients); it is a snapshot of that moment. "At least N" is not used. `COUNT(*)` cost on large tables was not measured.

## 9. Web
`useAdrQueue` loads the first page (50) and follows `X-Next-Cursor`; states: loading, error (existing), ready, **loading more** (button disabled), **next-page error** (rows stay; "Try again" repeats the same cursor), **cursor no longer accepted** (400 → message and Refresh only), **access ended** (403 → the normal refusal state, rows gone), **end of list** (as of a time), **refresh** (restarts from the top; also on filter change, which drops appended rows). The Phase 17 notice follows the LAST page: `true` → "This list may be incomplete"; missing or unexpected → "We cannot confirm this list is complete" (and no "Load more", a cursor is only followed beside an exact `true`); exact `false` → no warning. Duplicate rows across pages are shown once.

## 10. Indexes
No migration. Existing indexes: `(SubjectId, ReportedAt)`, `(Status, LastActivityAt)` (`AdrDbContext.cs`). No query plan or timing was collected, so nothing is claimed about speed or cost. Optional later step (needs the owner's go-ahead): on the local PostgreSQL with synthetic rows, run `EXPLAIN (ANALYZE, BUFFERS)` for the first page, a cursor page and the count with several subject-list sizes, and decide on `(SubjectId, Status, LastActivityAt)` only from that evidence.

## 11. Stored patient workflows that existing endpoints could complete (read-only review; nothing changed)
The patient pages still on sample data and what already exists server-side (`web/src/records/api.ts`):
| Page (sample data) | Existing endpoint(s) that could back it | Blocker / decision before wiring |
|---|---|---|
| `PatientHome` – today's doses and "taken" | `doses(id, date)`, `logIntake` (Taken/Skipped/Missed) | the sample has an "undo"; there is no undo endpoint, only append of a new intake line; product decision on semantics |
| `PatientHome` – adherence series | `adherence(id, days)` summary | the sample series is a flat number list; the real summary has different fields (no invented mapping) |
| `PatientHome` – alerts/activity | `messages` (guidance) | sample "alerts" have no sourced meaning; messages are rule-based/simulated channel |
| `Profile` – profile card | `profile`, `saveProfile` | sample preferences (reminders, language) have no backend |
| `Medications` – list | already available as `/app/patient/taking` (stored) | sample page also shows prescriptions/interactions with no backend |
| `CheckIn` | `symptoms` needs text, severity and onset | the check-in has mood/symptom ids but no severity: mapping would invent data |
Recommendation: the smallest candidate is `PatientHome` doses → `doses` + `logIntake`, after the "undo" decision. Not started.

## 12. Tests
* `tests/MedSmarter.Patients.Tests/Phase18ApiTests.cs` (30 cases, run on the in-memory provider AND on local PostgreSQL): older authorized record beyond the former 200 boundary; full walk with equal timestamps in the database's order; total only on page one; empty / exactly-full / last page; status filter and cursor binding; nothing of unreadable patients in bodies, headers, total or cursor; consent revoked and relationship ended between requests; disabled account; parity with the old rule for every professional demo account; invalid `limit` (10 forms) and valid bounds; bad / foreign / tampered / oversized cursors; forged positions; replay; changes during a walk; `no-store` and CORS exposure; clients without parameters; audit lines; the service's empty-subject and malformed-request behaviour.
* `Phase17ApiTests.cs`: the test that pinned "an authorized older record may be missing" was removed (its opposite is the first Phase 18 test); the header-under-cap and refused-caller test stays.
* `web/src/__tests__/phase18.test.tsx` (32): paging, dedup, errors, expired cursor, 403, refresh, filter reset, notice rules incl. missing/unexpected headers, Persian keys, and a guard that reads `routes.tsx`: every page on the sample services is wrapped by the sample notice (or listed with a reason), every wrapped page really uses the sample services, the stored ADR pages never do. `phase17.test.tsx` and `phase9.test.tsx` were adjusted to the new contract (count from `X-Total-Count`; `&limit=50` in the request). The Phase 17 truncation-notice tests pass unchanged.

## 13. Remaining limitations / decisions
* Per-request cost of the scope (identity scan, batched relationship and consent reads, denial audit appends, `COUNT(*)`) was measured only on a local synthetic harness, not under production-like conditions; the audit options in §6 remain open.
* Stale browser bundles show at most 50 rows (§3.1).
* A record updated mid-walk is left out of that walk (§5).
* No Persian review. Flutter does not use this endpoint. IntegrationTests (Docker) and the Python tests were not run.
* Roadmap: monitoring/notifications moved to Phase 19; the two following phases moved one number down (relocation only).
