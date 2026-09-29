# 03 — Authentication

## Architecture
```
Client ──POST /auth/login──▶ AuthenticationService ──▶ IAuthenticationProvider (mock now, real later)
                                   │                          └─▶ ProviderIdentity(provider, subject)
                                   ├─ user = UserIdentityLink(provider, subject) → User      (must be Active, ≥1 role)
                                   ├─ Device (app-generated id) + Session (absolute 7 days)
                                   └─ TokenPair: access JWT (10 min) + opaque refresh token (rotating)
Every API request ─▶ BearerAuthenticationHandler
                       1. validate JWT (HS256 only, issuer, audience, lifetime, signature)
                       2. RE-RESOLVE on the server: session valid? user active? roles current?  (CurrentUser)
                       3. permission policy / resource authorization (see 04, 05)
```
| Piece | Status |
|---|---|
| `IAuthenticationProvider` abstraction; login/refresh/logout/logout-all; token issue/validate | IMPLEMENTED NOW |
| `MockAuthenticationProvider` (mode `DevelopmentMock`): authenticates a fictional account id, **no password, no secret** | **MOCKED** |
| Real provider (OIDC / hospital SSO / national ID), MFA, account recovery, password policy | DEFERRED |
| Login throttling (5 failures → 15 min lock per identifier; in memory) | IMPLEMENTED NOW (shared store DEFERRED) |

## Tokens
* **Access token**: JWT, HS256, 10 minutes. Claims: `sub` (user id), `sid` (session id), `jti`, `iat/nbf/exp`, `iss`, `aud`. **No roles, no permissions, no email, no name, no PHI** (test: `Access_token_carries_only_minimal_claims_and_no_roles_or_phi`).
* Because roles are not in the token, changing a role or revoking a session applies on the **next request**, not when the token expires.
* Validation rejects: bad signature, other algorithm (`alg=none`), wrong issuer/audience, expired, oversized (>2 KB) tokens.
* **Refresh token**: 256-bit random, stored only as SHA-256, single use. Refresh rotates it. **Reuse of an already-used token revokes the whole session** and is audited (`REFRESH_TOKEN_REUSE_DETECTED`).
* Signing key comes from `Auth:SigningKey` (≥32 chars, environment/secret store). In Development an empty key generates a random per-process key. **No key is committed.**

## "Cannot be mistaken for production"
* `Auth:Mode=DevelopmentMock` **refuses to start** unless `ASPNETCORE_ENVIRONMENT` is `Development` or `Testing` (`AuthGuard`, tested).
* Outside Development a real `Auth:SigningKey` is mandatory (startup fails otherwise).
* The mock provider, the demo-account directory (`GET /auth/demo-accounts`) and the seeder are **not registered** unless the mode is `DevelopmentMock`.
* The API logs a warning at startup: "DEV AUTH: DevelopmentMock is active…". The UI shows "DEMO ENVIRONMENT" on the login screen and "DEMO DATA — NOT FOR CLINICAL USE" everywhere.
* `docker-compose.yml` sets `ASPNETCORE_ENVIRONMENT=Development` for the **local** api service only, with a comment.

## Endpoints
`POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout`, `POST /auth/logout-all`, `GET /auth/me`, `GET /auth/demo-accounts` (dev only).
Responses: `401` unauthenticated / bad credentials (uniform), `403` with `code` = `account_disabled` | `no_active_role`, `429` throttled.
