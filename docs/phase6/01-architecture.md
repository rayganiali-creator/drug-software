# 01 — Architecture and reuse

```
Web / Flutter ──HTTPS──▶ POST /ai/medication-assistant  (perm: ai.use, rate limited)
                              │
                       AssistantService
   1 validate ─▶ 2 QuestionScreen ─▶ 3 IEvidenceRetriever ─▶ 4 patient context ─▶ 5 ExternalProcessingGate ─▶ 6 IAIProvider ─▶ 7 AnswerSafetyPolicy ─▶ 8 audit
                    │ (fixed backend answers:                 │                          │ (External only,             │ (Mock | External |      │ (withholds the text,
                    │  emergency, refusal)                    │ IMedicationService       │  fails closed)              │  Local | Disabled)      │  keeps the evidence)
                    ▼                                         ▼ (no direct DB access)    ▼                             ▼
              no model call                          evidence items + provenance     no HTTP request            model sees only evidence + policy + (consented) context
```

**Reused, unchanged in purpose:** `IAIProvider`, `MockAIProvider`, `ExternalAIProvider`, `LocalAIProvider` + `ILocalModelRuntime` seam, `DisabledAIProvider`, `ConfiguredAIProvider`
(provider chosen by settings only), `AiGuard`, `AuthGuard`, `PatientContext` + `IPatientContextService` (consent-filtered, no identity), `IPurposeConsentEvaluator`,
`IMedicationService` (search + knowledge documents), the audit writer, `FreeTextGuard`, the rate limiter and the Phase 3 permission catalog (`ai.use`, `ai.manage`).

**New in Phase 6** (all inside the existing AI module + small additive contract changes):
| Piece | File |
|---|---|
| Evidence model, answer contract, `IEvidenceRetriever` | `AI.Contracts/AiContracts.cs` |
| Normalisation, topic detection, `MedicationEvidenceRetriever` (+ pure `Build`) | `AI/Evidence.cs` |
| `InjectionPatterns`, `QuestionScreen`, `AnswerSafetyPolicy`, `SafeMessages` | `AI/Safety.cs` |
| `ExternalProcessingGate` (fail-closed) | `AI/ExternalProcessing.cs` |
| Rewritten `AssistantService` (pipeline above) | `AI/AssistantService.cs` |
| Evidence-citing Mock summary, `AnswerPolicy` instructions, gateway request with `policy` + `evidence`, grant check, https/loopback-only URLs | `AI/Providers.cs` |
| Source receipt date + validation on `KnowledgeSourceRef` (additive, optional) | `Medications.Contracts`, `Medications/Reading.cs` |
| Audit actions `AI_REQUEST_HANDLED`, `AI_EXTERNAL_BLOCKED`, `AI_ANSWER_BLOCKED` | `Audit.Contracts` |
| `TextFolding` (full-width / mathematical look-alikes → ASCII; needed because the repository runs with invariant globalization where NFKC is a no-op) | `BuildingBlocks` |

**Module boundaries kept:** AI references only `*.Contracts` of Medications, Patients, Consent, Identity and Audit (architecture tests pass). The model never reads a database; there are no tools.
