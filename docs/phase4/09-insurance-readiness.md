# 09 — Insurance integration readiness

**No insurer is connected. Nothing here proves an integration exists.** What exists are the contracts, a fictional provider and the rules a real channel must obey.

## Contracts (`Integrations.Contracts`)
`InsuranceMember`(insurer code, external member id, coverage — **no name, no national id, no diagnosis**) · `InsuranceCoverage`(plan, status, validity dates) ·
`ExternalPatientIdentifier`(system, value) · `InsuranceBatch` · `InsuranceDataImport` (trace of one received batch with per-record outcomes) · `IInsuranceProvider` (one adapter per insurer) ·
`IInsuranceIntegrationService` · `MockInsuranceProvider` (`DEMO-INS-0001…`, obviously fictional).

## Rules implemented and tested (`InsuranceIntegrationService`, in-memory store)
| Concern (brief) | Behaviour |
|---|---|
| validation of incoming data | per record: insurer/member/plan formats, known status, date order and sane range; invalid records are *rejected individually* with a code, the rest of the batch continues; batch size 1–5000 |
| provenance and time | each import stores provider id, batch id, content SHA-256, received-at, producer timestamp, actor |
| internal ↔ external id mapping | `LinkPatientAsync`: external id → **existing** patient only; one external id ↔ one patient; one patient ↔ one id per system; idempotent |
| no duplicate patients | an unmapped member is reported `Unmapped` — **a patient is never created by an import** |
| updates and contradictions | newer batch replaces; older ⇒ `Conflict(older_than_stored)`; same timestamp but different data ⇒ `Conflict(same_time_different_data)`; duplicates inside a batch ⇒ unchanged or conflict |
| idempotency / safe re-processing | same batch id + same bytes ⇒ `Duplicate`, no change; same id + different bytes ⇒ conflict; `RetryAsync` re-applies a stored batch (e.g. after linking) safely |
| processing status and errors | `Completed` / `CompletedWithIssues` / `Duplicate` / `Failed`; counts per outcome; provider outage ⇒ recorded `Failed` import with code `provider_unavailable` (no partial state) |
| audit | `INSURANCE_IMPORT` with counts and result — **no member identifiers**; denials audited |
| access control | `insurance.import` (import/link/retry/list) and `insurance.read` (coverage) — **held by no role** (deny by default); insurance data is separate from clinical data |

## NOT built (and needed before any real connection)
Contract with the insurer · legal basis and consent wording for receiving/using coverage data · official technical specification and authentication method of the channel ·
a real adapter + secure settings · PostgreSQL storage and retention rules · an operator role and UI · reconciliation reports · member-level read access with patient consent ·
penetration and privacy review. There are **no HTTP endpoints** for insurance in this phase on purpose.
