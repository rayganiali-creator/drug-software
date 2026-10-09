# 09 — Real vs Mock vs not ready

| Capability | Status | Notes |
|---|---|---|
| Patient record (profile, conditions, allergies, medicines taken, schedule, intake, symptoms) | **REAL code, fictional data** | PostgreSQL + EF migrations; versioning, soft delete, freshness. Seeded patients are DEMO |
| Authentication | **MOCK** | `Auth:Mode=DevelopmentMock` (Phase 3): fictional accounts, no passwords; refuses to start outside Development/Testing. The identity *store* is still in memory |
| Authorization (RBAC → relationship → consent) | **REAL code** | catalog-driven, tested incl. IDOR |
| Care relationships, consents (+history) | **REAL code** | PostgreSQL (`consent`, `patients` schemas) |
| Audit | **REAL code** | persistent, append-only, hash-chained in PostgreSQL (triggers refuse UPDATE/DELETE) |
| Medication reference | **REAL code, fictional content** | PostgreSQL repository, pg_trgm; all medical statements are DEMO |
| Batch/lot records | **REAL code** (manual entry) | barcode scanning **NOT BUILT** (501) |
| Manufacturer report creation, de-identification, consent, review, outbox, retry | **REAL code** | tested incl. PostgreSQL concurrency |
| Manufacturer report **delivery** | **MOCK** | `MockManufacturerReportProvider` (acknowledgements labelled `MOCK-ACK-`); no real provider exists; nothing leaves the process |
| Guidance contract, tone policy, lifecycle, fa/en templates | **REAL code** | message *producers* (rules/AI) **NOT BUILT** |
| AI assistant | **MOCK by default** | no key, no network; the External provider gateway exists but needs approval + key + consent |
| Patient context for AI | **REAL code** | consent-filtered, minimised, no identity |
| Insurance | **MOCK** (Phase 4) | no real insurer connection |
| External identifiers (national id, insurance member id) | **PARTIAL** | table + keyed hashing code; no registry integration; the key is empty by default (feature refuses) |
| Web UI | **REAL**, tested on Chromium | |
| Flutter UI | **REAL code**, widget-tested; Linux debug build OK | Windows/macOS/Android/iOS untested |
| Docker integration tests | **NOT RUN here** | no Docker daemon in this environment; 2 of 3 integration tests were exercised against a local PostgreSQL/Redis, the 3rd needs OpenSearch/Kafka |
