# Phase 7 — Clinical safety engine, versioned rules, patient-specific medication risk assessment

> **Not clinically validated. Not production-ready.** Everything here is synthetic or demonstration data. Passing tests is not clinical validation.
> No rule in this repository is approved: approval needs two real people and a validated source, and no such source exists here.

| Doc | Content |
|---|---|
| [01-engine-design.md](01-engine-design.md) | architecture, data flow, determinism, the independent safety layer |
| [02-rule-contract-and-lifecycle.md](02-rule-contract-and-lifecycle.md) | rule fields, criteria kinds, life cycle, activation policy, two-person review |
| [03-provenance-and-approval.md](03-provenance-and-approval.md) | what counts as evidence, the five separate questions, demonstration rules |
| [04-completeness-and-freshness.md](04-completeness-and-freshness.md) | outcomes, missing vs stale vs not authorized, status derivation |
| [05-findings-and-guidance.md](05-findings-and-guidance.md) | finding contents, Guidance integration, idempotence, non-atomic limits |
| [06-security-consent-audit.md](06-security-consent-audit.md) | permissions, ownership, consent, privacy, audit |
| [07-api-ui-limitations.md](07-api-ui-limitations.md) | endpoints, web/Flutter states, limitations |
| [08-test-matrix.md](08-test-matrix.md) | every suite with exact results |
| [09-phase-report.md](09-phase-report.md) | final report: implemented, supported domains, demo rules, decisions needed, Phase 8 |

Run locally (no key, no internet, no paid service): `dotnet test MedSmarter.sln`; with a local PostgreSQL `MEDSMARTER_PG_TEST=… make test-pg`.
