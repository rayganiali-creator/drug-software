# 09 — Testing

Run everything: `dotnet test MedSmarter.sln` · `cd web && npm run lint && npm run typecheck && npm test && npm run build` · `cd mobile && flutter analyze && flutter test` · `node security/build.mjs --check`.

| Area | Where | What it proves |
|---|---|---|
| Authentication | `AuthenticationTests` | login/refresh/logout/logout-all, rotation + reuse kills session, disabled/unknown accounts, throttling, minimal claims, tampered/expired/`alg=none`/foreign-key tokens rejected, immediate revocation, device handling, production guard, signing-key rules |
| Authorization | `AuthorizationTests` | exact role→permission mapping for all 9 roles, no patient permissions for admin/industry/researcher/content/AI/pharmacy-admin, multi-role union, unknown permission denied, role changes immediate + audited, last-admin protection, denial audit |
| Resource access | `ResourceAccessTests` | ownership, relationship, expired/revoked/out-of-scope consent, organization membership, ended relationships, listing |
| Consent | `ConsentTests` | subject-only grant/revoke, validation, statuses, indistinguishable not-found, audit, evaluation reasons |
| Audit | `AuditTests` | no tokens/passwords, redaction, size limits, hash chain + tamper detection, ordering |
| API 401/403 | `ApiTests` (real host via `WebApplicationFactory`) | 14 protected routes → 401 without token; invalid tokens → 401; missing permission → 403; uniform non-leaking 403 body; resource endpoints; revoked consent blocks next request; logout/revoke kills token; CORS + security headers |
| Web | `auth.test.tsx`, `apiBackend.test.ts`, updated `pages.test.tsx` | demo backend rules, guards for every area pair, redirects, disabled/expired states, consent-filtered patient lists, admin console, account page, sign-out, RTL login, API client (refresh, storage, error mapping) |
| Web browser QA | `scripts/qa.mjs`, `scripts/flow.mjs`, `scripts/auth-shots.mjs` | axe WCAG 2.2 AA, RTL/LTR, no horizontal overflow, console errors, sign-in/out flow on the built app; `auth-shots.mjs` signs in through the real login screen (local mode or `VITE_AUTH_MODE=api` against the running API) and asserts the essentials while capturing screenshots |
| Flutter | `auth_test.dart`, updated `app_test.dart` | controller rules, guarded navigation incl. deep links, no-access screen, expiry/sign-out messages, RTL login, 200 % text scale, account card |
| Generators | `security/build.mjs --check` (CI) | catalog rules (e.g. no patient permission for non-clinical roles) and generated code up to date |

## Not tested / DEFERRED
Load/penetration testing, real-provider integration, PostgreSQL persistence, clock-skew across nodes, device Keychain/Keystore behaviour, screen-reader testing on real assistive tech.

## End-to-end against the real API
Run the API with `Auth__Mode=DevelopmentMock` in Development (dummy dependency settings are enough: auth does not need Postgres/Redis), build the web with
`VITE_AUTH_MODE=api VITE_API_BASE_URL=http://127.0.0.1:5080` and run `node web/scripts/auth-shots.mjs --base <preview url>`. In this mode the physician's
patient list, the consents page and the sessions page are answered by the server (e.g. the patient whose consent expired is absent from the physician's list).
