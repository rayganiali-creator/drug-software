# Online demo (Cloudflare Pages + Render) — preparation only

A public walkthrough of AI MedSmarter with **fictional data, the Mock AI provider and in-memory storage**. Nothing here is deployed by the repository; these are the settings and steps to do it by hand. Commands are shell-neutral: use any terminal, or the hosts' dashboards.

## What this demo is — and is not

- **Fictional data only.** Patients, medications and rules are made-up seeds marked "DEMO DATA - NOT FOR CLINICAL USE". Never enter real patient information.
- **Mock AI only.** The Mock provider lists what the supplied documents say; it generates no text and sends nothing anywhere. The API **refuses to start** in the Demo environment if any other provider, `Ai__AllowExternalDataTransfer`, `Ai__BaseUrl` or `Ai__ApiKey` is set, so no environment variable can silently switch on an external provider. The consent and external-processing checks are unchanged.
- **Storage is temporary.** Everything lives in memory. Data resets when the service sleeps, restarts or is redeployed (Render's free web service sleeps after a period of inactivity and wakes slowly on the next request). Accounts, sessions, consents and audit entries all start fresh each time.
- **The shared access code is not user authentication.** It is one code for everybody, used to keep casual visitors out of a public demo. Anyone who has it can use the allowed demo personas. Treat it like an unlisted link plus a lock: share it only with people you chose, and change `Demo__AccessCode` when it leaks. It is not a substitute for real sign-in, which does not exist yet.
- **Changing the code does not by itself revoke active sessions.** The API only checks the code on anonymous requests (sign-in, refresh, account list). In the current implementation a restart — which a redeploy causes — wipes the in-memory sessions, so everyone must sign in again with the new code. In a still-running process, an access token that was already issued stays valid until it expires (10 minutes) and nothing revokes it when the code changes. If sessions ever become durable, rotation will not revoke them unless that is built and tested.
- **Not clinical software.** No medical, regulatory or privacy-compliance claim is made.

## How the Demo environment is protected

The API runs with `ASPNETCORE_ENVIRONMENT=Demo` — a dedicated environment, **not** Development (no developer exception page, no `appsettings.Development.json`). `Production` and every other environment name keep refusing the mock sign-in, in-memory storage, demo seeds and the Mock AI provider.

| Safeguard | Where |
|---|---|
| Startup refuses: an environment name that is not exactly `Demo` (`demo`, `DEMO` stop the host); a missing/short (<12 chars) `Demo__AccessCode`; a missing, empty or invalid `Auth:DemoAllowedAccounts`; non-in-memory storage; non-https/wildcard CORS origins; any non-Mock/Disabled AI provider or external transfer; a missing `Auth__SigningKey` | `DemoGuard`, `AuthGuard` |
| Every **anonymous** endpoint needs the `X-Demo-Code` header — sign-in, refresh and demo-account discovery (and any anonymous route added later) — except `/health/*` and `/version`. Authenticated endpoints still need the bearer token, which only comes from a gated sign-in | `DemoAccessGate` |
| Code compared in constant time; 5 wrong codes from one connection address (and 100 in total) lock out for 15 minutes with `429` + `Retry-After`; the code is never logged or echoed | `DemoAccessGate`, `DemoAccessThrottle` |
| Only these accounts can be listed **or** signed into: `demo-patient`, `demo-physician`, `demo-pharmacist`, `demo-researcher`, `demo-industry`. Administrator, AI-manager, content-manager/reviewer and pharmacy-admin accounts are rejected by the server even if someone sends their ids directly, and the host will not start if such an account is put on the list | `Auth:DemoAllowedAccounts`, `DemoAccountPolicy` |
| Existing authorization, consent, permission and audit checks are untouched | — |

**Rate limiting and proxies.** The limiter is keyed on the TCP connection address. `X-Forwarded-For` is deliberately **not trusted**, because how Render sets it is **unverified**; a caller can therefore not reset the lockout by changing forwarded headers. The consequence: behind Render's proxy all visitors very likely share one connection address, so the limiter behaves as one global limit. Five wrong codes from anyone lock **everybody** out for 15 minutes (a denial of service on the demo, not a leak), and the 100-failure total cap stays as a second bound. A restart resets the lockout. Per-visitor limiting would need a verified trusted-proxy setting on Render and is not part of this demo.

## 0. Decide the names first (Render and Cloudflare depend on each other)

The Render API needs the exact Pages origin (CORS), and the Pages build needs the exact API origin (build variable and CSP). Before configuring either, choose both project names so both origins are known: e.g. Pages `https://medsmarter-demo.pages.dev` and Render `https://medsmarter-api.onrender.com`. Create the two projects with exactly those names (the host may append a suffix if a name is taken — use the final, real origins shown in each dashboard, and write them down). Then use these two origins in every step below.

## 1. API on Render (free web service)

1. New **Web Service** → connect the repository → runtime **Docker**.
   - Dockerfile path: `src/Host/MedSmarter.Api/Dockerfile`; build context: repository root.
   - Instance type: the **Free** plan. Do not add a database, disk or paid add-on.
2. Health check path: `/health/live` (`/health/ready` reports the unused infrastructure as unhealthy by design).
3. Environment variables. **Two values are secrets** — `Demo__AccessCode` (the demo access code) and `Auth__SigningKey` (the token signing key): enter them only in Render's secret fields and never commit them. All other values below are not secret:

| Variable | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Demo` |
| `Demo__AccessCode` | a long random code, ≥ 12 chars (e.g. generate one with a password manager, or `openssl rand -base64 18`) |
| `Auth__SigningKey` | a different random string, ≥ 32 chars |
| `Cors__AllowedOrigins__0` | the **exact** Cloudflare Pages origin, e.g. `https://medsmarter-demo.pages.dev` — https, no trailing slash, no path, no wildcard |
| `PORT` | `8080` — **unverified on Render**. The container always listens on 8080 (`ASPNETCORE_URLS` in the Dockerfile); `PORT` only tells Render where to look. Confirm Render's current port handling in the dashboard and fix the service's port setting if it differs |
| `AllowedHosts` | the Render host name, e.g. `medsmarter-api.onrender.com` (optional hardening) |
| `ConnectionStrings__Postgres` | placeholder, never connected: `Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused-placeholder` |
| `Redis__ConnectionString` | placeholder: `127.0.0.1:1` |
| `OpenSearch__Uri` | placeholder: `http://127.0.0.1:1` |
| `Kafka__BootstrapServers` | placeholder: `127.0.0.1:1` |

   The four infrastructure placeholders only satisfy start-up validation; storage is in memory (`appsettings.Demo.json` sets `Persistence:Provider=InMemory`). They contain no real credentials.
4. Do **not** set any `Ai__*` variable, `Persistence__Provider`, `Auth__Mode` or a real database. The committed `appsettings.Demo.json` holds all non-secret demo settings and no access code or key.
5. After deploy, check `https://<api>/health/live` returns 200 and `https://<api>/auth/demo-accounts` returns `401 demo_code_required` without the header.

## 2. Web on Cloudflare Pages

1. Create a Pages project from the repository (no Workers, no paid features).
   - Root directory: `web`; build command: `npm ci && npm run build`; output directory: `dist`; `NODE_VERSION=22`.
2. Build variables (**public by nature — they are baked into the JavaScript; never put the access code or any secret here**):

| Variable | Value |
|---|---|
| `VITE_AUTH_MODE` | `api` |
| `VITE_API_BASE_URL` | the Render API origin, e.g. `https://medsmarter-api.onrender.com` — **no trailing slash** (a slash produces `//auth/...` requests that fail) |
| `VITE_DEMO_GATE` | `1` (only a flag that makes the sign-in page ask for the code) |

3. `web/public/_redirects` gives single-page-app routing, and `web/public/_headers` sets the CSP and other security headers. **Before building**, edit `_headers` and replace the single placeholder host `REPLACE-WITH-YOUR-API-HOST` in the `connect-src` of the `Content-Security-Policy` line with the exact API origin from step 0 (the same value as `VITE_API_BASE_URL`; the Pages origin is the CORS value on Render, a different thing). Afterwards search the file: no `REPLACE` text may remain, or the browser blocks every API call. Commit that edit through your normal review.
4. The visitor enters the access code on the sign-in page; it is checked by the server and kept only in that browser tab's `sessionStorage`.

## 3. Check before sharing the link

- Without the code, sign-in and the account list fail (`401`); five wrong codes in a row lock everybody out for 15 minutes (see the rate-limiting note).
- The account list shows five personas and no administrator; `POST /auth/login` with `demo-system-admin` and a valid code returns `401 invalid_credentials`.
- `Access-Control-Allow-Origin` is the exact Pages origin only; the page carries the CSP from `_headers`.
- No real AI call is possible (the API would not have started).
- Remember the first request after idle takes about a minute on the free plan, and all data may be gone by then.

## Moving on later

The architecture stays provider-portable: a real or self-hosted AI provider needs its own reviewed decision, an operator flag **and** user consent, and is deliberately impossible in the Demo environment. A durable demo would need a separate review (real database, real identity provider) rather than loosening these settings.

## Limitations to know about

- **Unverified on Render:** the `PORT=8080` behaviour above, how Render sets `X-Forwarded-For` (not used, see the rate-limiting note), and that changing an environment variable restarts the service (this document assumes it does).
- Free-tier terms, sleep time, account/card requirements and limits change; confirm them on the providers' pages when you create the accounts (not verified here).
- The gate is a shared secret in a header over https. It does not identify users, and a leaked code gives access to the five personas until it is changed and the service restarted. The browser keeps the code in the tab's `sessionStorage`, readable by any script running in the page.
- Local run for developers is unchanged (`ASPNETCORE_ENVIRONMENT=Development`); do not expose a Development instance publicly.
