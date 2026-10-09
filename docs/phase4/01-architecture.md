# 01 — Architecture of the medication knowledge core

```
 Web (React)      Flutter (Android · iOS · Windows · macOS · Linux)
      │                         │            same HTTP contract, token in memory only, base URL = configuration
      └──────────────┬──────────┘
                     ▼
        ASP.NET Core host  ── Phase 3 authentication (JWT + server-side session check) ── perm:<name> policies ── rate limiting
          │ /medications/*  /admin/*  /ai/*
          ▼
 ┌─────────────────────────── modular monolith (one process, modules talk through *.Contracts only) ──────────────────────────┐
 │ Medications  : IMedicationService (read) · IMedicationAdminService (edit/lifecycle/validate) · IKnowledgeSourceService     │
 │                domain model · TextNormalizer · SearchEngine · MedicationValidator · IMedicationRepository                 │
 │ AI           : IAIAssistantService → retrieves ONLY through IMedicationService → IAIProvider (Disabled│Mock│External│Local) │
 │ Integrations : IInsuranceIntegrationService (+ IInsuranceProvider, MockInsuranceProvider)  — contracts, not a connection   │
 │ Audit        : every edit/validation/AI call/insurance import/denial (Phase 3 hash-chained log)                             │
 └──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
   store: InMemoryMedicationRepository (default, runs anywhere)   │   MedicationsDbContext + migration for PostgreSQL (ready, tested)
```

**What is IMPLEMENTED:** the domain model; Persian/English search with normalization and ranking; source / provenance / validation rules;
versioned, optimistically-locked editing; the read/edit/publish endpoints with Phase 3 permissions; rate limits; the AI provider
abstraction with a deterministic Mock and tested External/Local adapters; the insurance integration contracts with a fictional mock;
the PostgreSQL schema, constraints, indexes and a migration (applied and tested against a real local PostgreSQL 16).

**What is NOT built:** the PostgreSQL *repository* (the API serves from memory; the schema is ready and tested), any real drug database import, any
real AI provider, any real insurer connection, a medication admin UI.

## Why one module (and not a new one per entity)
`Medications` owns the reference data: ingredient, brand, manufacturer, forms/routes/classes, statements, interactions, sources and revisions. Phase 0
sketched separate `ActiveIngredients` and `KnowledgeBase` modules; splitting them now would force cross-module foreign keys and chatty contracts
before any of them has behaviour of its own. They stay as (empty) placeholders and can be extracted later because everything crosses the boundary
through `Medications.Contracts` (DTOs only). This is recorded as decision **D4-1** (docs/phase4/02).

## How the AI is kept away from the database
The assistant never receives a connection, a query, or an entity. `IAIAssistantService` asks `IMedicationService` (the same authorised service the
API uses) for `MedicationKnowledgeDocument`s — source-carrying, status-labelled projections — and passes only those plus the question to the provider.
No source ⇒ the provider is not called and the answer says so.

## Security model (reuses Phase 3, adds nothing parallel)
* `medication.read` (new, global) — search/detail; held by Patient, Physician, Pharmacist, PharmacyAdmin, PharmaceuticalCompany, Researcher, ContentManager, AIManager. **Not** SystemAdmin.
* `knowledge.manage` — create/edit/activate/deactivate, add sources/revisions. `knowledge.publish` — validate/reject (separate and stronger). `knowledge.read` — list sources.
* `ai.use` — ask the assistant; `ai.manage` — see provider status (never the key).
* `insurance.import` / `insurance.read` (new) — held by **no role** (deny by default); a future integration role gets them deliberately.
* Drafts and inactive records are invisible to readers who lack `knowledge.manage` (the API ignores `includeInactive` for them).
* Errors are `ProblemDetails` with machine-readable codes only; malformed input is always 400 (also in Development); unknown → 404; no stack traces.
