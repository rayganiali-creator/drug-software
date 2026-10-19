# Roadmap — proposed Phases 16–20

**Status: the plan below is a proposal; the owner has approved only what is stated per phase.** Phase 16 (stated scope), the first subset of Phase 17, Phase 18 (owner-directed scope, `docs/phase18/README.md`), the **reliability part of Phase 19** (`docs/phase19/README.md`) and **Phase 20, delivery and monitoring operability and fairness** (`docs/phase20/README.md`) are implemented and recorded in the repository history. What the original Phase 19 and 20 text listed beyond that (alert prioritisation, deduplication across rule sets, per-category freshness, emergency delivery, retention, offline-queue encryption and the like) is **not done** and is marked below. Phase 21 (integrated demo) is a planning category, grounded in evidence from the repository, and must be re-scoped before work starts. The older plan in `docs/phase0/10-roadmap.md` is a different numbering (see its note); this file continues the *delivered* numbering (Phases 0–15 are complete).

Global rules for every phase: synthetic data only; no paid server, domain, API or AI service; no outbound network call in development or tests; mock/disabled adapters stay mock/disabled; no clinical rule becomes active without a named reviewer and a validated source; no claim of clinical validation, regulatory compliance, anonymity or production readiness without evidence.

---

## Phase 16 — Clinical Safety and Evidence Management (implemented)

**Objective.** Make the evidence behind every assistant answer traceable and make weak or conflicting evidence impossible to hide, without any new clinical content.

**Reused (already existed).** Evidence retrieval with source name/version/publisher/received-date/validation (`MedicationEvidenceRetriever`, `EvidenceItem`, `EvidenceSource`); stale / undated / not-validated / incomplete / truncated flags; interaction-severity conflict detection across sources; `EvidenceQuality`; the fixed backend answers for emergency, rule-override, medicine-change and diagnosis requests; the deterministic engine in `ClinicalRules` (never calls a model, `NoApprovedCoverage` when no rule is active); `AnswerSafetyPolicy`.

**Gaps found in the code (evidence).**
1. An evidence item could not be traced to the stored record: `EvidenceItem` had source and medication ids only (`Evidence.cs`, `KnowledgeStatementView` had no id).
2. A severity disagreement was a "conflict" only across ≥ 2 sources (`Evidence.cs`), so one source contradicting itself was silently resolved.
3. Weaknesses were reported only as side-channel metadata (`Limitations`); the generated text (and the model request, which carries no limitation information) could present one side with no hedge.
4. `AnswerSafetyPolicy.Check` rejected unknown citations but accepted an answer with **no** citation at all.

**Delivered.** (a) `RecordId` + `RevisionId` on knowledge views and evidence items (optional trailing fields, no schema change); (b) same-source severity disagreement is a conflict; (c) a backend-written caveat (fa/en, driven only by limitation codes) placed in front of every generated answer whose evidence is conflicting, incomplete, not validated, stale or truncated — the demo-provider label stays first; (d) `uncited_answer` violation: a generated answer must cite at least one evidence id, otherwise it is withheld and the sources are shown; (e) tests, including a structural test that the AI module and the rule engine do not reference each other.

**Not done / blocked.** A real *publication / effective date* of a source is still not recorded (`KnowledgeSource` has only `ReceivedAt`); recording it needs a schema migration and a decision on who enters it and from what. "Stale" therefore still means "received long ago". No clinical rule was added or approved. Persian caveat wording is unreviewed. Conflicts other than interaction severity are not detected (needs structured data, not free text).

**Acceptance.** Knowledge.Tests green incl. `Phase16Tests`; no existing test weakened except one that encoded "uncited calm text passes" (changed deliberately, see `docs/phase16/README.md`).

---

## Phase 17 — Patient, Pharmacist and Physician workflows (first subset implemented; the rest still proposed)

See `docs/phase17/README.md` for the audit, the subset done and what remains.

**Objective.** Close the gap between workflows that exist as real backend features and screens that still run on in-browser mock data.

**Reusable.** Patient records, care relationships, consent, ADR records (Phase 9/12), pharmacy safety (Phase 10), professional pages with real routes (`ProPages`, adherence, safety, rules, ADR queue).

**Evidence of missing work.** The web dashboards/pages `Physician.tsx`, `Pharmacist.tsx`, `Pharmacy.tsx`, `PatientHome.tsx`, `Medications.tsx`, `Profile.tsx`, `CheckIn.tsx` and the legacy `Industry.tsx` import the mock service layer (`useServices`/`useAsync`); Flutter `home`, `checkin`, `profile`, `assistant` use `MockServices` (`mobile/lib/features/*`). Phase 9/10 report lists: pharmacy UI uses the first organization only, product link by typed ids, no independent-reviewer rule for pharmacy records. Which of these screens are fully mock versus partly wired must be verified screen by screen first.

**Dependencies / decisions.** Which dashboard widgets are worth wiring; role-boundary decisions for follow-up messages between pharmacist and patient; reviewer-registry decision (who may be a reviewer).

**Tests.** Role/route audit (extends `phase13.test.tsx`), per-screen test that no screen shows DEMO data as real, API-level boundary tests per role.

**Exclusions / blockers.** No prescribing, dispensing authority, e-prescription or national-system integration (Phase 11 adapters stay disabled/mock); no clinical decision support beyond approved rules.

---

## Phase 18 — Complete and safe access to the adverse-event queue (owner-directed; implemented)

