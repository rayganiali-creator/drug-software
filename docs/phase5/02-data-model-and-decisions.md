# 02 — Data model and design decisions

## Schemas and tables (EF Core migrations, snake_case, enums stored as text)
| Schema | Tables (main) |
|---|---|
| `patients` | `patient` (pseudonymous subject id = user id, status, soft delete), `patient_profile` (year of birth — **not** birth date —, sex, weight, height, time zone, version), `condition`, `allergy`, `patient_medication` (+ `patient_medication_version`, soft delete, `medication_id` nullable: unregistered medicines are kept as typed), `schedule_entry`, `intake_log`, `symptom`, `external_patient_identifier` (HMAC hash only), `care_relationship`, `snapshot_meta` |
| `product_trace` | `product_record` (batch/lot, expiry, optional GTIN — **never generated**, method Manual/BarcodeScan/ExternalSystem, verification SelfReported/ProfessionalConfirmed, consistency findings, versions), `manufacturer_report` (state machine, frozen de-identified `payload_json` as **text** + SHA-256), `outbox_entry` (claim/lease/backoff) |
| `guidance` | `guidance_message` (template key, level, status, locale, patient part, professional part, frozen text) |
| `consent` | `consent`, `consent_event` (grant/revoke history) |
| `audit` | `audit_event` (append-only, hash chained, see [12](12-persistence-and-audit.md)) |

## Decisions
| # | Decision | Why |
|---|---|---|
| D1 | One pseudonymous **subject id** (= user id) keys a patient; the record contains no national id | minimisation; identifiers (national id, insurance member id) can only be linked as a keyed hash and are **NOT BUILT** end-to-end |
| D2 | Year of birth and age *band* only | a full birth date is not needed for any Phase 5 feature |
| D3 | A medicine the reference does not know is **kept as typed**, flagged "not in reference", excluded from interaction logic, never auto-matched | the person's truth beats a guess; no silent normalisation |
| D4 | Optimistic concurrency (`expectedVersion` → 409 `version.mismatch`) on every editable record; every change writes a version row | two devices, auditability, "who changed what" |
| D5 | Soft delete + retention window (`Patients:RetentionDaysAfterDelete`, `PurgeDeletedAsync`) | recoverable mistakes; the purge is explicit |
| D6 | Freshness per category (`last updated`, `count`, `stale after N days`) | old information must not look current |
| D7 | The manufacturer-report payload is **frozen** at submission and hashed; the hash is verified again right before sending | what the person/reviewer approved is exactly what leaves |
| D8 | `payload_json` is a `text` column, not `jsonb` | `jsonb` reformats the document and would break the integrity hash (found by the PostgreSQL tests) |
| D9 | Report state machine `Draft → PendingConsentOrReview → ReadyToSend → Sent → Acknowledged`, plus `Failed`/`Cancelled`; transitions only through `ReportStateMachine` | no hidden jumps |
| D10 | Outbox: claim by concurrency token, lease, back-off 1 / 5 / 30 / 120 / 720 min, `MaxAttempts` 5, **idempotency key** per report; consent is re-checked at send time | duplicates and withdrawn consent never send |
| D11 | Guidance text is **composed from reviewed templates** (fa/en), validated by `PatientMessagePolicy` before it is stored | no free-form AI text reaches a patient in this phase |
| D12 | Care relationship = **two-sided** (patient and provider must both accept), either side can end it; a deactivated patient grants professionals nothing | consent of both parties |
| D13 | Consent purposes: `Treatment, MedicationReview, Dispensing, Research, InsuranceSharing, ManufacturerReport, Monitoring, AiProcessing, AiExternalProcessing`; the last five can be **grantee-free** (no organisation) | some consents are not "to a person" |
| D14 | New data scopes `conditions`, `allergies`, `products` | consent granularity |
| D15 | Separation of duties in knowledge validation: the editor of a medication revision cannot validate it | one person cannot both write and approve a medical statement |
