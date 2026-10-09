# 07 — PostgreSQL and migrations

## State
| Item | Status |
|---|---|
| EF Core model, constraints, indexes (`MedicationsDbContext`, schema `medications`) | **IMPLEMENTED** |
| Migration `InitialMedicationsSchema` (generated with `dotnet ef`, 18 tables) | **IMPLEMENTED** — applied from scratch to a real local PostgreSQL 16.13 and re-checked with `has-pending-model-changes` |
| `--migrate-and-exit` applies the platform **and** medications schemas | **IMPLEMENTED** — verified on the same local server (18 + 3 tables) |
| 9 schema tests against PostgreSQL (`PostgresSchemaTests`) | **IMPLEMENTED**, optional (skipped unless `MEDSMARTER_PG_TEST` is set) |
| A PostgreSQL implementation of `IMedicationRepository` | **NOT BUILT** — the API serves from `InMemoryMedicationRepository` |

Why the repository is not built yet: this phase's goal is a working, demonstrable core on a developer machine; the in-memory repository passes the
same service-level tests and the schema/constraints are proven separately. Writing the EF repository is the first task once a database is chosen
(it needs: search over `medication_search_term` with `ILIKE`/trigram, the aggregate load with children, and a transactional save that also appends
`medication_version` and rebuilds `medication_search_term`).

## Local development database (what was done here, at no cost)
The development image already contains PostgreSQL 16. Nothing was installed or bought.
```bash
pg_ctlcluster 16 main start
sudo -u postgres psql -c "CREATE ROLE medsmarter_dev LOGIN PASSWORD '<random, kept outside git>' CREATEDB" -c "CREATE DATABASE medsmarter_phase4 OWNER medsmarter_dev"
sudo -u postgres psql -d medsmarter_phase4 -c "CREATE EXTENSION IF NOT EXISTS pg_trgm"     # trusted extension; the migration also declares it
export ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=medsmarter_phase4;Username=medsmarter_dev;Password=…'
dotnet ef database update -p src/Modules/Medications/MedSmarter.Modules.Medications -s src/Host/MedSmarter.Api -c MedicationsDbContext
#   or both schemas at once:
dotnet run --project src/Host/MedSmarter.Api --no-launch-profile -- --migrate-and-exit
```
Or use the project's compose stack (`make infra-up migrate`), which creates the same schemas in the Postgres container.

## Creating further migrations
```bash
dotnet ef migrations add <Name> -p src/Modules/Medications/MedSmarter.Modules.Medications -s src/Host/MedSmarter.Api -c MedicationsDbContext -o Persistence/Migrations
dotnet ef migrations has-pending-model-changes -p … -s … -c MedicationsDbContext     # CI runs this
```
The design-time factory uses `ConnectionStrings__Postgres` or a dummy local string; **no credential is in the source tree**. Migrations never run automatically when the API starts.

## For the future server
* Needs: PostgreSQL ≥ 13 (trusted `pg_trgm`) or a role allowed to `CREATE EXTENSION pg_trgm`; a dedicated role for the app (DML only) and one for migrations.
* Seed data is **never** a migration: demo medications are loaded by `DemoMedicationSeeder` only in Development/Testing. No patient data exists in any migration or seed.
* The audit log and consent store are still in memory (Phase 3); they get their own schemas later.
* Backups, retention and encryption at rest are environment decisions (not part of this phase).
