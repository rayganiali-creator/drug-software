# Phase 8 — Adherence, scheduled reassessment, monitoring foundations, rule review UI

> **Not clinically validated. Not production-ready. Not continuous or real-time monitoring. No real notification is sent anywhere.**
> Adherence tracking and scheduled reassessment are bookkeeping around the deterministic Phase 7 engine. They are not clinically validated prediction.
> No rule is clinically approved in this repository. Persian clinical wording is unreviewed (visible banner in both clients).

| Doc | Content |
|---|---|
| [01-architecture.md](01-architecture.md) | what was added, data flow, module boundaries, transaction boundaries |
| [02-adherence.md](02-adherence.md) | patient-reported adherence semantics, instruction gaps, retries, professional access |
| [03-reassessment-and-worker.md](03-reassessment-and-worker.md) | change-aware reassessment, input versions, durable job, worker, retry, recovery, local execution |
| [04-delivery-and-guidance.md](04-delivery-and-guidance.md) | delivery states, the simulated channel, provenance, reactivation policy, urgent findings |
| [05-rule-authoring-and-review.md](05-rule-authoring-and-review.md) | the web workflow, server safeguards, source policy |
| [06-security-privacy-audit.md](06-security-privacy-audit.md) | authorization, consent, job payload privacy, audit |
| [07-test-matrix.md](07-test-matrix.md) | exact commands and results |
| [08-phase-report.md](08-phase-report.md) | final report, limitations, decisions needed, Phase 9 scope |

Local execution (no key, no internet, no paid service):
```bash
dotnet test MedSmarter.sln                                     # everything, in memory
MEDSMARTER_PG_TEST='<local PostgreSQL connection string>' dotnet test tests/MedSmarter.Patients.Tests   # + scratch-database suites
# workers are OFF by default. To try them locally (Development, PostgreSQL or InMemory):
ClinicalRules__Monitoring__WorkerEnabled=true ClinicalRules__Monitoring__IntervalSeconds=30 \
Guidance__Delivery__WorkerEnabled=true       Guidance__Delivery__IntervalSeconds=30  make api
```
