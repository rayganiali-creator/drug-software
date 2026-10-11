# 09 — Test matrix and results

Local only, synthetic data, **no outbound network call in any test**. Scratch PostgreSQL databases are local and were dropped afterwards.

| Suite | Command (abridged) | Result |
|---|---|---|
| Build | `dotnet build` (analyzers as errors) | 0 warnings, 0 errors |
| Architecture | `tests/MedSmarter.ArchitectureTests` | 6 passed |
| BuildingBlocks | `tests/MedSmarter.BuildingBlocks.Tests` | 4 passed |
| Api | `tests/MedSmarter.Api.Tests` | 8 passed |
| Knowledge (in memory) | `tests/MedSmarter.Knowledge.Tests` | 290 passed, 15 skipped (PostgreSQL-only) |
| **Patients (in memory)** | `tests/MedSmarter.Patients.Tests` | **405 passed, 6 skipped** (PostgreSQL-only) — includes Phase 4–8 regressions, the Phase 5 manufacturer-report consent and data-minimisation tests, and **37 new Phase 9 tests that run in memory** |
| Security | `tests/MedSmarter.Security.Tests` | 133 passed |
| IntegrationTests | `tests/MedSmarter.IntegrationTests` | **3 skipped — not run**: they need the Docker compose stack, which was not provisioned |
| **Patients on PostgreSQL** | `MEDSMARTER_PG_TEST=… dotnet test tests/MedSmarter.Patients.Tests` | **411 passed, 0 skipped** (includes the 1 new PostgreSQL-only Phase 9 test: persistence across a restart, unique request id) |
| Knowledge on PostgreSQL | same variable, pointing at a freshly migrated scratch DB | 305 passed, 0 skipped |
| EF drift | `dotnet ef migrations has-pending-model-changes` × 8 contexts (Adr, Audit, ClinicalRules, Consent, Guidance, Medications, Patients, ProductTrace) | "No changes" for all 8 |
| Fresh database | `--migrate-and-exit` on an empty PostgreSQL DB | 8 migrators applied; `adr.adverse_event` (17 columns) and `adr.adverse_event_entry` present |
| Web lint / types | `eslint src scripts`, `tsc -b` | clean |
| Web unit | `vitest run` | **288 tests, 14 files, all passed** (10 new in `phase9.test.tsx`) |
| Web build | `vite build` | OK |
| Browser QA | `web/scripts/phase9-qa.mjs` (Chromium, axe, en/fa × light/dark × desktop/phone, real local API on PostgreSQL) | **178 checks passed, 0 failed.** It found one real defect (a select with a long option overflowed a 390 px screen), fixed |
| Flutter | `flutter analyze`, `flutter test` | analyze clean; **157 tests passed** (no Flutter UI was added; access catalog and strings were regenerated) |
| Generators | `node design/build.mjs --check`, `node security/build.mjs --check` | up to date |

## What the new tests cover
**Service (`Phase9Tests`)** default Unclassified/Submitted/NotSubmitted; professional-entered label; category set is exactly the five defined terms + Unclassified (no quality/effect); definitions cite supplementary sources, none a lecture slide, Persian awaiting review; **no seriousness/causality/E2B/MedDRA/deadline names anywhere in the contract**; **no reference to Integrations/ProductTrace/HTTP**; the original never changes after follow-ups, supplements, assessments and classification; supplements fill only unknowns; unknown medicines stay unknown, another patient's medication is refused, a removed link becomes `LinkedUnavailable` and is flagged in the assistance; classification is professional-only, reasoned, reversible, versioned, history preserved; status machine, reasons for closing/reopening, patient answer reopens review; voices separated (patient view hides notes, assessments, category, author ids); assistance is a labelled template, not stored, asserts nothing (banned phrases); validation codes; idempotent retries; audit holds no report text; cross-patient lists; PostgreSQL persistence.
**HTTP (`Phase9ApiTests`)** 401 on all 11 routes; owner and consenting professional allowed; physician without relationship, relationship without consent, pharmacist without consent, other patient, industry, researcher, system admin all refused on read, list and create; missing vs hidden record identical; professional entry labelled; review actions refused for a patient; patient view hides professional content and category; stale version → 409; assistance permissions; queue filtered per patient and empty (not error) for an unrelated physician.
**Web (`phase9.test.tsx`)** notices, no-emergency note, no category for patients, form never fills unknown values, "not connected" state, original/unknown/not-classified display, no "send/submit/report to" control, classification needs a reason and sends the version, definitions labelled supplementary, assistance labelled, patient view without professional panel, already-known values not re-offered, conflict explained in words, Persian banner and no raw keys, queue request.

## Not run / not established
Docker integration tests; any real notification or reporting channel (none exists); load or multi-instance tests; clinical validation; Flutter ADR screens (none exist).
