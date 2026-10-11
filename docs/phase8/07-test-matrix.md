# Phase 8 — Test matrix and results

All runs are local, with synthetic data, and no external service. Counts are from the final verification run.

| Suite | Command (abridged) | Result |
|---|---|---|
| Build | `dotnet build` (analyzers as errors) | 0 warnings, 0 errors |
| .NET Architecture | `tests/MedSmarter.Architecture.Tests` | 6 passed |
| .NET BuildingBlocks | `tests/MedSmarter.BuildingBlocks.Tests` | 4 passed |
| .NET Api | `tests/MedSmarter.Api.Tests` | 8 passed |
| .NET Knowledge | `tests/MedSmarter.Knowledge.Tests` | 290 passed, 15 skipped (PostgreSQL-only tests; run separately below) |
| .NET Patients (incl. ClinicalRules, Guidance, Phase 8) | `tests/MedSmarter.Patients.Tests` | 368 passed, 4 skipped (PostgreSQL-only; run separately) |
| .NET Security | `tests/MedSmarter.Security.Tests` | 133 passed |
| .NET IntegrationTests | `tests/MedSmarter.IntegrationTests` | 3 skipped — **not run**: they need the Docker compose stack, which is not provisioned without approval |
| PostgreSQL Patients | `MEDSMARTER_PG_TEST=… dotnet test tests/MedSmarter.Patients.Tests` | 373 expected/passed (372 in the full run, plus the new Phase 8 PostgreSQL test run on its own and passing) |
| PostgreSQL Knowledge persistence | same variable | 8 / 8 passed |
| PostgreSQL schema | same variable | 9 / 9 passed |
| EF migration drift | `dotnet ef migrations has-pending-model-changes` for all 7 contexts | "No changes" for each |
| Fresh database | migrate-and-exit on an empty PostgreSQL database | 7 migrators applied; `monitoring_job`, `guidance_delivery` and `intake.source` present |
| Web lint / types | `eslint`, `tsc -b` | clean |
| Web unit | `vitest run` | 278 tests, 13 files, all passed |
| Web build | `vite build` | OK |
| Web browser QA | `scripts/phase8-qa.mjs` (Chromium, axe, en/fa × light/dark × desktop/phone, real API on PostgreSQL, workers enabled) | 219 checks passed, 0 failed (includes axe). It found one real defect, fixed: the long delivery-status badge overflowed on a narrow Persian (RTL) screen |
| Flutter | `flutter analyze`, `flutter test` | analyze clean; 157 tests passed |
| Generators | `node design/build.mjs --check`, `node security/build.mjs --check` | up to date |

## Areas covered by the new tests

Adherence semantics and idempotent replay; instruction gaps; no-slot Missed rejected; ownership/relationship/consent on adherence summary; fingerprint-based change detection; no duplicate open alerts; record-level reactivation; resolved-finding policy; job lease, optimistic claim, retry/backoff, exhaustion, recovery after interrupted run; delivery state machine and the "recovery never promotes to Confirmed" rule; draft editing and author-only edits; approval blockers; server-enforced author/reviewer separation; demonstration rules unable to become active; endpoint authorization; web and Flutter rendering of every state, including Persian.

## Not run / not established

Docker/compose integration tests; any real notification channel; any load or multi-instance test; any clinical validation.
