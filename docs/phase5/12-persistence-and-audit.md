# 12 — Persistence, migrations and the append-only audit chain

## Providers
`Persistence:Provider = Postgres` (default) or `InMemory` (Development/Testing only — `PersistenceSettings.EnsureSafe` throws otherwise).
`Persistence:MigrateOnStartup` (dev only) applies migrations; elsewhere run `dotnet run --project src/Host/MedSmarter.Api -- --migrate-and-exit`.
Seven EF contexts, each with its own schema and history table: `PlatformDbContext`, `medications`, `audit`, `consent`, `patients`, `product_trace`, `guidance`.
All report "No changes have been made to the model since the last migration" (`dotnet ef migrations has-pending-model-changes`), also checked in CI.

## Medications repository (prerequisite A)
`PostgresMedicationRepository`: unique source/revision/manufacturer-code/term constraints surface as `409`; search = trigram `LIKE`, prefix matches first; optimistic concurrency by
`ExecuteUpdate … WHERE version = @expected`. The in-memory repository enforces the same uniqueness so tests behave the same on both.

## Audit (prerequisite B)
* `audit.audit_event` rows are **append-only**: `BEFORE UPDATE/DELETE/TRUNCATE` triggers raise `restrict_violation` (23001).
* Appends take a transaction advisory lock, read the last hash and write `hash = SHA-256(previous hash ‖ canonical event)`; timestamps are truncated to microseconds so the stored value re-hashes identically.
* `/audit/verify` recomputes the chain; `IAuditStore.QueryAsync/StreamAsync` back the user's own access log and the admin view.
* **Fail-closed**: if the durable store is unreachable, auditable actions fail rather than proceed unrecorded (production-style hosts without a database answer non-200 on login).
* Never logged/audited: tokens, report content, free text, identifiers.

## Hardening done in this phase
* `Ai:AllowMockInProduction=true` makes a Production host **fail at start-up**; elsewhere it only produces a warning.
* `Ai:AllowExternalDataTransfer` is necessary but not sufficient: the patient's `AiExternalProcessing` consent is also required.
* Separation of duties in knowledge validation (editor ≠ validator, audited as denied `separation_of_duties`).
* Demo seeding, Mock manufacturer provider and `InMemory` are refused outside Development/Testing.
