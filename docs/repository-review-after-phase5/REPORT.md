# AI MedSmarter — Repository-wide review after Phase 5

Branch `claude/amazing-darwin-kfj6sv` · review date 2026-10-09 · **review and stabilisation only — not Phase 6.**
Status words: **PASS** actually executed and passed · **FAIL** executed and failed · **NOT RUN** unavailable or not executed · **NOT APPLICABLE**.
Nothing in this report says the platform is production-ready or safe for real patients. **It is not.**

---

## 1. Executive summary

* The architecture is coherent: a modular monolith with Contracts-only module boundaries (enforced by architecture tests), deny-by-default HTTP authorization, RBAC → relationship → consent layers, per-module PostgreSQL schemas with migrations, and a durable hash-chained audit log. Mock providers cannot start outside Development/Testing.
* I traced the main workflows end to end (login → record → consent → care relationship → batch → report → review → outbox → Mock acknowledgement) by reading the code, by writing new adversarial tests, and by driving the **real running API on PostgreSQL** with `curl`.
* **Two real defects were found and fixed** (both with regression tests, one proven to fail without the fix):
  * **R-01 (High)** — the manufacturer-report send-time consent check only looked at the *batch* scope, so a patient who narrowed their consent after approval still had age band, sex and other medicines sent.
  * **R-02 (Medium-High)** — the free-text privacy filter could be bypassed with invisible characters (zero-width space, word joiner, soft hyphen) hiding an e-mail address, link or phone number.
* **No critical defect was found in authorization.** A new test enumerates every route in the host and proves each one is either on a short public list or names an explicit `perm:` policy; live probes (IDOR, alg-none token, injection-like search text, oversized body, wrong method, admin routes as a patient) all behaved correctly.
* **Blockers that remain (not fixable in this review, all known):** login/identity/session storage are a development mock held in memory; guidance *producers* and any clinical-safety layer do not exist; de-identification of free text cannot be guaranteed; guidance text is not clinically validated; Windows/macOS/Android/iOS were never built.
* Verification results are in §7. Everything executed passed, except **two intermittent failures in the PostgreSQL run of `Patients.Tests` that passed on every re-run** (see §7 and finding T-01). Docker-based integration tests, CI itself and all non-Linux platform builds are **NOT RUN**.
* **No purchase, no real external integration, no Phase 6 feature.**

---

## 2. Phase-by-phase findings

| Phase | What was checked | Result |
|---|---|---|
| 1 — Platform skeleton | solution layout, central package management, analyzers-as-errors, health endpoints, Docker/compose, CI | Build `-warnaserror`: **PASS**. Compose binds to 127.0.0.1 and runs `Development` (mock auth + demo data) — acceptable for local use only. `docker compose` itself **NOT RUN** (no daemon). |
| 2 — Design system / clients | web + Flutter shells, i18n generation, RTL/Jalali, a11y | `design/build.mjs --check` **PASS**; web lint/tsc/vitest/build **PASS**; Flutter analyze/test **PASS**. |
| 3 — Identity, authz, consent, audit | JWT handling, sessions, refresh rotation + reuse detection, RBAC catalog, uniform 401/403 | Sound for what it is. **Everything behind login is a mock** (`DevelopmentMock`), and users/sessions/devices/login-throttle are in memory (finding H-01). Token validation: HS256 only, issuer/audience/lifetime checked, `alg:none` rejected (live probe → 401). Refresh-token reuse kills the session (code + tests). |
| 4 — Medication knowledge, AI | models, provenance, versioning, search, AI gateway, insurance mock | PostgreSQL repository + `pg_trgm` verified on a real database (Knowledge tests 180/180 on PostgreSQL). Separation of duties verified. External AI needs approval + key + (for patient context) the patient's consent; question text is screened only by the global flag (M-01). Insurance mock is fully decoupled from reports and clinical data (no reference in Patients/ProductTrace/Guidance; architecture tests). |
| 5 — Patient layer | profile, medicines, batches, reports, consent, care, guidance, clients | Two defects fixed (R-01, R-02). Details in §3–§6. |

---

## 3. Cross-phase architecture review

