# AI MedSmarter

Healthcare/pharmaceutical platform (patients, physicians, pharmacists, pharmacies, industry, AI assistant).
Architecture and requirements: [`docs/phase0/`](docs/phase0/README.md). Current state: **Phase 1 – project foundation**
(no product features yet).

## Layout
| Path | What | Stack |
|---|---|---|
| `src/Host/MedSmarter.Api` | Composition root, health, logging, `/version` | ASP.NET Core (.NET 10) |
| `src/BuildingBlocks/*` | `IModule`, `Result`, outbox/inbox, Postgres/Redis/OpenSearch/Kafka wiring, EF migrations | C# |
| `src/Modules/<Name>/` | 22 bounded modules from Phase 0 (`.Contracts` + implementation, both empty) | C# |
| `ai/` | AI service (health/config/logging only; no LLM yet) | Python 3.11+, FastAPI |
| `web/` | Dashboard shell showing API readiness | React 19, TypeScript, Vite |
| `mobile/` | Patient app shell showing API readiness | Flutter/Dart |
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
Readiness (`/health/ready`) reports `postgres`, `redis`, `opensearch`, `kafka` as Healthy/Unhealthy (HTTP 200/503) and
never includes exception text. Liveness (`/health/live`) has no dependencies.

## How to test
```bash
make test                # dotnet (unit+architecture+host), pytest, vitest, flutter test — no infrastructure needed
make infra-up migrate && make test-integration   # real Postgres/Redis (+ readiness of the whole stack)
make lint
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
See [`docs/phase1-notes.md`](docs/phase1-notes.md).