See `docs/phase18/README.md`. Scope was set by the owner after the Phase 17 assessment: the reviewer's queue is limited to the patients the reviewer may read BEFORE it is read, counted or cut into pages, and is paged with a cursor; web states, header contract, dashboard count, tests and a read-only review of stored patient workflows. Everything else is out of scope. The monitoring and notification work that was proposed here moved to Phase 19; the two phases after it each moved one number down (relocation only, nothing else in them changed).

---

## Phase 19 — Medication monitoring and preventive notifications (reliability part implemented; the rest still proposed)

**Delivered (see `docs/phase19/README.md`; verified by the Phase 19 tests: 25, of which 4 need PostgreSQL).** Monitoring ownership/cancellation/lease/audit correctness (`MONITORING_RESULT_DISCARDED`); send-time eligibility from existing facts (resolved message or missing patient ends a delivery, inactive patient defers it); an idempotency key and explicit handling of a failed outcome write; at most one open message per finding (partial unique index, migration `AddOpenFindingUniqueness`). Delivery stays **at-least-once**; the only channel is simulated.

**Still proposed / deferred (not implemented):** alert prioritisation across findings, deduplication across rule sets, per-category freshness thresholds, any real channel, **consent-gated delivery (deferred by explicit decision, option c)**, an emergency path (blocked on authoritative criteria), operator endpoints and worker heartbeat.

**Original objective.** Better rule-based monitoring behaviour and a clearer notification lifecycle.

**Reusable.** Scheduled reassessment job with lease/ownership (`MonitoringService`), guidance delivery state machine and optimistic claim (Phase 8/14), freshness handling per input category (`docs/phase7/04`), reactivation by record version.

**Evidence of missing work.** `docs/phase8/08-phase-report.md`: no real channel, **no emergency delivery path**, no sourced emergency criteria; the routine worker must not be the only urgent route. Alert prioritisation across several open findings, deduplication across rule sets, and per-category freshness thresholds are not described anywhere as implemented (to verify before scoping).

**Dependencies / decisions.** Approved, sourced clinical rules and a named reviewer (without them the engine returns `NoApprovedCoverage`); an approved real channel (paid/external → blocked); emergency criteria from an authoritative source.

**Tests.** Two-process worker test on PostgreSQL, deduplication and ordering tests, simulated-vs-real labelling.

**Exclusions.** No invented emergency criteria; no real push/SMS/e-mail; no Redis/Kafka.

---

## Phase 20 — Delivery and monitoring operability and fairness (implemented)

See `docs/phase20/README.md`. Owner-approved scope after the post-Phase-19 review: counts-only pass summary logs for both workers; a rejected pre-send database write no longer aborts a delivery pass; inactive-patient deferral grows progressively to at most 24 hours with a fair per-pass quota; stale `patient.inactive` code cleared when an attempt starts; neutral web and mobile wording for deliveries that were never attempted (`message.resolved`, `patient.missing`, `channel.unavailable`, `message.missing`); CI migration-drift check extended to all ten database contexts; a guard test for the open-finding index predicate. No migration, no new endpoint, no real channel, no consent change. **Not done / deferred:** operator endpoints and heartbeat, transactional audit (audit is still written after commit), terminal retry cap, anything below.

---

## Reliability, synchronization, security and privacy (still proposed; this was labelled "Phase 20" in the earlier plan, and the delivered Phase 20 above is a different, smaller scope)

**Objective.** Harden what is already built.

**Reusable.** Hash-chained audit, uniform 403, route-inventory test, offline dose-report queue (Phase 14), optimistic claims, PostgreSQL scratch-database test harness.

**Evidence of missing work.** `docs/post-phase-15-stabilization` and `docs/industry-privacy-review`: offline queue unencrypted (no offline secure-storage package; key custody undecided); only dose reports queue; no background sync; no retention/deletion policy for published industry aggregates or ADR records (matrix N-01); industry differencing risk reduced not removed; Phase 5 review items (in-memory identity store, per-instance rate limit, audit written after commit) carried; no dedicated EF model-drift test; no multi-process test.

**Dependencies / decisions.** Privacy expert (k, noise, DPIA), legal basis (Q-14), key-custody decision, retention periods (no legal period is asserted here).

**Tests.** Drift test, multi-process PostgreSQL tests, retention/deletion tests once a policy exists, extended route/permission inventory.

**Exclusions.** No real identity provider, no cloud services.

---

## Phase 21 — Integrated demonstration and readiness assessment (proposed; was Phase 20)

**Objective.** One coherent, honest synthetic-data demo with verified instructions and a readiness report.

**Reusable.** Demo accounts and seeders (`demo-*`), docker-free local run documented per phase, existing Playwright QA scripts (`web/scripts`), DEMO labelling throughout.

**Evidence of missing work.** No single end-to-end scenario script covers patient → ADR → pharmacist/physician → notification → industry aggregate; Linux is the only verified Flutter build; `IntegrationTests` (Docker) were never run in this environment; `.github/workflows/ci.yml` exists but was not reviewed in this roadmap pass.

**Dependencies.** Phases 17–19 decisions; Docker availability for the integration tests.

**Tests.** Scripted end-to-end scenarios (API + web), documented run-through on a clean checkout, screenshot QA with axe.

**Exclusions.** Not a clinical validation, not a regulatory submission, not a pilot, no real patient data.

---

## Decisions the owner must make before Phases 17–20

1. Approve, reorder or drop Phases 17–20 (they are proposals).
2. Whether to record a publication/effective date for sources (migration + data-entry rule).
3. Who may review rules and Persian wording (named reviewers) and which validated sources the first approved rules rest on.
4. Whether answers with an uncited model reply should be withheld (Phase 16 does) or softened.
5. Privacy/legal inputs listed in `docs/industry-privacy-review/README.md` §7.
