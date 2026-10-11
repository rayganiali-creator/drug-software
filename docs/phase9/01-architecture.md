# 01 — Architecture

## What was added
| Layer | Item |
|---|---|
| `Modules/ADR.Contracts` | `AdrContracts.cs`: enums (`ReporterRole`, `ReportOrigin`, `EventCategory`, `ReviewStatus`, `EntryKind`, `EntryVisibility`, `MedicationReferenceState`, `WordingStatus`), DTOs, `IAdverseEventService`, `AdrDefinitions` (quoted definitions), `AdrNotices` |
| `Modules/ADR` (was an empty placeholder) | `AdverseEventService`, `AdverseEventAssistance` (fixed template), `Persistence/AdrDbContext` + migration `InitialAdrSchema` (schema `adr`: `adverse_event`, `adverse_event_entry`) |
| `Host/Adr/AdrEndpoints.cs` | 11 routes, all default-deny |
| `Modules/Audit.Contracts` | 5 new audit actions (`ADR_RECORD_CREATED`, `ADR_ENTRY_ADDED`, `ADR_CLASSIFIED`, `ADR_STATUS_CHANGED`, `ADR_ASSISTANCE_GENERATED`); `ADR_ACCESSED` already existed |
| `security/access-catalog.json` | one new permission `adr.record.create` (Physician, Pharmacist); the others (`patient.adr.read`, `patient.adr.create`, `adr.review`) already existed since Phase 3 |
| Web | `web/src/adr`, `web/src/features/adr`, routes and navigation for patient, physician, pharmacist |
| Flutter | **no UI in this phase** (documented gap); only the regenerated access catalog and strings |

Module boundary: the ADR module references only BuildingBlocks, its own contracts, **Audit.Contracts** and **Patients.Contracts** (to check that a patient exists and that a named medication belongs to that patient). It references **no** Integrations, ProductTrace or HTTP assembly — a test enforces this. There is no outbound adapter in the module.

## Data model
`adverse_event` — one row per record. The **original report columns** (`event_description`, `onset_at`, `resolved_at`, `outcome_as_stated`, `additional_details`, `suspected_medications_json`, reporter role/user, origin, `reported_at`) are written once and never updated by any code path. The mutable part is `category`, `status`, `last_activity_at`, `version`.
`adverse_event_entry` — append-only: follow-ups, review notes, information requests, professional assessments, supplements, classification changes, status changes. Each has author, role, time, visibility and (where relevant) from/to values.
`subject_id` is the pseudonymous patient user id (no cross-schema foreign key, as in the other modules). Unique index `(subject_id, reporter_user_id, client_request_id)` gives retry safety.

## Routes
| Route | Who |
|---|---|
| `GET /adverse-events/definitions` | any signed-in user with `patient.adr.read` |
| `POST /patients/{id}/adverse-events` | the patient themself (`patient.adr.create`) or a professional (`adr.record.create`) with relationship + `adr` consent |
| `GET /patients/{id}/adverse-events` | the patient or a permitted professional |
| `GET /adverse-events/queue` | `adr.review`; **filtered per patient** to those the reviewer may read now |
| `GET /adverse-events/{rid}` | record's subject or permitted professional; authorized against the **record's own subject** |
| `POST …/{rid}/follow-ups`, `…/supplements` | the record's patient (`patient.adr.create`) or a professional (`adr.review`) |
| `POST …/{rid}/entries`, `…/classification`, `…/status`, `…/assistance` | professionals only (`adr.review`); a patient is refused |

## Authorization (default deny)
1. Authenticated (fallback policy) → 2. catalog permission (RBAC) → 3. for a professional: an active care relationship and per-scope consent for the **`adr`** data scope, evaluated by the existing `IAccessAuthorizer` against the subject → 4. the patient record must be active. "No such record", "not your patient" and "no consent" all return the identical 403 (a test compares the bodies, ignoring only the trace id). Nothing in a body can name another patient.
The service additionally refuses professional-only operations for a `Patient` role and refuses a patient acting on another patient's record.

## Status moves
`Submitted → InReview | InformationRequested | Closed`; `InReview → InformationRequested | Assessed | Closed`; `InformationRequested → InReview | Closed`; `Assessed → InReview | InformationRequested | Closed`; `Closed → InReview` (with a reason). Closing and reopening require a note. One automatic move: when the **patient** answers an information request the record goes back to `InReview`, recorded on the entry. Every change bumps `version`; classification and status changes require the expected version (optimistic concurrency → 409).

## Transaction boundaries
Each write is one `SaveChanges` (row + entry together). The audit line is written **after** the commit (as in the other modules); if the audit write fails the data change is already stored — the same trade-off documented in Phase 5. A concurrent edit surfaces as `version.mismatch` (409), never as a silent overwrite.
