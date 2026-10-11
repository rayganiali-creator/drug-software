# Phase 9 — Professional pharmacovigilance records, human review, safe escalation

> **Not clinically validated. Not production-ready. Not a regulatory report.** The record built here is an **internal** record for human review.
> It is not submitted anywhere, not proof of causality, and has no seriousness grade or causality score. No clinical rule was activated or created.
> Persian wording is **awaiting clinical review**. MedSmarter is a **facilitator** of internal records and review; it is not a manufacturer, importer, national reporting authority or legally responsible reporting party.

Evidence labels used in every document of this phase (the five categories requested):

| Label | Meaning |
|---|---|
| **[SLIDE]** | directly supported by a lecture slide (Shalviri, Karimi Ghovanlou, Mohebbi) |
| **[SUPP]** | supported by a supplementary document, named (WHO 2025, FIP 2020, npj Digit Med review, Excel comparison) |
| **[DESIGN]** | design inference or proposal — **not** a source requirement |
| **[OPEN]** | open question needing an authoritative source or confirmation |
| **[BLOCKED]** | blocked pending clinical, regulatory, legal, privacy or expert approval |

| Doc | Content |
|---|---|
| [01-architecture.md](01-architecture.md) | module, schema, API, authorization, data flow, transaction boundaries |
| [02-record-and-classification.md](02-record-and-classification.md) | the internal record, conservative classification, definitions and their sources |
| [03-field-traceability.md](03-field-traceability.md) | every field labelled source-derived / existing-system reuse / design proposal |
| [04-review-and-assistance.md](04-review-and-assistance.md) | professional review workflow, separation of voices, the template assistance (no model) |
| [05-security-privacy-audit.md](05-security-privacy-audit.md) | access rules, consent, uniform refusals, audit content, no outbound path |
| [06-persian-wording-governance.md](06-persian-wording-governance.md) | wording status registry |
| [07-sources-and-limits.md](07-sources-and-limits.md) | which files were read and how, known source limitations, Excel provenance |
| [08-roadmap-reconciliation.md](08-roadmap-reconciliation.md) | planned (Phase 0) vs delivered phase numbering |
| [09-test-matrix.md](09-test-matrix.md) | exact commands and results |
| [10-phase-report.md](10-phase-report.md) | final report, limitations, decisions needed, proposed Phase 10 |

Product-wide matrix (all phases): [`../pharmacovigilance-traceability-matrix.md`](../pharmacovigilance-traceability-matrix.md). Source analysis: [`../pharmacovigilance-source-analysis.md`](../pharmacovigilance-source-analysis.md).

Local execution (no key, no internet, no paid service):
```bash
dotnet test MedSmarter.sln                                   # everything, in memory
MEDSMARTER_PG_TEST='Host=127.0.0.1;Port=5432;Database=postgres;Username=…;Password=…' dotnet test tests/MedSmarter.Patients.Tests   # + PostgreSQL (scratch, local)
cd web && npx vitest run && node scripts/phase9-qa.mjs       # browser QA needs the local API + a local PostgreSQL
```
