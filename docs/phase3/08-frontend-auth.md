# 08 — Frontend authentication and authorization

**Frontend authorization is UX only.** It hides what a user cannot use; it is never the security boundary.

## Web (`web/src/auth`, `web/src/features/auth`)
* `AuthBackend` interface with two implementations, chosen by `VITE_AUTH_MODE`:
  * `LocalMockBackend` (default) — fictional accounts in the browser (**MOCKED**, DEMO ONLY). Stores only `{accountId, expiry}`; no tokens exist. Mirrors the server rules (roles, relationships, consents) so the app is explorable without a server.
  * `ApiBackend` (`VITE_AUTH_MODE=api`) — real calls to the API: login, in-memory access token, rotating refresh token in `sessionStorage`, single-flight refresh on 401, session-lost handling, server-decided `canViewPatient` probes. (**IMPLEMENTED NOW**, unit-tested with a mocked `fetch`; also exercised end-to-end against the running API, see REPORT.)
* Screens: **Login** (DEMO ENVIRONMENT label, fictional account list), **Session expired**, **Account disabled**, **No access** (neutral, leaks nothing), **Patient record not available**, **Account & security** (sessions, consents with revoke, "who accessed my data", role permissions), **Administration** (users/roles/audit, no patient records), **Workspace** placeholder for content/AI roles.
* Guards: `RequireAuth` (→ `/login`, remembers target), `RequireArea` (permission per area), permission-filtered navigation, `AppIndexRedirect` (home by role). Patient lists (`patients.list/get`, prescriptions, ADR, pharmacist/pharmacy queues) are filtered through `withAccessControl` (relationship + consent).
* Area ↔ permission: patient `patient.checkins.create`, physician `prescription.create`, pharmacist `prescription.review`, pharmacy `pharmacy.inventory.read`, industry `analytics.read`, admin `role.manage`.
* **DEMO DEV** controls in the account menu (switch demo account, simulate session expiry) are labelled `DEMO DEV`, exist only when the backend reports `demo`, and are absent for a real provider.
* Old "Switch demo role" (Phase 2, unauthenticated) is **removed**; role switching is now a labelled DEMO DEV account switch.

## Flutter (`mobile/lib/auth`, `mobile/lib/features/auth`)
* `AuthController` (demo accounts from `assets/shared/access-catalog.json`), `go_router` redirect: anonymous → `/login`; signed in but not a patient → `/no-access`; deep links guarded.
* Profile: account card (roles, sharing consents with immediate revoke, sign-out, DEMO DEV expiry).
* The mobile app remains the patient app (Phase 2 decision P2-5); professional roles are told to use the web app.
* Real token handling on mobile (secure storage, refresh) — DEFERRED until a real provider exists.

## Accessibility / i18n
All new strings are in the shared catalog (fa + en, RTL first-class). Web QA (axe, 5 theme/locale/viewport combinations) includes login, account, admin, workspace and the no-access page.
