# 01 — Architecture and API

## Module and data
The previously empty **Pharmacies** module now holds the Phase 10 behaviour (`Modules/Pharmacies`, contracts in `Modules/Pharmacies.Contracts`). It references only BuildingBlocks, its contracts and **Audit.Contracts**. It has **no** reference to ProductTrace, Consent, Integrations, AI or any HTTP library (a test enforces this): it cannot send anything, and it cannot read a patient. The host composes the cross-module checks.

Schema `pharmacy` (migration `InitialPharmacySafetySchema`):
| Table | Content |
|---|---|
| `safety_event` | a dispensing error or near miss: organization, reporter (user id, role), kind, `reached_patient`, example tags (jsonb), description, optional date, medicine as named, how noticed/intercepted, immediate action, optional links (ADR id, concern id), status, `last_activity_at`, `version`. **No patient identifier** |
| `product_concern` | a product concern: organization, reporter, provisional type, description, optional date, `link_state`, **linked snapshot** (identifiers copied from a ProductTrace record + that record's version and verification at linking), **as-reported identifiers** (typed), optional links (ADR id, safety event id), manufacturer-report note flag, status, version |
| `safety_entry` | append-only history for both kinds: review note, follow-up, manufacturer-report proposal note, status change (from/to), author, role, time |
The original-report columns are written once; only status, activity time, version (and the proposal flag) change afterwards.

## Permissions (catalog, kind `organization`)
`pharmacy.safety.create`, `pharmacy.safety.read`, `pharmacy.safety.review` — granted to **Pharmacist** and **PharmacyAdmin** only. Physicians, patients, industry, researchers and administrators have none (tested).

## Routes
| Route | Permission |
|---|---|
| `GET /pharmacy-safety/definitions` | read |
| `POST/GET /organizations/{org}/safety-events` | create / read |
| `GET /safety-events/{id}` | read (audited) |
| `POST /safety-events/{id}/entries`, `/status` | review |
| `POST/GET /organizations/{org}/product-concerns` | create / read |
| `GET /product-concerns/{id}` | read (audited) |
| `POST /product-concerns/{id}/entries`, `/status`, `/manufacturer-report-proposal` | review |

## Authorization (default deny)
Authenticated → catalog permission → **membership of the organization that owns the record** (`AccessResource.Organization`). For `/…/{id}` routes the organization is read **from the record**, never from the request; "no such record" and "another organization's record" return the identical 403 (trace id aside). Creation uses the organization in the route and is checked against the caller's memberships.
Two optional links are verified in the host, each by that record's **own** rules:
* **ADR link** — only if the caller may review that adverse-event record (`adr.review` + relationship + `adr` consent for its subject). Missing and hidden answer the same `link.adr_unavailable`.
* **ProductTrace link** — only if the caller may read that patient's product records (`patient.products.read` + relationship + `products` consent), and the record exists; otherwise `product_record.unavailable`. The concern is then **refused**, never silently linked elsewhere; the pharmacist can re-submit as unmatched.
