# 06 — Endpoints

All routes require a bearer token (`401` otherwise) and a Phase 3 permission (`perm:<name>`); then ownership/relationship and consent are checked for another person's data.
A missing record and a forbidden one both answer `403`/`404` in a way that does not reveal which (no enumeration). Writes use `expectedVersion` (409 `version.mismatch`).
`{id}` is the patient's subject id (= user id). Enum values travel as strings. Rate limit on writes: `RateLimits:WritePerMinute`.

| Area | Method + path | Permission |
|---|---|---|
| Record | `POST /patients/me` (create own record, idempotent) | authenticated |
| | `GET/PUT /patients/{id}/profile`, `GET /patients/{id}/freshness` | `patient.profile.read / .update` |
| | `GET/POST /patients/{id}/conditions`, `PUT/DELETE …/{cid}` | `patient.conditions.read / .update` |
| | `GET/POST /patients/{id}/allergies`, `PUT/DELETE …/{aid}` | `patient.allergies.read / .update` |
| | `GET/POST /patients/{id}/symptoms`, `DELETE …/{sid}` | `patient.symptoms.read / .create` |
| | `GET /patients/{id}/ai-context` (what an assistant would see) | `patient.ai-summary.read` |
| Medicines taken | `GET/POST /patients/{id}/medications`, `GET/PUT/DELETE …/{mid}`, `POST …/{mid}/stop`, `…/resume`, `GET …/{mid}/versions` | `patient.medications.read / .update` |
| Schedule, intake | `GET/POST …/{mid}/schedule`, `DELETE /patients/{id}/schedule/{eid}`, `GET /patients/{id}/doses?date=`, `POST /patients/{id}/intake`, `GET /patients/{id}/adherence` | `patient.medications.*`, `patient.adherence.read / .log` |
| Batches | `GET/POST /patients/{id}/products`, `GET/PUT/DELETE …/{pid}`, `POST …/{pid}/confirm`, `GET …/{pid}/versions`, `POST …/products/scan` (**501**) | `patient.products.read / .record` |
| Reports | `GET/POST /patients/{id}/manufacturer-reports`, `GET/PUT …/{rid}`, `POST …/{rid}/submit`, `…/cancel`, `…/review` | `manufacturer-report.read / .create / .review` |
| | `GET /manufacturer-reports/pending-review` (professionals; de-identified) | `manufacturer-report.review` |
| | `GET /manufacturer-reports/queue`, `POST …/queue/process`, `POST /manufacturer-reports/{rid}/retry` (admin) | `manufacturer-report.queue.read / .manage` |
| Care | `GET/POST /care-relationships`, `POST /care-relationships/{id}/accept`, `…/decline`, `…/end`; `GET /directory/providers` | `care.relationship.read / .manage` |
| Consent | `GET/POST /consents`, `DELETE /consents/{id}`, `GET /consents/history` | `consent.read / .grant / .revoke` |
| Guidance | `GET /guidance/messages`, `POST /guidance/messages/{id}/status`, `GET /guidance/samples?locale=` | `guidance.read / .update` (`guidance.professional.read` for the professional part) |
(The catalog `security/access-catalog.json` is the source of truth for permission names; 67 permissions in total.)

## Examples (DEMO)
```http
POST /patients/6a1c…/products
{ "productName": "Nocturin", "batchNumber": "DEMO-B-1", "expiryDate": "2027-03-31", "receivedOn": "2026-10-01", "method": "Manual" }
→ 201 { "id": "…", "expired": false, "findings": [], "verification": "SelfReported", "isDemo": true, "notice": "DEMO DATA — NOT FOR CLINICAL USE" }

POST /patients/6a1c…/manufacturer-reports
{ "productRecordId": "…", "issueType": "AbnormalAppearanceOrPackaging", "severity": "Mild", "occurredOn": "2026-10-01",
  "description": "The tablet had a different colour.", "includeConcomitantMedications": false, "clientRequestId": "0d6e…" }
→ 201 { "status": "Draft", "reviewRequired": false, "consentActive": false, "payloadPreview": { "reportReference": "MSR-…", "ageGroup": "40-64", … } }

POST /patients/6a1c…/manufacturer-reports/{rid}/submit   { "expectedVersion": 1 }
→ 200 { "status": "PendingConsentOrReview" }       (no consent yet)  |  400 { "code": "invalid", "errors": ["consent.required"] }

POST /guidance/messages/{id}/status   { "status": "Seen" }  → 200 | 409 when the move is not allowed
GET  /patients/{id}/doses?date=2026-10-09 → [{ "medicationName": "Demopril", "localTime": "08:00:00", "status": "Taken", … }]
```
Errors are `{ "code": "<machine code>", "errors": ["…"] }`; clients translate the code, they never show it raw.
