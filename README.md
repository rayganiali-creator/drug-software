# AI MedSmarter

Healthcare/pharmaceutical platform (patients, physicians, pharmacists, pharmacies, industry, AI assistant).
Architecture and requirements: [`docs/phase0/`](docs/phase0/README.md). Design system and UI/UX: [`docs/phase2/`](docs/phase2/README.md).
Identity, authentication, authorization, consent and audit: [`docs/phase3/`](docs/phase3/README.md).
Medication knowledge core, economical AI architecture and platform readiness: [`docs/phase4/`](docs/phase4/README.md).
Patient record, medicines taken, batch/lot tracing, manufacturer reports (Mock delivery) and safe anticipatory guidance: [`docs/phase5/`](docs/phase5/README.md).
AI assistant, evidence-grounded retrieval and the safe provider architecture: [`docs/phase6/`](docs/phase6/README.md).
Current state: **Phase 6 – source-grounded medication assistant (Mock provider by default, external AI off and fail-closed, structured answer contract, Web + Flutter pages); no real model was run, nothing bought**. (Earlier: Phase 5 – patient record + medicines taken + batch/lot records + consent-gated, de-identified manufacturer reports (Mock delivery only) + guidance contract (fa/en), PostgreSQL persistence with a persistent hash-chained audit, Web + Flutter pages; nothing deployed or bought, no paid API**. (Earlier: Phase 4 – medication knowledge core (fictional DEMO data, in-memory repository, PostgreSQL schema prepared), AI provider architecture (Mock by default, no API key needed), insurance architecture (mock only), Web + Flutter drug reference, Flutter desktop folders**. Nothing is deployed or purchased; no paid API is used.

## Layout
| Path | What | Stack |
|---|---|---|
| `src/Host/MedSmarter.Api` | Composition root, health, logging, `/version` | ASP.NET Core (.NET 10) |
| `src/BuildingBlocks/*` | `IModule`, `Result`, outbox/inbox, Postgres/Redis/OpenSearch/Kafka wiring, EF migrations | C# |
| `src/Modules/<Name>/` | 24 bounded modules (`.Contracts` + implementation; Patients, ProductTrace, Guidance, Consent, Audit, Medications… have code) | C# |
| `ai/` | AI service (health/config/logging only; no LLM yet) | Python 3.11+, FastAPI |
| `web/` | Web app: landing page, design-system gallery, patient / physician / pharmacist / pharmacy / industry UIs (Mock data) | React 19, TypeScript, Vite |
| `mobile/` | Patient app for Android + iOS (one codebase, Mock data) | Flutter/Dart |
| `security/` | Single source of truth for roles, permissions and FICTIONAL demo identities → generated C#/TS/Dart (`node security/build.mjs`) | Node |
| `design/` | Single source of truth: tokens, icons, i18n strings, demo data → generated for web and Flutter (`node design/build.mjs`) | Node |
| `tests/` | Unit, architecture (module boundaries), API host, env-gated integration tests | xUnit |
| `docker-compose.yml` | Postgres 16, Redis 7, OpenSearch 2.19, Kafka 3.9 (KRaft) + app containers (`--profile app`) | Docker |
| `.github/workflows/ci.yml` | CI | GitHub Actions |

## How to run
Prereqs: Docker (compose v2), .NET 10 SDK, Python ≥3.11, Node 22, Flutter stable.

```bash
make env          # creates .env with random LOCAL-ONLY passwords (git-ignored)
make infra-up     # postgres, redis, opensearch, kafka
make migrate      # applies EF Core migrations (schema "platform")
make api          # http://localhost:5080  (/health/live, /health/ready, /version)

make ai-install && make ai-run   # http://localhost:8000
make web-install && make web-run # http://localhost:3000
cd mobile && flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5080

make up           # alternative: everything in containers (builds images)
```
Drug reference without Docker: `make web-run-api` (web against the local API) – see [`docs/phase4/04-local-setup.md`](docs/phase4/04-local-setup.md).
Readiness (`/health/ready`) reports `postgres`, `redis`, `opensearch`, `kafka` as Healthy/Unhealthy (HTTP 200/503) and
never includes exception text. Liveness (`/health/live`) has no dependencies.

## How to test
```bash
node design/build.mjs --check   # generated design artefacts up to date + WCAG contrast rules
node security/build.mjs --check # access catalog rules + generated permission code up to date
make test                # dotnet (unit+architecture+host), pytest, vitest, flutter test — no infrastructure needed
# web QA (needs Chromium): cd web && npm run build && npm run preview & node scripts/qa.mjs && node scripts/flow.mjs
make infra-up migrate && make test-integration   # real Postgres/Redis (+ readiness of the whole stack)
make lint
```

## Sign-in (development only)
The login screen offers **fictional demo accounts without passwords** (`Auth__Mode=DevelopmentMock`). The API refuses that mode unless
`ASPNETCORE_ENVIRONMENT` is `Development`/`Testing`. Web default is in-browser demo accounts; `VITE_AUTH_MODE=api` signs in through the API.
```bash
make api    # runs with ASPNETCORE_ENVIRONMENT=Development (local only)
cd web && VITE_AUTH_MODE=api VITE_API_BASE_URL=http://localhost:5080 npm run dev
```

## Configuration
All settings are environment variables (see `.env.example`); no secret is stored in the repo or has a default.
`ConnectionStrings__Postgres`, `Redis__ConnectionString`, `OpenSearch__Uri`, `Kafka__BootstrapServers`,
`Cors__AllowedOrigins__0` (API); `AI_OPENSEARCH_URI`, `AI_KAFKA_BOOTSTRAP_SERVERS` (AI). Missing values fail startup.
Migrations are never applied by the API at runtime: run `--migrate-and-exit` (the `migrator` compose service does this).

## Migrations
```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/BuildingBlocks/MedSmarter.BuildingBlocks.Infrastructure \
  -s src/Host/MedSmarter.Api -c PlatformDbContext -o Persistence/Migrations
```

## Known issues / limitations
See [`docs/phase1-notes.md`](docs/phase1-notes.md) and [`docs/phase2/07-decisions-and-status.md`](docs/phase2/07-decisions-and-status.md).
