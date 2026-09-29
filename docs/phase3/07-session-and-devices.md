# 07 — Sessions and devices

| Item | Behaviour | Status |
|---|---|---|
| Session | One per login per device; absolute lifetime 7 days; refresh tokens cannot outlive it | IMPLEMENTED NOW |
| Access token | 10 minutes; session/user/roles re-checked on every request | IMPLEMENTED NOW |
| Device | App-generated random UUID stored by the client (web: localStorage; mobile: app storage), platform, app version, user-agent-derived name. **No fingerprinting.** A device id belonging to another user is ignored and a new one is created | IMPLEMENTED NOW |
| List sessions | `GET /sessions` (permission `session.read`), marks the current one | IMPLEMENTED NOW |
| Revoke one | `DELETE /sessions/{id}` (own sessions only; others → 404) | IMPLEMENTED NOW |
| Revoke all / logout everywhere | `POST /auth/logout-all` | IMPLEMENTED NOW |
| Admin revoke | `DELETE /admin/sessions/{id}` (`session.revoke.any`, SystemAdmin) | IMPLEMENTED NOW |
| Disabled user / removed role | Live sessions stop working on the next request | IMPLEMENTED NOW |
| Refresh-token theft | Reuse of a consumed token kills the session | IMPLEMENTED NOW |
| Client token storage (web) | Access token **in memory only**; refresh token in `sessionStorage`; never in `localStorage`, never logged. Trade-off documented in 08 | IMPLEMENTED NOW |
| Mobile secure storage (Keychain/Keystore) | DEFERRED (mobile is demo-account only; no tokens exist there yet) |
| Trusted devices, new-device alerts, MFA step-up, geo/IP risk | DEFERRED |
