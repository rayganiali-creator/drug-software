# 01 — Identity model

> **Update after Phase 5 (repository review):** Consent, audit and care relationships are no longer in memory since Phase 5 (PostgreSQL: `consent`, `audit`, `patients` schemas). Users, sessions, devices and login throttling are still in memory.

## Entities (code: `src/Modules/Identity/MedSmarter.Modules.Identity/Domain.cs`)
| Entity | Purpose | Status |
|---|---|---|
| `User` | The person/account: id, status (Active/Disabled), timestamps. No credentials, no PHI. | IMPLEMENTED NOW (in-memory store) |
| `UserProfile` | Display name, email (fictional `@example.invalid`), locale. Kept apart from `User`. | IMPLEMENTED NOW |
| `UserIdentityLink` | (provider, subject) → user. Lets a real provider (OIDC, national ID, hospital SSO) replace the mock **without touching users, roles or data**. | IMPLEMENTED NOW |
| `UserRole` | Role assignment (optionally per organization). Revocation keeps history (`RevokedAt/By`). | IMPLEMENTED NOW |
| `Organization` / `OrganizationMembership` | Pharmacies, clinic, company. Membership is the scope for organization permissions. | IMPLEMENTED NOW |
| `CareRelationship` | "Provider (user or organization) is treating/dispensing for patient X". The relationship scope. | IMPLEMENTED NOW |
| `Device` | App-generated random id, platform, app version, name. **No hardware fingerprinting.** | IMPLEMENTED NOW |
| `Session` | One login on one device; server-side revocable; absolute lifetime. | IMPLEMENTED NOW |
| `RefreshToken` | Only the SHA-256 hash is stored; rotated on every use. | IMPLEMENTED NOW |
| Consent | Its own module (see 05). | IMPLEMENTED NOW |
| Persistence | All stores are in-memory behind `IIdentityStore` / `ISessionStore` / `IConsentStore` / `IAuditStore`. PostgreSQL implementations replace them. | **MOCKED** — DEFERRED (real DB) |

## Rules
* A user can hold **several roles** (`demo-multirole` = Physician + Researcher): permissions are the union.
* A **disabled** user, a user with **no active role**, or a user whose **session was revoked** is rejected on the very next request.
* Roles are names from the catalog; unknown roles grant nothing (deny by default).
* No role grants "all patient data". `SystemAdmin`, `PharmaceuticalCompany`, `Researcher`, `ContentManager`, `AIManager` and `PharmacyAdmin` hold **no** `patient.*` permission (enforced by the catalog generator and by tests).

## Demo identities (all fictional, no credentials)
19 accounts in the catalog: the nine roles, a second patient (`demo-patient-2`, whose physician consent has expired), a physician
with no patients (`demo-physician-b`), a multi-role user, a disabled user, and six "subject-only" patients that cannot log in.
