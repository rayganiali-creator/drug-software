# 04 — Authorization

**Deny by default.** Every endpoint requires an authenticated caller (fallback policy) unless explicitly `AllowAnonymous`
(`/health/*`, `/version`, `/auth/login`, `/auth/refresh`, `/auth/demo-accounts` in dev). Unknown permission names are denied.

## Layers (all must pass) — `AccessAuthorizer`, status IMPLEMENTED NOW
1. **RBAC** — the caller's server-resolved roles grant the permission (catalog). Enforced at the edge by an ASP.NET policy `perm:<name>`.
2. **Resource scope**, depending on the permission kind:
   * `subject` → ownership (own data) **or** an active `CareRelationship` with the patient (directly or via the caller's organization);
   * `organization` → active membership of that organization;
   * `own` → resource must be the caller's own;
   * `aggregate` / `global` → RBAC is sufficient (aggregate endpoints return no patient-level rows).
3. **Consent** — for another person's data (see 05).

The result is an `AccessDecision(allowed, layer, reasonCode)`. The reason code is **internal**: it goes to the audit log, never to the client.

## HTTP behaviour
| Case | Status | Body |
|---|---|---|
| No / invalid / expired token, revoked session, disabled user, removed last role | **401** | `{"title":"Authentication required"}` + `WWW-Authenticate: Bearer` |
| Authenticated but not allowed (missing permission, no relationship, no/expired/out-of-scope consent, other org) | **403** | `{"title":"Access denied"}` — identical for every cause |
| Patient id that does not exist | **403** | identical to "not allowed" (no existence probing) |
| Revoking someone else's consent/session | **404** | identical to "not found" |
Tests assert the 403 bodies are the same shape and contain none of: permission names, "consent", "relationship", role names, patient names.

## Frontend authorization is UX only
Web/Flutter route guards, permission-filtered navigation and hidden buttons only shape what the user sees. **The API re-checks every request**
(tests call the API directly without any UI and assert 401/403).

## Not built (DEFERRED)
Policy-as-code engine (OPA/Cedar), break-glass access, delegated/proxy access (caregivers, guardians), time-boxed elevated roles, ABAC on data sensitivity.
