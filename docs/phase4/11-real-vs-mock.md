# 11 — Real vs mock

> **Update after Phase 5 (repository review):** Superseded for persistence by `docs/phase5/09-real-vs-mock.md`: the PostgreSQL-backed medication repository, audit and consent stores are implemented since Phase 5.

Legend: **IMPLEMENTED** works and is tested · **MOCKED** works with fictional data / a stand-in · **NOT BUILT** only prepared or documented.

| Capability | Status | Notes |
|---|---|---|
| Medication domain model (21 concepts), invariants, versioning, provenance | IMPLEMENTED | Unit-tested; served from an **in-memory repository** |
| Medication content | MOCKED | 7 fictional medications + 1 inactive, 2 fictional interactions, one Demo source. Labelled DEMO DATA — NOT FOR CLINICAL USE. No official identifiers |
| Drug search (fa/en, normalisation, ranking, paging, validation) | IMPLEMENTED | Own engine, no external search service; tested |
| REST endpoints, DTOs, uniform errors, audit, `perm:` policies, rate limits, `If-Match` | IMPLEMENTED | API tests with the in-process host |
| EF Core model + migration for PostgreSQL | IMPLEMENTED (schema) | Migration applied to a throwaway local PostgreSQL 16 and verified; opt-in tests |
| PostgreSQL-backed repository (the API reading/writing the DB) | NOT BUILT | The API serves the in-memory repository |
| `MedicationKnowledgeDocument` for RAG | IMPLEMENTED | Built only from validated, active, non-demo-mixed data; no retrieval index/embeddings |
| `IAIProvider`, settings, `AiGuard` | IMPLEMENTED | Disabled by default |
| `MockAIProvider` | MOCKED | Deterministic text; never presented as a real model; refused in Production unless explicitly allowed |
| `ExternalAIProvider` | IMPLEMENTED (adapter), UNTESTED against any real vendor | Neutral JSON contract; tested with a fake HTTP handler only; no key anywhere |
| `LocalAIProvider` | NOT BUILT (port only) | Needs an `ILocalModelRuntime` implementation; none shipped |
| Insurance abstractions (`IInsuranceProvider`, import tracking, dedupe, conflicts, retry) | IMPLEMENTED (in memory) | Fictional ids only |
| `MockInsuranceProvider` | MOCKED | |
| Insurance HTTP endpoints, real insurer connector | NOT BUILT | Permissions `insurance.*` exist but no role holds them |
| Web drug search/detail (Loading/Empty/Error/not-found/demo labels) | IMPLEMENTED | Tested on Chromium against the real API |
| Flutter drug search/detail | IMPLEMENTED | Widget tests with `MockClient`; Linux build run once under Xvfb |
| Android / iOS / Windows / macOS builds | NOT VERIFIED | See [10](10-platform-support.md) |
| Authentication | Phase 3 | Real JWT/session in API mode; the Mock auth backend is for the offline web demo only |
