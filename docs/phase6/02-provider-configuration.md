# 02 — Provider configuration and the external-processing gate

## Settings (server side only — never in React/Flutter, never in source control, never logged)
| Key | Meaning | Default |
|---|---|---|
| `Ai:Provider` | `Disabled` \| `Mock` \| `External` \| `Local` | **`Disabled`** |
| `Ai:BaseUrl` | External gateway URL. **https only**, or http to a loopback address; no credentials in the URL. Anything else stops start-up | empty |
| `Ai:ApiKey` | External key. Environment / secret store only | empty |
| `Ai:Model` | Model name sent to the gateway | empty |
| `Ai:TimeoutSeconds`, `Ai:MaxTokens` | Per-call limits (timeout 1–120 s) | 20, 512 |
| `Ai:AllowExternalDataTransfer` | Operator approval to send anything to an external provider | **`false`** |
| `Ai:AllowMockInProduction` | Demo only; **fails start-up in Production** | `false` |
| `Ai:EvidenceStaleAfterDays` | Source "received" age after which evidence is flagged stale | 1095 |

Start-up guards (unchanged and extended): `AiGuard` refuses Mock outside Development/Testing (and `AllowMockInProduction` in Production) and now also refuses an unacceptable `BaseUrl`;
`AuthGuard`, `PersistenceSettings`, `MedicationsGuard`, `PatientsGuard` and `ManufacturerReportsGuard` are untouched.

## Providers
| Provider | Status | Notes |
|---|---|---|
| `DisabledAIProvider` | IMPLEMENTED | default; answer is "unavailable" and the evidence is still shown |
| `MockAIProvider` | IMPLEMENTED (MOCK) | deterministic, no model, no network; with evidence it lists *which* evidence exists by id and paraphrases nothing |
| `ExternalAIProvider` | IMPLEMENTED, **off by default**, never contacted in any test | neutral JSON gateway contract `POST {BaseUrl}complete {model,max_tokens,locale,question,policy[],evidence[],patient?}`; no redirects (`AllowAutoRedirect=false`); refuses without the gate's grant |
| `LocalAIProvider` | **PLACEHOLDER** | contract-ready (`ILocalModelRuntime`); without a runtime it reports `NotConfigured`. No local model is bundled |

## The gate (`ExternalProcessingGate`) — a global flag alone is never enough
All must hold, otherwise **nothing is sent** (fail closed; a check that cannot be made counts as failed):
1. `Ai:AllowExternalDataTransfer=true`, an acceptable `BaseUrl`, a key and a model (`external.not_approved`, `external.missing_base_url`, `external.insecure_base_url`, `external.missing_key`, `external.missing_model`);
2. the caller's own **`AiExternalProcessing` consent** is active, checked on every request; no evaluator or an error while checking → `external.consent_unverifiable`; no consent → `external.consent_required`;
3. the question passes **privacy screening** (`FreeTextGuard` incl. invisible and look-alike characters) → else `external.question_looks_identifying`.

Then the gate issues an `ExternalProcessingGrant`; the provider independently refuses a request without it (defence in depth). **Patient context** additionally needs the patient's `AiProcessing`
consent and `ExternalProcessingConsented`; categories without consent never enter the context.
A blocked request returns status `Unavailable` + the reason code, is audited (`AI_EXTERNAL_BLOCKED`, code only), and still shows the retrieved evidence. There is **no silent fallback to another provider.**
`GET /ai/provider` (permission `ai.manage`) lists `externalBlockers` — configuration codes only, never values or the key.

Professionals who cannot grant consent cannot use an external provider (fail closed). This is deliberate until an organisation-level approval exists.
