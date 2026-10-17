# Phase 11 — External integration architecture and provider adapters

> **No real external system is connected.** Every integration is disabled or a labelled local mock. "Real" is fail-closed. Nothing here is production-ready.

## 1. What was inspected (actual code)
Existing provider abstractions: `IAIProvider` (Phase 6: Mock / Disabled / Local / External, the external one behind a consent gate), `IManufacturerReportProvider` (Phase 5: Disabled default, Mock), `IInsuranceProvider` (Phase 4: mock only, administrative data), `IAuthenticationProvider` (Phase 3: DevelopmentMock), `IProductScanProvider` (not-available). `HttpClient` appears only in the AI module (the gated external provider) and infrastructure health checks. No duplicated contract was found; the gap was a **common vocabulary and registry**, and contracts for four future systems.

## 2. Added
| Item | Where |
|---|---|
| Vocabulary: `IntegrationKind`, `IntegrationMode` (Disabled/Mock/Real), `IntegrationState` (Inactive/MockActive/RealBlocked/RealGatedElsewhere), `IntegrationDescriptor`, `AdapterResult<T>` (+ `AdapterOutcome`: NotConfigured/Simulated/Rejected/TransientFailure; `IsSimulated`) | `Integrations.Contracts/IntegrationContracts.cs` |
| Future adapter contracts (versioned `…/v1-draft` or `v0-placeholder`): `IMedicationReferenceAdapter`, `IProviderVerificationAdapter`, `IElectronicPrescriptionAdapter`, `INationalReportingAdapter` | same |
| `Disabled*` (default) and `Mock*` implementations | `Integrations/IntegrationAdapters.cs` |
| `IntegrationRegistry` — describes every integration (including the existing five) from configuration alone; makes no call; never returns an endpoint, key or value | `Integrations/IntegrationRegistry.cs` |
| `GET /integrations/registry` (permission `integrations.registry.read`, SystemAdmin only) | `Host/Integrations` |

## 3. Activation rules (fail-closed)
* Configuration `Integrations:{MedicationReference|ProviderVerification|ElectronicPrescription|NationalReporting}:Mode` = `Disabled` (default) | `Mock` | `Real`.
* `Mock` registers the local mock; anything else registers the **Disabled** adapter. `Real` does **not** activate anything: the registry reports `RealBlocked` with the reason and the list of what is required first. No real adapter exists to activate.
* Existing switches are read, not replaced: `ManufacturerReports:Provider`, `Integrations:Insurance:EnableMock`, `Ai:Provider`, `Auth:Mode`. An AI provider other than Mock/Disabled/Local is shown as `RealGatedElsewhere` (its own Phase 6 gate and per-patient consent still apply; the registry does not enable it).
* Secrets: none exist; the registry returns no configuration values (tested with a configured endpoint and key).
* **Timeouts/error handling:** mocks are synchronous and local. A real adapter, when one is ever approved, must use bounded timeouts and typed `AdapterResult` outcomes; this is a contract rule, not yet exercised.

## 4. Boundaries preserved
* **Insurance:** administrative/coverage data only. The registry states that adverse-event and pharmacy-safety records are never part of it; no code path from those modules to Integrations exists (Phase 9/10 assembly tests).
* **National and manufacturer reporting:** the national mock **refuses** a submission without a recorded human review and a confirmed legal basis, and returns a receipt labelled `MOCK-NATIONAL-…`; the envelope is deliberately thin because **no national form, field list, deadline or pathway was supplied**. The existing manufacturer pipeline keeps its human review, minimised payload and mock acknowledgements (`MOCK-ACK-`).

## 5. Tests (7 new, in `Phase11Tests.cs`)
Default = all adapters disabled and inert; mocks return labelled simulated results, the national mock refuses without review/legal basis and is idempotent; requesting `Real` leaves the Disabled adapter in place and the registry shows `RealBlocked` without echoing endpoint/key; insurance descriptor keeps the PV separation; the integration assemblies declare no HTTP client/handler/socket member; **a counting `HttpMessageHandler` installed on every `HttpClientFactory` client records zero requests while every adapter, the manufacturer mock provider and the AI mock provider are exercised**; the registry endpoint is 401/403/200 as specified.
Exact suite results: see the repository-wide review.

## 6. Blockers and what a real integration needs (nothing was activated or bought)
Per integration: the **authoritative specification** and access agreement; credentials managed outside source control; a **legal basis** and privacy review; consent/authorization re-check at disclosure; clinical/regulatory approval where applicable; hosting/egress policy and cost owner. For national reporting additionally: the authoritative **national form, pathway and deadlines** (none supplied) and a decision whether MedSmarter may submit at all (it is a facilitator, not a manufacturer, importer or authority).

## 7. Dependencies for later phases
The registry is the single place the web/mobile "simulated" labels and Phase 14 notification adapters should consult; Phase 15 analytics must not read integration data.
