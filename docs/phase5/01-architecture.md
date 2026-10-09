# 01 — Architecture

Still a **modular monolith** (.NET 10). No microservice, Kafka consumer, Redis cache or search index was added for Phase 5: the volume
(one patient's own lists) does not justify them, and every added moving part is a cost and an attack surface.

```
Web (React)  ─┐                                    ┌─ Patients ──── schema "patients"   (profile, conditions, allergies, medicines taken,
Flutter app  ─┴─ HTTPS/JSON ─ MedSmarter.Api ──────┤                                      schedule, intake, symptoms, care relationships)
                  (Phase 3 auth: JWT + session,    ├─ ProductTrace ─ schema "product_trace" (batch/lot records, manufacturer reports, outbox)
                   perm:<name> policies,           ├─ Guidance ──── schema "guidance"       (calm six-part messages, lifecycle)
                   RBAC → ownership/relationship   ├─ Consent ───── schema "consent"        (+ consent_event history)
                   → consent, uniform 401/403)     ├─ Audit ─────── schema "audit"          (append-only, hash chained)
                                                   └─ Medications ─ schema "medications"    (reference; PostgreSQL repository, pg_trgm)
```

* Every module is `X` + `X.Contracts`; modules reference each other **only** through `Contracts` (architecture tests enforce it).
  `Patients` offers `IPatientService`, `IPatientMedicationService`, `ICareRelationshipService`, `IPatientContextService`;
  `ProductTrace` offers `IProductTraceService`, `IManufacturerReportService`, `IManufacturerReportProvider`; `Guidance` offers `IGuidanceService`.
* **Care relationships** reach the authorizer through `ICareRelationshipSource` (Identity.Contracts), implemented by Patients — Identity does not
  depend on Patients. The authorizer merges the static catalog relationships (Phase 3 demo) with the dynamic, two-sided ones.
* **AI** gets a `PatientContext` (Patients.Contracts) only through `IPatientContextService`: consent-filtered, minimised, **no name / contact / id**;
  an external provider additionally needs the global `Ai:AllowExternalDataTransfer` **and** the patient's `AiExternalProcessing` consent.
* **Persistence switch** `Persistence:Provider` = `Postgres` (default) | `InMemory` (Development/Testing only; refused elsewhere, `PersistenceSettings.EnsureSafe`).
  One `AddModuleDatabase<TContext>` helper (BuildingBlocks) registers the context factory and migrator per module; each module keeps its own
  schema and `__ef_migrations_history`.
* **Host**: `Patients/PatientEndpoints.cs` (`MapPatientRecords`) holds all Phase 5 routes. The old Phase 3 demo "kinds" `profile/medications/adherence/symptoms`
  in `SecurityEndpoints` were **removed** (the demo data now lives in the real modules).
* **Startup guards**: demo seeding (`Patients/ManufacturerReports/Guidance`), the Mock manufacturer provider and `Ai:Provider=Mock` are refused outside
  Development/Testing; `Ai:AllowMockInProduction` makes the host **fail to start** in Production.
* **Clients** (Web, Flutter) hold no secret and no business rule: the server decides; a refusal arrives as an HTTP status and is shown through a translation.
