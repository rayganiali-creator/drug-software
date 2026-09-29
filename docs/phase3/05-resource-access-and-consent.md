# 05 — Resource-level access and consent

## Ownership, relationship, organization — IMPLEMENTED NOW
* **Patient → own data**: `subject == actor` passes the ownership layer.
* **Professional → patient**: needs an **active** `CareRelationship` (Treating for physicians, Dispensing for pharmacists), directly or through an organization the professional belongs to. Ended relationships stop working immediately.
* **Organization data** (pharmacy inventory…): requires active membership of that specific organization; another org, an unknown id or no id → denied.
* **Patient data reached through an organization** (pharmacy prescriptions): membership **plus** the organization's own care relationship with the patient **plus** the patient's consent to that organization (purpose Dispensing/MedicationReview, scope `prescriptions`).
* Aggregate roles (industry, researcher) and admins have **no** patient-level route at all.

## Consent (`Consent` module) — IMPLEMENTED NOW
A consent is: subject, grantee (**one** user **or** one organization), purpose, data scope(s), granted-at, expires-at, revoked-at, version of the consent text.
* Purposes: `Treatment`, `MedicationReview`, `Dispensing`, `Research`. Scopes: `profile, medications, prescriptions, adherence, adr, checkins, symptoms, ai_summary`.
* **Only the subject** can grant or revoke (`actor == subject`); others get `NotSubject` / "not found".
* Validation: known purpose, non-empty known scopes, exactly one grantee that is not yourself, expiry in the future and ≤ 366 days.
* Evaluation (`IConsentEvaluator`) for "grantee G reads scope S of subject X for purposes P": needs an **active** (not expired, not revoked) consent from X to G (or G's organization) whose purpose ∈ P and scope ∋ S. Internal reasons: `no_consent`, `consent_expired`, `consent_revoked`, `purpose_mismatch`, `scope_mismatch`.
* **Revocation is immediate** (evaluated on every request; test: physician allowed → patient revokes → next request 403).
* Grant/revoke/denials are audited; the consent text version is stored.

## Demo data (fictional) that shows every branch
| Situation | Account → patient | Result |
|---|---|---|
| Relationship + full consent | `demo-physician` → Sara | allowed |
| Relationship, consent **expired** | `demo-physician` → Ali (`demo-patient-2`) | denied (`consent_expired`) |
| No relationship | `demo-physician-b` → Sara | denied |
| Consent **scope** excludes data | `demo-pharmacist` → Sara: prescriptions ✔, adherence ✘ | scope-limited |
| Organization consent | Pharmacy A may read Sara's **prescriptions** (`GET /organizations/{org}/patients/{id}/prescriptions`) | members of Pharmacy A only, and only with the org's relationship + consent |

## DEFERRED
Emergency/break-glass access with retrospective review; guardian/caregiver proxies; consent for research/secondary use with de-identification; legal wording and per-jurisdiction consent capture (**[DECISION REQUIRED]** — regulator and lawful basis are still UNKNOWN from Phase 0).
