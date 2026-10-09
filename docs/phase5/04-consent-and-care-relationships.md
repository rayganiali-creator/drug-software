# 04 — Consent and care relationships

## Layers (unchanged from Phase 3, extended)
`RBAC permission → ownership / relationship → consent`, deny by default, uniform 401/403 (a missing *and* a forbidden record look the same: no enumeration).

## Care relationships — IMPLEMENTED
* A patient **or** a provider (physician = `Treating`, pharmacist = `Dispensing`) requests; the other side accepts or declines.
  States: `PendingProvider`, `PendingPatient`, `Active`, `Declined`, `Ended`. Both consents and timestamps are stored; **either side can end it** (with a reason).
* An `Active` relationship is only the *relationship* layer. Seeing data still needs a **consent** with the right purpose and scope.
* A deactivated patient grants professionals nothing. Ending a relationship takes effect immediately for the authorizer
  (`ICareRelationshipSource` is queried per request, no cache).
* `GET /directory/providers` lets a patient pick a professional by display name (no e-mail, no ids beyond the opaque user id).

## Consents — IMPLEMENTED
* Fields: purpose, scope (`profile, medications, allergies, conditions, symptoms, adherence, products, prescriptions, adr, checkins, ai_summary`), grantee (or none for
  grantee-free purposes `InsuranceSharing, ManufacturerReport, Monitoring, AiProcessing, AiExternalProcessing`), expiry, version of the text, status.
* **History**: every grant/revoke is an immutable `consent_event` (`GET /consents/history`), shown to the person.
* `IPurposeConsentEvaluator` answers "is purpose X active for this patient" for services (manufacturer send, AI context).
* Revocation is immediate; the demo seeding uses deterministic ids so it is idempotent.

## AI and consent
`PatientContext` includes a category only if the matching consent is active, and lists what was **excluded and why**
(`GET /patients/{id}/ai-context`, shown to the patient as "What an AI assistant could see"). Sending anything to an *external* provider
needs `Ai:AllowExternalDataTransfer=true` **and** the patient's `AiExternalProcessing` consent; the Mock provider is the default and never leaves the process.