**Strengths (verified):**
* Module boundaries: architecture tests PASS (5); Contracts reference only Contracts.
* Deny-by-default: `FallbackPolicy` requires an authenticated user; **new test** `Every_route_either_is_on_the_public_list_or_names_an_explicit_permission_policy` lists all endpoints (>100) and fails if any anonymous route is off the allow-list or any other route has no `perm:` policy. Only `/auth/logout`, `/auth/logout-all`, `/auth/me` (session only) and `/guidance/samples` (permission checked inside the handler) are allow-listed.
* Uniform refusals (403 for "no such patient", "not yours", "no consent"; denial reason only in the audit log).
* Patient-layer routes go through one helper (`Route(...)`): permission policy → ownership/relationship/consent → active-patient check for professionals → audit line for other people's data.
* Persistence switch refuses `InMemory` outside Development/Testing; seeding, Mock provider and Mock AI are refused outside Development/Testing; an unset `ASPNETCORE_ENVIRONMENT` means *Production* (fail-safe).
* Migrations: fresh database + `--migrate-and-exit` → all seven schemas created (**PASS**, executed); `has-pending-model-changes` → no drift for all 7 contexts (**PASS**). There are no cross-schema foreign keys, so migration order does not matter.

**Weaknesses (open):**
* **A-01 (Medium)** Audit rows are written *after* the business transaction commits, in a separate transaction. If the audit append fails the request answers 500 although the data changed, and no audit row exists. (Fail-closed for the caller, not atomic for the data.)
* **A-02 (Medium)** No referential integrity between schemas: `patient_medication.medication_id`, `product_record.medication_id` are plain ids. Medications are never hard-deleted today, so nothing dangles yet.
* **A-03 (Medium)** Search ranks in memory: `FindTermsAsync` loads up to `MaxCandidates` rows and the visible-id set per request, then paginates in memory. Fine for hundreds of medicines, not for a real catalogue.
* **A-04 (Medium)** `pending-review` and `/patients` do one authorization (several queries) per related patient (N+1). Fine for tens of patients.
* **A-05 (Low)** Lists such as products/conditions/allergies/medications are unpaginated (one person's data). Symptoms are clamped (1–200), audit (1–500), search limit validated (400 `limit.range`), outbox (1–100).
* **A-06 (Low)** No explicit request-body size limit beyond Kestrel's default (a 10 MB body was rejected with 400 in a live probe).
* **A-07 (Low)** EF logs `Error` for the "history table does not exist" probe on the first migration of every schema; it is harmless but would trigger alerting.

---

## 4. Security and privacy findings

### Fixed in this review
| ID | Severity | Where | Evidence | Impact | Fix | Verified by |
|---|---|---|---|---|---|---|
| **R-01** | **High** | `ManufacturerReportService.ProcessDueAsync` (send-time consent re-check) | Test `Narrowing_consent_after_approval_stops_data_the_new_consent_no_longer_covers`: patient consents to [products, profile, medications], report with other medicines is approved, patient revokes and grants [products] only → **before the fix the report was sent** (test failed with the fix stashed) | Age band, sex and other medicines left the system without a covering consent | Re-check against **every scope the frozen payload contains** (products; + profile if age/sex present; + medications if other medicines present) | `ReportTests` 22/22; the new test fails without the fix (executed) and passes with it |
| **R-02** | **Medium-High** | `BuildingBlocks/FreeTextGuard.cs` | New tests: `sara​@example.com`, `0912​3456​789`, `ht​tps://…`, `12345­67890`, word-joiner e-mail → all **accepted** before the fix (live API probe now returns 400 `text.looks_identifying`) | Identifying text could reach lists, AI context and manufacturer payloads while the filter looked active | Invisible *format* characters (Unicode category Cf) are ignored when checking; stored text is unchanged (Persian ZWNJ still allowed) | `ReviewTests` (6 bypass vectors + ordinary Persian/English text accepted) |
| R-03 | Low | docs | Phase 5 doc called `consent_event` "immutable"; there is no DB trigger on it | Overclaim | Reworded; Phase 3/4 docs that still said "in memory / not built" got an "Update after Phase 5" note | review of docs |

### Open findings
| ID | Severity | Finding | Impact | Recommended minimal correction |
|---|---|---|---|---|
| **H-01** | **High (blocker)** | `Auth:Mode=DevelopmentMock`: log in with an account id, no password; identity, sessions, devices and the login throttle are in-memory. `AuthGuard` refuses to start it outside Development/Testing, so **production has no way to log in at all** | Nothing can be used by real people; a restart logs everyone out | Real identity provider + persistent identity/session store (a future phase). Do not "enable" the mock in any shared environment |
| **M-01** | Medium | `AssistantService`: for the **External** provider the user's free-text *question* is sent after only `Ai:AllowExternalDataTransfer` + key + model are set — no patient `AiExternalProcessing` consent and no `FreeTextGuard` on the question (the patient *context* is correctly double-gated) | A user could type identifying data into a question that then leaves the system | When `Provider=External`: run `FreeTextGuard` on the question and require the `AiExternalProcessing` consent of the caller. Off by default; not reachable today (Mock default, no key) |
| **M-02** | Medium | Audit chain is **tamper-evident, not tamper-proof**: the application's database role owns the table and can drop the triggers; whoever can rewrite the whole chain and recompute hashes is undetectable; the chain head is not anchored outside the database | Claims such as "immutable" would be wrong | Separate DB roles (app role has INSERT/SELECT only), periodic export/anchoring of the head hash. Docs now say "append-only, tamper-evident" |
| **M-03** | Medium | All audit appends serialise on one advisory lock; `/audit/verify` streams the whole table, is not rate limited and needs only `audit.read` | Throughput ceiling; a permitted admin can load the DB | Bound/paginate verification, rate-limit it, verify in a background job |
| **M-04** | Medium | **Re-identification remains** after removing direct identifiers: exact event date + batch/lot + manufacturer + age band + sex + a rare event or free-text detail can identify a person to the manufacturer; free text may contain names/places/rare details no regex detects | `FreeTextGuard` is a screening aid, **not anonymisation** | Keep mandatory human review for adverse events; add a documented review procedure and a DPIA before any real transmission; consider coarsening dates (week) per manufacturer agreement. Docs now state this explicitly |
| **M-05** | Medium | Reviewer endpoints return the patient's pseudonymous subject id (needed to act on the report) | A reviewer can link reports to the id | Replace by an opaque per-review handle |
| **M-06** | Medium | `/directory/providers` lets any patient list **all** professional accounts (id, display name, roles) | Enumeration of staff | Restrict to professionals who opted in or to search-by-exact-handle |
| **M-07** | Medium | `/analytics/summary` returns **fixed, fabricated numbers** (labelled demo) to the Industry role | Looks like real analytics | Remove or clearly gate before any real use (no real analytics exist) |
| **M-08** | Medium | Login throttle is per account id, in memory, no IP limiter; anonymous `/auth/login` has no rate-limit policy | Account lock-out DoS; credential stuffing once real credentials exist | Per-IP + per-account limits in a shared store with the real provider |
| **L-01** | Low | Patient subject GUIDs appear in request paths and audit lines (pseudonymous). Logs were checked live: no token, name, e-mail or request body in the API log (0 hits for `Bearer`, `eyJ`, `Sara`, `example.invalid`) | — | Acceptable; keep |
| **L-02** | Low | Token validation uses sync-over-async (`GetAwaiter().GetResult()`) | Thread-pool pressure under load | Use the async API |
| **L-03** | Low | `AllowedHosts: *`; no HSTS (TLS not terminated here) | Deployment hardening | Set at deployment |
| **L-04** | Low | iOS ATS: plain `http://localhost` development URL may be blocked; Android cleartext is enabled **only in the debug manifest** (verified); macOS release entitlements include `network.client` (verified) | iOS dev may need `NSAllowsLocalNetworking` — **NOT RUN, unverified** | Add when iOS is first built |

**Mock/production safeguards checked and working:** `PersistenceSettings`, `AuthGuard` (also demands a ≥32-char signing key outside dev), `MedicationsGuard`, `AiGuard` (`AllowMockInProduction` fails start-up in Production), `PatientsGuard`, `ManufacturerReportsGuard` (Mock provider and demo seed refused; any provider other than `Disabled`/`Mock` refused because none exists), Guidance seed guard. **External AI** cannot run without `Provider=External` + `AllowExternalDataTransfer` + key + URL + model (code review + Phase 4 tests).

**Data freshness / missing data:** the patient layer stores per-category last-updated and stale-after days (`/freshness`), shown as "may be out of date / not recorded yet" on both clients; `PatientContext.LastUpdated` carries timestamps to AI consumers; missing information is stated, not guessed (unregistered medicines are flagged and excluded from interaction logic).

---

## 5. Manufacturer-reporting readiness

Verified against code (`ReportPayloadBuilder`, `ManufacturerReportService`, `ReportStateMachine`, tests):

| Requirement | Status |
|---|---|
| Medication, manufacturer, batch/lot, expiry, optional GTIN (typed, format-checked, never generated) | **Present.** *Brand* is not a separate payload field (only `ProductName`/`GenericName`) — **Low gap** |
| Event details | **Partly.** Issue type, severity, date, duration of use, screened description, age band, sex, other medicines (names only, opt-in). **Missing for a regulatory report:** seriousness criteria, outcome, action taken with the product, indication, dose/route/frequency of the suspect product, reporter type — **Medium gap** (also the fixed allow-list deliberately excludes conditions/allergies) |
| Payload minimisation / privacy filter | **Present**: fixed allow-list type (a test fails if a field is added), `FreeTextGuard` (now hardened, R-02). **Not anonymisation** (M-04) |
| Human review | **Present** (adverse events, severe/unknown severity, other medicines always; patient cannot review own report) |
| Consent | **Present and now complete at send time** (R-01). **Legal-basis check per country/manufacturer: not built** |
| Audit trail | **Present** (create, submit, review, block, send, acknowledge; no content in audit) |
| Payload hash | **Present** (SHA-256 of the frozen text, verified before every send; column is `text` because `jsonb` would reformat) |
| Idempotency / retries / acknowledgement | **Present** (`clientRequestId`, per-report idempotency key to the provider, claim + lease + back-off 1/5/30/120/720 min, 5 attempts, `Acknowledged` with external reference). Outbox runs **only on an admin call** |
| Separation of *suspected adverse event*, *suspected quality defect*, *confirmed defect* | **Not satisfied (Medium).** One enum (`AdverseEvent`, `AbnormalAppearanceOrPackaging`, `ApparentLackOfEffect`, `QualityProblem`, `Other`); nothing in the payload says the report is a *patient-reported, unconfirmed suspicion*; there is no "confirmed" concept (correct today — no confirmation process exists). **Proposed minimal change (not applied; schema/UX change):** add `category` (SuspectedAdverseEvent / SuspectedQualityDefect / Other) and a constant `basis: "patient-reported-unconfirmed"` to the payload, bump `schemaVersion` |
| Professional-originated reports | **Not built**: only the Patient role holds `manufacturer-report.create`; physicians/pharmacists can only review |
| MOCK acknowledgement vs real delivery | **Mostly accurate.** `IsMockDelivery`, `MOCK-ACK-` references and UI warnings exist; **but the status names `Sent`/`Acknowledged` are the same for Mock and real (M-09, Medium)** — only a flag and a UI note distinguish them. Recommend a `delivery` channel value (`mock`/`real`) in the status DTO and API |
| No real transmission in this review | **Confirmed**: the only provider is `MockManufacturerReportProvider`; no HTTP client exists for manufacturers |
| Insurers must not receive reports or unrestricted clinical data | **Confirmed**: the Integrations (insurance) module has no reference to ProductTrace/Patients/Guidance; the `InsuranceSharing` consent purpose exists but nothing reads it |

---

## 6. Proactive clinical-guidance readiness

| Prerequisite | Exists? | Evidence / gap |
|---|---|---|
| Authorised patient context (medications, allergies, conditions, symptoms, age band, sex) with timestamps and consent filtering | **Yes (partial)** | `PatientContext` with `LastUpdated`, `Excluded` categories, `ExternalProcessingConsented`; **missing:** structured doses (numeric/units), schedule/adherence summary, **laboratory results** (no lab model at all), vitals, trends |
| Medication knowledge with provenance, versioning, validation status, interactions with severity | **Yes** | Phase 4 models; all content is DEMO; no allergy-cross-reactivity or dose-range data |
| Backend-controlled, versioned **clinical guidelines / structured rules** | **No** | No rules engine, no guideline entity, no versioning of rules |
| **Independent deterministic safety layer** that an LLM cannot override | **No** | Not built. The only safeguard is `PatientMessagePolicy` (tone/content policy on already-composed text) and the fact that the Mock/LLM cannot write to patients today |
| Evidence-backed medication-risk assessment | **No** | Interaction records exist as reference data; nothing evaluates a patient against them |
| Predictive monitoring (validated) | **No** — and none may be claimed before validation | — |
| Alert entity: severity, evidence, uncertainty, freshness, delivery, acknowledgement, review, escalation, resolution | **Partly** | `GuidanceMessage` has level (4), lifecycle `Sent→Seen→Reviewed→Referred→Resolved`, patient + professional parts, `basisAndConfidence`; **missing:** evidence references, structured uncertainty/freshness fields, delivery channel/receipt, escalation timers, professional-facing alert inbox/endpoints (`guidance.professional.read` exists but no professional route) |
| Scoped access (role, care relationship, consent, law) | **Yes** | Same authorizer; guidance is visible only to the patient; professional view is not exposed yet; legal-requirement checks not built |
| Calm, non-alarmist patient language; facts vs possibilities; uncertainty; no unsupported probabilities | **Policy yes, producers no** | Six-part contract, tone policy (forbids scare words, diagnosis phrases, treatment orders, percentages, shouting, jargon), reviewed fa/en templates. **Templates are not clinically validated** |
| Emergencies not hidden | **Yes (by rule)** | `Urgent` level requires a non-empty urgent-signs part (policy test); `symptom.severe_followup` says to contact a professional the same day and gives emergency signs. No automatic emergency detection exists |

Conclusion: the *contract and access model* can carry proactive guidance; the *clinical engine* (rules, deterministic safety layer, labs, alert delivery/escalation) is **entirely missing** and must be designed in its own phase. No prediction model was implemented here.

---

## 7. Test and build matrix (actual results)

Environment: Linux container, .NET 10, PostgreSQL 16 installed locally (scratch databases), Node, Flutter 3.47 (Linux), Chromium. **No Docker daemon.**

| Check | Status | Result |
|---|---|---|
| `node design/build.mjs --check`, `node security/build.mjs --check` | **PASS** | up to date |
| `dotnet build MedSmarter.sln -c Release -warnaserror` | **PASS** | 0 warnings, 0 errors |
| .NET suites in memory, **3 consecutive runs** | **PASS** | Architecture 5 · BuildingBlocks 4 · Api 8 · Patients 214 passed / 3 skipped (PG-only) · Knowledge 165 passed / 15 skipped (PG-only) · Security 133 — identical in all three runs |
| Knowledge.Tests on scratch PostgreSQL | **PASS** | 180 / 180 |
| Security.Tests with the PostgreSQL variable set | **PASS** | 133 / 133 |
| Patients.Tests on scratch PostgreSQL, **before** the harness fix | **FAIL intermittently** | 2 of 6 full runs failed with 2 tests each (harness cleanup error, see T-01); all the other runs 217 / 217 |
| Patients.Tests on scratch PostgreSQL, repeated **after** the harness fix | **PASS** | 5 consecutive full runs, 217 / 217 each (a sample of 5 is evidence, not proof, that the intermittency is gone); Knowledge.Tests on PostgreSQL 180 / 180 and Patients.Tests in memory 214 passed / 3 skipped re-run after the fixture change |
| EF `has-pending-model-changes` × 7 contexts | **PASS** | no drift |
| Fresh database + `--migrate-and-exit` | **PASS** | 7 schemas created |
| Live API on PostgreSQL: IDOR, admin routes as patient, alg-none token, bad JSON, 5 000-char text, zero-width e-mail, 10 MB body, wrong method, injection-like search, limit/offset abuse, audit chain verify | **PASS** | 403 / 403 / 401 / 400 `body.invalid` / 400 `text.too_long` / 400 `text.looks_identifying` / 400 / 405 / 200 empty / 400 `limit.range` / `{"intact":true}`; API log contained no token, name, e-mail or body |
| Web: eslint, `tsc --noEmit`, `vite build` | **PASS** | clean |
| Web vitest, 3 consecutive runs | **PASS** | 199 / 199 each time |
| Web browser QA (axe, RTL, overflow) against real API | **PASS earlier, NOT re-run in this review** | 360 checks, 0 failures at the end of Phase 5; review changes did not touch the web |
| Flutter `analyze` | **PASS** | no issues |
| Flutter `test`, 2 consecutive runs | **PASS** | 109 / 109 each time |
| `flutter build linux --debug` | **PASS earlier in Phase 5, NOT re-run** | built |
| `MedSmarter.IntegrationTests` (3 tests) | **NOT RUN** | skipped (need `MEDSMARTER_IT=1` + Docker stack) |
| Docker image build / `docker compose up` | **NOT RUN** | no Docker daemon |
| GitHub Actions CI | **NOT RUN** | cannot execute here; the workflow was extended (7 EF drift checks, PostgreSQL suites) but is unverified until it runs |
| Windows, macOS, Android, iOS builds/tests; Firefox, Safari | **NOT RUN** | no toolchain/browsers; **no compatibility is claimed** |

**T-01 (test reliability, Medium) — root cause found and fixed.** Intermittent failures appeared in roughly one of three full PostgreSQL runs of `Patients.Tests` (2 tests each time, a *different* pair each time: e.g. `ProductTests.Batch_numbers_have_a_checked_format`, `ReportTests.Another_patients_report_and_product_cannot_be_used`). The saved log shows the cause was **not** an assertion: `Npgsql.PostgresException 42501: permission denied to terminate process`, thrown by the test harness when it dropped the scratch database with `DROP DATABASE … WITH (FORCE)` while a background PostgreSQL worker owned by another role (e.g. autovacuum) was attached to it. The product behaviour under test was not involved. Fix (tests only): `DropQuietly` retries and, if it still fails, defers the drop to process exit (both `Patients.Tests` and `Knowledge.Tests` fixtures). Result after the fix: 5 of 5 full runs passed (§7).

---

## 8. Fixed defects and changed files

| File | Change |
|---|---|
| `src/Modules/ProductTrace/MedSmarter.Modules.ProductTrace/ManufacturerReportService.cs` | R-01: send-time consent check covers every scope present in the frozen payload |
| `src/BuildingBlocks/MedSmarter.BuildingBlocks/FreeTextGuard.cs` | R-02: invisible format characters ignored when screening |
| `tests/MedSmarter.Patients.Tests/ReviewTests.cs` (new) | route inventory test; 6 free-text bypass vectors; ordinary text still accepted |
| `tests/MedSmarter.Patients.Tests/ReportTests.cs` | regression test for R-01 (proved to fail without the fix) |
| `tests/MedSmarter.Patients.Tests/Fixture.cs`, `tests/MedSmarter.Knowledge.Tests/Fixture.cs` | T-01: scratch-database cleanup retries and never fails a test |
| `tests/MedSmarter.Knowledge.Tests/ApiTests.cs` | (committed earlier in Phase 5) API fixture migrates all schemas on PostgreSQL |
| `docs/phase3/*`, `docs/phase4/*` (7 files) | "Update after Phase 5" notes where statements became false |
| `docs/phase5/03…`, `04…` | consent re-check scope, "screening aid, not anonymisation", `consent_event` not DB-protected |
| `docs/repository-review-after-phase5/REPORT.md` (new) | this report |

---

## 9. Remaining blockers and recommended priorities

1. **H-01** real identity provider + persistent identity/session store (blocks any real use).
2. **Privacy/legal:** DPIA and a documented human-review procedure for manufacturer reports; legal basis per manufacturer; decide date coarsening (M-04, M-05).
3. **Clinical safety layer** (rules + deterministic checks + labs + alert delivery/escalation) — designed and validated before any proactive guidance is produced; clinical review of templates and Persian wording.
4. **Manufacturer-report schema**: category + "unconfirmed" basis, regulatory fields, professional-originated reports, explicit `delivery: mock|real` (M-09, §5).
5. **External AI**: question screening + consent gate (M-01) before any provider is approved.
6. **Audit**: least-privilege DB roles, head anchoring, bounded verification (M-02, M-03); transactional outbox pattern for audit (A-01).
7. **Reliability:** find the cause of T-01; run Docker integration tests and CI; build the other platforms.
8. **Scale items:** search paging in the database, N+1 authorization, directory exposure (A-03, A-04, M-06), remove `/analytics/summary` placeholder (M-07), per-IP login limits (M-08).

---

## 10. Local-development readiness and explicit production limitations

**Ready for local development:** yes — `make api-memory` + `make web-run-api` (no database), or PostgreSQL via `make env db-up` + `make api`; Flutter with `--dart-define=API_BASE_URL`; no API key, no internet needed for any test.

**Not production-ready — explicit limitations:** mock login and in-memory identity; Mock manufacturer delivery and Mock AI/insurance; no real identity, registry or manufacturer integration; no clinical engine; guidance text not clinically validated; audit chain tamper-evident only; free-text filtering is not anonymisation; no TLS/HSTS/hosting/backup/monitoring; no background worker; Windows/macOS/Android/iOS/Firefox/Safari untested; CI unexecuted. **Do not use with real patient data.**

---

## 11. Confirmation

* No server, domain, paid API or cloud service was purchased or activated.
* No real external integration was made; the only manufacturer provider is the Mock; the AI provider stayed Mock/disabled; no network call left the machine (local PostgreSQL only).
* All data used were synthetic (DEMO / MOCK / NOT FOR CLINICAL USE).
* **No Phase 6 functionality was implemented.** Work stopped for review.
