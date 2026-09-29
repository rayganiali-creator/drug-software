# Phase 3 — Identity, Authentication, Authorization & Access Control

Everything here is **LOCAL / DEV / MOCK**. There is no real identity provider, no government / national-health / pharmacy /
insurance integration, no real password and no real personal data. All identities are fictional and labelled
**DEMO ENVIRONMENT / DEMO DATA / NOT FOR CLINICAL USE**.

Four concerns are kept apart on purpose (separate code, separate tests):

| Concern | Question it answers | Where |
|---|---|---|
| **Authentication** | Who is calling? | `Identity` module: `AuthenticationService`, `IAuthenticationProvider` |
| **Authorization** | May this role do this, on this resource? | `Identity` module: `AccessAuthorizer` (RBAC + resource scope) |
| **Consent** | Did the data subject allow this sharing, for this purpose and scope? | `Consent` module: `ConsentService` |
| **Audit** | What happened, who did it, was it allowed? | `Audit` module: `AuditService` |

Status legend used in every document: **IMPLEMENTED NOW** (works, tested) · **MOCKED** (works in dev with fictional data;
must be replaced) · **DEFERRED** (not built; needed before production).

| # | Document |
|---|---|
| 01 | [Identity model](01-identity-model.md) |
| 02 | [Roles and permissions](02-roles-and-permissions.md) |
| 03 | [Authentication](03-authentication.md) |
| 04 | [Authorization](04-authorization.md) |
| 05 | [Resource access and consent](05-resource-access-and-consent.md) |
| 06 | [Audit log](06-audit-log.md) |
| 07 | [Sessions and devices](07-session-and-devices.md) |
| 08 | [Frontend authentication (web + Flutter)](08-frontend-auth.md) |
| 09 | [Testing](09-testing.md) |
| — | [Final report](REPORT.md) |

Single source of truth: [`security/access-catalog.json`](../../security/access-catalog.json) → `node security/build.mjs`
generates C# (`Permissions`, `RoleNames`, …), TypeScript, Dart and JSON copies; CI runs `node security/build.mjs --check`.
