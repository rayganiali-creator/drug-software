# Phase 4 — final report

Status: **LOCAL / DEV / MOCK**. Nothing purchased, deployed or activated. No paid API, VPS, domain or hosting.

1. **Files/modules**: new modules `Medications(.Contracts)`, `AI(.Contracts)`, `Integrations(.Contracts)`; `Knowledge/KnowledgeEndpoints.cs` and `Program.cs` in the host; tests `MedSmarter.Knowledge.Tests`; web `src/knowledge`, `features/knowledge`; Flutter `lib/api`, `lib/features/drugs`, desktop folders; catalogue (+3 permissions); CI, Makefile, `.env.example`; `docs/phase4/`.
2. **Data model/migrations**: 21 domain concepts; schema `medications` (18 tables) with migration `InitialMedicationsSchema`, verified on a throwaway local PostgreSQL 16. The API serves an in-memory repository (PostgreSQL repository not built).
3. **Endpoints**: see [03](03-endpoints.md) (search, detail, admin write with `If-Match`, sources, assistant, document export), all behind `perm:` policies.
4. **Web & Flutter**: drug search/detail on the real API with Loading/Empty/Error/not-found and DEMO labels; master-detail ≥1024px on web.
5. **Desktop/OS**: Linux debug build run under Xvfb against the real API; Windows/macOS folders generated, not built; Android/iOS not built here. See [10](10-platform-support.md).
6. **AI**: Disabled default, MockAIProvider working, ExternalAIProvider adapter (fake-handler tested only), LocalAIProvider port only; no secrets.
7. **Insurance**: contracts, in-memory service, fictional mock; no endpoints, no real insurer.
8. **Tests**: see below.
9. **Real / experimental / not ready**: [11](11-real-vs-mock.md).
10. **Run locally**: [04](04-local-setup.md).
11. **Dependencies/limits**: .NET 10, Node 22, Flutter 3.47.5; Linux desktop needs `libgtk-3-dev`; Xvfb for headless runs; no Windows/macOS/Android/iOS toolchains here.
12. **Possible future costs (nothing bought)**: hosting for API/DB, a managed PostgreSQL, an LLM API (per-token) or GPU hardware for a local model, licences for drug-knowledge data, Apple/Google developer accounts, code-signing certificates, domain/TLS. None activated.
13. **Remaining problems**: [12](12-limitations-and-risks.md) (priority: PostgreSQL repository, clinical data governance, untested OS targets, external AI evaluation).

## Test results (this machine, 2026-10-09)

| Suite | Result |
|---|---|
| `dotnet build -c Release -warnaserror` | 0 warnings, 0 errors |
| BuildingBlocks 4 · Architecture 5 · Security 133 · Api.Tests 8 | all passed |
| Knowledge.Tests | 162 passed, 9 skipped (PostgreSQL, opt-in) → **171/171 passed with `MEDSMARTER_PG_TEST` against local PostgreSQL 16** |
| IntegrationTests | 3 skipped (need the Docker stack; not run) |
| Web: eslint, tsc, vitest | clean; 178/178 |
| Web browser QA (`qa.mjs`) | no problems; axe 0 violations over 225 page×theme combos |
| `flow.mjs`, `knowledge-qa.mjs` | 0 failures (144 checks, axe 0) — Chromium only |
| Flutter `analyze` / `test` | no issues / 91 passed |
| `design/build.mjs --check`, `security/build.mjs --check` | up to date |

Not tested: Firefox/Safari, Windows, macOS, Android, iOS, any real external AI/insurance service.
