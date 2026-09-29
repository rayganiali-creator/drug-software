# AI MedSmarter — Phase 3 final report
**Identity, Authentication, Authorization & Access Control** · branch `claude/amazing-darwin-kfj6sv` · **LOCAL / DEV / MOCK — DEMO ENVIRONMENT, fictional data, NOT FOR CLINICAL USE**

Scope respected: Phases 0–2 were not redone; no medication knowledge, real AI/RAG, clinical safety engine, real external API or production infrastructure was added; Phase 4 was **not** started.

## 1. What was implemented
* **Shared access catalog** (`security/access-catalog.json`, 46 permissions, 9 roles, 4 organizations, 19 fictional accounts, care relationships, consents) → generator `security/build.mjs` emits C#, TypeScript, Dart and JSON copies; CI checks they are current and that catalog rules hold (e.g. no patient permission for admin/industry/researcher/content/AI/pharmacy-admin roles).
* **Three real backend modules**: `Audit` (hash-chained, sanitizing), `Consent` (subject-controlled, purpose/scope/expiry/revocation), `Identity` (users, roles, organizations, care relationships, devices, sessions, JWT + rotating refresh tokens, RBAC + resource-level authorizer, mock provider, demo seeder).
* **Host**: bearer authentication that re-resolves session/user/roles on every request, deny-by-default fallback policy, permission policies, uniform 401/403, denial auditing, security headers, tightened CORS, auth/session/consent/audit/admin endpoints and mock-data resource endpoints (patient data, pharmacy, analytics) that exercise every authorization layer.
* **Web**: login screen, guards, permission-filtered navigation, session-expired / account-disabled / no-access / record-not-available states, Account & security (sessions, consents with revoke, "who accessed my data"), Administration console, workspace placeholder, consent-filtered patient lists, two interchangeable backends (in-browser demo accounts, real API).
* **Flutter**: guarded `go_router`, login (fictional accounts), no-access screen for non-patient roles, expiry/sign-out messages, account card with consents.
* **Docs** (`docs/phase3/01…09`, each item marked IMPLEMENTED NOW / MOCKED / DEFERRED), README/`.env.example`/CI/compose updates.

## 2. Identity model
`User`, `UserProfile`, `UserIdentityLink(provider, subject)`, `UserRole` (history kept), `Organization`, `OrganizationMembership`, `CareRelationship`, `Device`, `Session`, `RefreshToken(hash)`; consent and audit are separate modules. A real provider replaces the mock by registering another `IAuthenticationProvider` — users, roles and data do not change. (docs/phase3/01)

## 3. Roles
Patient, Physician, Pharmacist, PharmacyAdmin, PharmaceuticalCompany, Researcher, ContentManager, AIManager, SystemAdmin. A user may hold several (union of permissions). (docs/phase3/02 has the full matrix)

## 4. Permission model
Permission names are a closed catalog; every permission has a **kind** that decides the extra checks: `subject` (relationship + consent, with a data scope), `organization` (membership), `aggregate` (no patient rows), `own`, `global`. Unknown permission ⇒ denied. Nothing but the catalog decides role→permission.

## 5. Authentication architecture
`Client → POST /auth/login → AuthenticationService → IAuthenticationProvider (MockAuthenticationProvider) → user link → session/device → access JWT (10 min, HS256, claims only sub/sid/jti/times) + opaque refresh token (SHA-256 at rest, single use, rotation, reuse ⇒ session killed)`. Every request: token validation, then server-side session/user/role resolution (revocation and role changes are immediate). Login throttling. **Dev auth cannot pass for production**: `Auth:Mode=DevelopmentMock` makes the API refuse to start unless the environment is Development/Testing; non-dev environments require a ≥32-char signing key; mock provider/seeder/demo-account endpoint are not even registered otherwise; startup warning; UI labelled DEMO ENVIRONMENT.

