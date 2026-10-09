# 04 — Running the backend, web and Flutter locally

> **Update after Phase 5 (repository review):** Since Phase 5 the API defaults to PostgreSQL (`Persistence:Provider=Postgres`); use `make api-memory` for the database-free mode described here. See `docs/phase5/07-local-setup-and-tests.md`.

Nothing below needs a purchased server, a domain or an API key. Everything binds to localhost.

## Backend (ASP.NET Core)
```bash
# prerequisites: .NET 10 SDK. PostgreSQL/Redis/OpenSearch/Kafka are NOT needed to serve the drug reference (it is in memory).
dotnet build MedSmarter.sln
# The host still requires its Phase 1 connection settings to be present (they may be dummies when no database is running; /health/ready
# then reports "not ready", which is expected):
export ConnectionStrings__Postgres='Host=localhost;Database=unused;Username=unused;Password=unused'
export Redis__ConnectionString=localhost:6379 OpenSearch__Uri=http://localhost:9200 Kafka__BootstrapServers=localhost:29092
export Cors__AllowedOrigins__0=http://localhost:3000
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5080 dotnet run --project src/Host/MedSmarter.Api --no-launch-profile
# or: make env && make api   (uses the generated, git-ignored .env)
```
`appsettings.Development.json` turns on, **for Development only**: the fictional login (`Auth:Mode=DevelopmentMock`), the fictional medications
(`Medications:SeedDemoData`), the Mock AI provider (`Ai:Provider=Mock`) and the fictional insurer. In any other environment the API refuses to
start with these on (see 06).

## Web (React)
```bash
cd web && npm ci
# the drug reference needs the API: sign in THROUGH the API
VITE_AUTH_MODE=api VITE_API_BASE_URL=http://localhost:5080 npm run dev      # http://localhost:3000   (or: make web-run-api)
```
Without `VITE_AUTH_MODE=api` the in-browser demo accounts have no token, so the drug pages show the honest "The drug reference is not connected" state.
Choose a demo account on the login screen (e.g. *DEMO Patient*), open **Drug reference** in the navigation.

## Flutter (Android · iOS · Windows · macOS · Linux)
```bash
cd mobile && flutter pub get
flutter run -d linux --dart-define=API_BASE_URL=http://localhost:5080        # desktop (Linux shown; use -d windows / -d macos on those systems)
flutter run -d <android-emulator> --dart-define=API_BASE_URL=http://10.0.2.2:5080
flutter run -d <ios-simulator>    --dart-define=API_BASE_URL=http://localhost:5080
```
Default base URL when `API_BASE_URL` is not given: `http://10.0.2.2:5080` on Android (emulator alias of the host), `http://localhost:5080` elsewhere.
After choosing a demo account the app starts a background development login against the API (token kept in memory only); open **Medications → Drug reference**.
The base URL is configuration; no secret is, or can be, stored in the app.

*Debug builds only* (ignored in release): `--dart-define=DEBUG_AUTO_SIGN_IN=demo-patient --dart-define=DEBUG_INITIAL_ROUTE=/medications/reference` — used to take the desktop screenshot on a machine without input devices.

## Optional: PostgreSQL
See 07. The API does not need it for this phase.
