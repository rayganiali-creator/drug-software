# Phase 5 — Patient record, medicines taken, batch/lot tracing, manufacturer reports, safe anticipatory guidance

**LOCAL / DEV / MOCK.** Nothing here is deployed or bought; no paid API, server or domain is used. All patients, batches, reports and
messages in the repository are **fictional** (labelled DEMO / MOCK / NOT FOR CLINICAL USE). Status words used in every document:
**IMPLEMENTED** (works and is tested), **MOCKED** (works with a stand-in that must be replaced), **NOT BUILT** (only prepared or documented).

| # | Document |
|---|---|
| 01 | [Architecture](01-architecture.md) |
| 02 | [Data model and design decisions](02-data-model-and-decisions.md) |
| 03 | [Batch/lot tracing and manufacturer reporting (with the de-identification policy)](03-batch-and-manufacturer-reports.md) |
| 04 | [Consent and care relationships](04-consent-and-care-relationships.md) |
| 05 | [Anticipatory-guidance contract (with sample messages)](05-guidance-contract.md) |
| 06 | [Endpoints](06-endpoints.md) |
| 07 | [Running locally and running the tests (no API key needed)](07-local-setup-and-tests.md) |
| 08 | [Platform status: Web, Android, iOS, Windows, macOS, Linux](08-platform-status.md) |
| 09 | [Real vs Mock](09-real-vs-mock.md) |
| 10 | [What needs contracts, permits or external APIs](10-external-dependencies.md) |
| 11 | [Limitations and known risks](11-limitations-and-risks.md) |
| 12 | [Persistence, migrations and the append-only audit chain](12-persistence-and-audit.md) |
| — | [Final report (14 points)](REPORT.md) |

Rules that shaped every decision: no server/API/domain purchase · tests never need the internet · no real secret in a repository or client ·
no real patient data · no invented official codes (GTIN, national id…) · deny by default (Phase 3 permissions) · nothing presented as real that is not ·
Phase 6 is **not** started.
