# 03 — Field traceability

The list of fields is a **proposed minimum internal record**, not an official national report form. [OPEN] No national reporting form, mandatory field list, deadline, seriousness criterion or causality method was supplied (see 07).

Legend: **SD** source-derived · **ER** existing-system reuse · **DP** design proposal · (S) slide · (X) supplementary.

| Field | Basis | Where it comes from / why |
|---|---|---|
| `id` (stable record id) | DP | Required by the brief; random GUID |
| `subjectId` | ER | Pseudonymous patient user id used by Patients, Consent, Guidance, ProductTrace |
| `reporterRole` (Patient / Physician / Pharmacist) | DP, supported by SD (S) | Karimi Ghovanlou slides 8–9: reports from patients and from healthcare professionals differ, so the reporter type is kept as a first-class fact. A list of reporter types is **not** given by the slides |
| `origin` (PatientReported / ProfessionalEntered) | DP | Derived from the role; keeps the patient's account distinct from professional entry |
| `reportedAt` | DP | Time of recording |
| `eventDescription` | DP | Free text, as written |
| `onsetAt`, `resolvedAt` | DP | Dates appear among fields of public databases in the Excel comparison (X, **unverified**) — not authoritative |
| `outcomeAsStated` | DP | Free text "as stated"; deliberately not a coded outcome (no coding system supplied) |
| `additionalDetails` | DP | Free text |
| `suspectedMedications[]` (id or name) | ER + DP | Reuses `PatientMedication` ids (Phase 5); name-only entries for unknown medicines |
| `category` | DP over (X) definitions | Five terms with quoted definitions (WHO 2025, FIP 2020); default Unclassified |
| `status` | DP | Review workflow; **no status set exists in any source** |
| `missingInformation` | DP | Computed from stored fields |
| `version` | ER | Optimistic-concurrency pattern used across modules |
| `entries[]` (follow-up, note, request, assessment, supplement, classification, status) | DP | Append-only history; the brief requires an auditable history |
| `visibility` of an entry | DP | Keeps professional notes out of the patient view |
| `externalStatus = NotSubmitted` | DP | Makes explicit that nothing leaves the system |
| `signalNotice`, `internalRecordNotice` | DP | Required by the brief |
| `persianWordingStatus` | DP | Governance, see 06 |

**Deliberately absent** (no source supports them as requirements for MedSmarter): seriousness, causality, outcome codes, MedDRA/WHODrug terms, E2B elements, report type (initial/follow-up), reporter qualification, patient age/sex group (not copied into the record — the patient's profile is not duplicated), lot/batch, regulator identifier, due date.
