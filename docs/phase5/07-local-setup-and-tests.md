# 07 — Running locally and running the tests (no API key, no internet)

## Fastest way: no database at all
```bash
make api-memory            # API on http://localhost:5080, in-memory store, fictional DEMO data, Mock providers
make web-run-api           # web (VITE_AUTH_MODE=api), http://localhost:5173 — sign in with a demo account
cd mobile && flutter run -d linux --dart-define=API_BASE_URL=http://localhost:5080   # or -d chrome / an emulator
```
`Persistence:Provider=InMemory` is **refused** outside Development/Testing; data is lost on restart.

## With PostgreSQL (the default provider)
```bash
make env && make db-up                         # throw-away local passwords; only PostgreSQL in Docker
set -a; . ./.env; set +a
Persistence__MigrateOnStartup=true make api    # dev only: applies all migrations at start-up
# elsewhere: dotnet run --project src/Host/MedSmarter.Api -- --migrate-and-exit
```
Without Docker, any local PostgreSQL 16 works (`pg_trgm` extension needed): create a database, set `ConnectionStrings__Postgres`.
The browser needs the web origin allowed: `Cors__AllowedOrigins__0=http://localhost:5173`.

## Demo accounts (fictional, no passwords, DevelopmentMock only)
`demo-patient` (Sara, has medicines, batches, a report, messages), `demo-patient-2`, `demo-physician`, `demo-pharmacist`, `demo-system-admin`, `demo-content-manager`,
`demo-content-reviewer`, … (list: `GET /auth/demo-accounts`).

## Tests
| Command | What |
|---|---|
| `dotnet test MedSmarter.sln` | all .NET suites on the in-memory stores (no database, no network) |
| `make test-pg` (needs `MEDSMARTER_PG_TEST`) | the same Patients/Knowledge suites on a **scratch PostgreSQL** (a template database is migrated once, every test gets a clone) — covers triggers, constraints, concurrency, outbox claim, hash chain |
| `cd web && npm test && npm run lint && npx tsc --noEmit && npm run build` | web unit/page tests (vitest), lint, types, build |
| `cd web && node scripts/records-qa.mjs --base http://localhost:4174` | browser QA against the **real API**: sign in, open each new page, content, RTL, overflow, axe WCAG 2.2 AA, screenshots (see [08](08-platform-status.md)) |
| `cd mobile && flutter analyze && flutter test` | Flutter analysis + widget tests (fake HTTP client, no network) |
| `node design/build.mjs --check`, `node security/build.mjs --check` | generated i18n/design/permission artefacts are up to date |
| `dotnet ef migrations has-pending-model-changes …` (7 contexts, in CI) | model and migrations agree |

Tests that need Docker (`tests/MedSmarter.IntegrationTests`) are skipped without `MEDSMARTER_IT=1`.