## 6. Session / device model
One session per login per device, absolute 7 days; list/revoke own sessions, logout-all, admin revoke; disabled user or removed role ends access on the next request; devices identified by an app-generated random id only (no fingerprinting; another user's device id is ignored). Web keeps the access token in memory and the refresh token in `sessionStorage` (never `localStorage`, never logged).

## 7. Authorization model
Layers, all required, deny by default: (1) RBAC via policy `perm:<name>`, (2) resource scope by permission kind, (3) consent for other people's data. Decisions carry an internal `layer:reason` that goes to the audit log only. **401** = not authenticated; **403** = authenticated but not allowed — identical body for every cause (also for non-existent patients); revoking someone else's consent/session ⇒ 404 identical to not-found. Frontend authorization is UX only; tests hit the API without any UI.

## 8. Resource-level authorization
Ownership (patient → own data); care relationship (physician: Treating, pharmacist: Dispensing; direct or via the professional's organization; ended relationships stop working); organization membership (pharmacy inventory); patient data through an organization (pharmacy prescriptions) needs membership **and** the org's relationship **and** the patient's consent to the org. Verified cases include: physician ↔ patient with consent ✔, expired consent ✘, no relationship ✘, pharmacist limited to consented scopes, other organization ✘, admin/industry/researcher ✘ for patient rows.

## 9. Consent model
Subject, single grantee (user **or** organization, not self), purpose (Treatment / MedicationReview / Dispensing / Research), data scopes (profile … ai_summary), granted/expires (≤ 366 days)/revoked, consent-text version. Only the subject can grant/revoke; revocation is effective on the very next request; grant/revoke/denial are audited. Legal wording and lawful basis remain **[DECISION REQUIRED]** from Phase 0.

## 10. Audit model
Central `IAuditWriter/Reader`; login/failed login/logout/refresh/**refresh reuse**/role changes/consent changes/patient-data reads/**every denial**/audit reads. Metadata sanitizer drops credential-like keys, redacts token-like values, caps size; failed logins do not store the attempted identifier. SHA-256 hash chain with `/audit/verify` (tamper test). Patients see "who accessed my data"; only SystemAdmin reads the full log (and that read is audited). Durable append-only storage is DEFERRED.

## 11. React changes
New `web/src/auth/*` (catalog, areas, LocalMock + API backends, context, guards), `features/auth/*` (Login, Account, Admin, Workspace, states), `withAccessControl` service decorator, `AppShell` (user menu, area switcher limited to permitted areas, DEMO DEV controls), routes now guarded (`/login`, `/app/account`, `/app/admin`, `/app/workspace`), patient pages use the signed-in subject, QA scripts sign in as the right fictional account. The Phase 2 unauthenticated "Switch demo role" is gone; account switching is an explicitly labelled DEMO DEV menu.

## 12. Flutter changes
`lib/auth/auth_controller.dart` (demo accounts from the shared catalog, consents, expiry), `features/auth/login_screen.dart` (Login + NoAccess), router `redirect`/`refreshListenable`, Profile account card, test helpers sign in by default; mobile stays the patient app (non-patients get a neutral screen).

## 13. Backend changes
New: `Audit`, `Consent`, `Identity` implementations + contracts; `Host/Security/*` (bearer handler, policies, uniform results, endpoints); `Program.cs` (auth pipeline, CORS methods/headers, security headers, string enums, guarded startup, dev seeding); `appsettings.Development.json`; `docker-compose.yml` (local api = Development); `Makefile api` target; central package versions (`IdentityModel.JsonWebTokens`, `Options.ConfigurationExtensions`, logging/options abstractions).

## 14. Tests and results  *(raw output in the appendix)*
| Suite | Result |
|---|---|
| .NET (Release, `-warnaserror`) | build 0 warnings / 0 errors · **150 passed**, 3 skipped (3 integration tests need real Postgres/Redis: env-gated) · security suite = 133 tests (authentication, authorization, resource access, consent, audit, real-host API 401/403) |
| Web (vitest) | **138 passed** in 8 files (auth backend rules, guards, redirects, states, API client, all Phase 2 pages re-run signed in) · eslint + `tsc` clean · production build OK |
| Web browser QA | axe WCAG 2.2 AA, RTL/LTR, overflow and console errors over 39 pages × 5 combinations, plus interaction flow incl. sign-in/out (see appendix) |
| Flutter | `flutter analyze`: no issues · **61 tests passed** (18 new auth/navigation/account tests + Phase 2 suite) |
| Generators | `design/build.mjs --check` and `security/build.mjs --check` up to date |

**Failing tests at the end of the phase: none.** Failures that occurred *during* development (all fixed) are listed in the appendix, verbatim.

## 15. Security checks performed
* Token: tampered payload, `alg=none`, foreign signing key, expired, oversized, garbage ⇒ rejected; claims contain no roles/PHI; refresh tokens stored hashed; rotation + reuse detection; signing key never committed (random in dev, required in prod).
* No secrets in the repo: appsettings test, catalog test (no `password/secret/token` fields), `security/build.mjs` rejects credential-like fields; `.env.example` has only commented placeholders.
* 401 vs 403 semantics and non-leaking uniform bodies; deny-by-default fallback (even unknown routes ⇒ 401); CORS allows only configured origins, methods GET/POST/DELETE, `Authorization` header; `nosniff`, `no-referrer`, `X-Frame-Options`.
* Production guard tests (host refuses to start with the mock or without a key in Production; production-style host offers no demo accounts or login).
* Audit hygiene tests (no tokens/passwords/medical content; tamper detection).
* Live end-to-end run against the running API (curl transcript in the appendix): 401 without/garbage token, 403 for admin routes as patient, 200/403 patient-data matrix (consent OK / no relationship / expired consent / random id / system admin), patient's "who accessed my data", audit chain intact. Web run in real API mode (`VITE_AUTH_MODE=api`): login, guards and consent-filtered patient list decided by the server.
* Accessibility/RTL: axe WCAG 2.2 AA over 39 pages × 5 theme/locale/viewport combinations (login, account, admin, workspace, no-access included).

## 16. Files created / modified


## 17. What remains mocked
Authentication itself (fictional accounts, no passwords, `MockAuthenticationProvider`); all persistence (users, sessions, consents, audit are in-memory and reset on restart); login throttle store; web *local* mode mirrors the server rules in the browser; patient/pharmacy/analytics endpoints return fictional payloads (`DEMO DATA`); Phase 2 clinical content is still a fixed demo dataset (a second demo patient sees the same fictional medicines); mobile has no real tokens.

## 18. Deferred (not built)
Real identity provider (OIDC/hospital SSO/national ID), MFA, password/recovery flows; PostgreSQL persistence for identity/consent/audit, append-only audit store/WORM export/retention; break-glass and proxy/guardian access; policy-as-code engine; trusted-device/new-device alerts; secure token storage on mobile; production key management/rotation; rate limiting at the edge; pen-test.

## 19. Unresolved decisions ([DECISION REQUIRED])
1. Identity provider and proofing level for each professional role (licence verification source is UNKNOWN).
2. Token delivery for the web in production: refresh token in `sessionStorage` (now) vs httpOnly same-site cookie through a backend-for-frontend.
3. Legal basis/wording and jurisdiction for consent, retention of consent and audit records, patient right to see full audit.
4. Whether pharmacists may have personal consents in addition to organization consents, and default consent durations.
5. Emergency access policy (break-glass) and who reviews it.
6. Whether SystemAdmin may ever see patient data (currently never) and how support access is granted.
7. Session lifetimes and re-authentication rules for sensitive actions.

## 20. Risks before Phase 4
* In-memory state: any restart logs everyone out and forgets consents/audit — **do not build on this as if persistent**; persistence should come before clinical features.
* The dev mock must never be enabled outside local development: the guard is startup-time; deployment pipelines must still set `ASPNETCORE_ENVIRONMENT` correctly and provide a signing key.
* Web refresh token in `sessionStorage` is exposed to XSS; needs the BFF/cookie decision before real users.
* Phase 4 features must go through `IAccessAuthorizer` + `IAuditWriter`; any new endpoint outside `MapSecurity` inherits only the deny-by-default fallback (authenticated) — not resource checks. Add permissions to the catalog, never inline role checks.
* `patients.list` filtering in the UI is convenience; server endpoints for real lists must apply relationship + consent filtering themselves (currently `/patients` lists relationships only).
* Audit volume/retention and PII in audit metadata need policy once real data exists.
