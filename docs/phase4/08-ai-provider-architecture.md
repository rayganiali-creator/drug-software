# 08 — AI provider architecture

```
POST /ai/medication-assistant  (ai.use, rate limited, audited)
   │
   ▼
AssistantService
   1. validate question (≤ 500 chars, no control chars) and locale (fa|en)
   2. RETRIEVE through IMedicationService (the authorised service layer): given ids, or search by the whole question then its words
      → MedicationKnowledgeDocument[] (≤ 5; active records only; each statement carries its source and validation status)
   3. no documents ⇒ answer "No information … available", provider NOT called
   4. IAIProvider.CompleteAsync(AiRequest{question, locale, documents, maxTokens})   ← the only thing a provider ever sees
   5. audit AI_PROVIDER_CALLED (provider name, document count, result — never the question or the answer text)
   ▼
AssistantAnswer { text, provider, isMock, answered, sources[], notice, error }
```
The model has **no database access, no tool that reads patient data, and no field in `AiRequest` that could carry patient identity.** Sending patient data to a
provider needs its own reviewed contract (minimisation, lawful basis, consent) — deliberately not present.

## Providers (all behind `IAIProvider`)
| Provider | Status | Behaviour |
|---|---|---|
| `DisabledAIProvider` | **IMPLEMENTED** (default) | controlled `Disabled` error |
| `MockAIProvider` | **IMPLEMENTED / MOCKED** | deterministic; prefix `[MOCK AI - DEMO ONLY - NOT FOR CLINICAL USE]`; no language model; lists supplied records + statuses; "information not available" where empty |
| `ExternalAIProvider` | **IMPLEMENTED (contract + adapter), never called against a real service** | speaks the neutral JSON contract `POST {BaseUrl}/complete {model,max_tokens,locale,question,documents[]} → {text,model}`; bearer key from server config; timeout via linked cancellation; maps HTTP 408/429/5xx → `Unavailable`, other 4xx → `Rejected`, bad/empty JSON → `InvalidResponse`, timeout → `Timeout`, transport → `Unavailable`; messages never contain URLs, keys or exception text. Sends nothing unless URL + key + model are set **and** `Ai:AllowExternalDataTransfer=true` |
| `LocalAIProvider` | **contract ready, no runtime bundled** | delegates to an optional `ILocalModelRuntime`; without one → `NotConfigured` ("No local AI runtime is installed.") |
| `ConfiguredAIProvider` | **IMPLEMENTED** | picks the provider from `Ai:Provider` per call: switching = changing settings |

## Configuration (server side only)
`Ai__Provider` (Disabled|Mock|External|Local) · `Ai__BaseUrl` · `Ai__ApiKey` · `Ai__Model` · `Ai__TimeoutSeconds` (1–120, default 20) · `Ai__MaxTokens` (default 512) ·
`Ai__AllowExternalDataTransfer` (default false) · `Ai__AllowMockInProduction` (default false). `.env.example` lists them empty. `GET /ai/provider` (ai.manage) reports kind/configured/`apiKeyPresent` — never the key.

## Guards
* Production-like environments refuse to start with `Ai:Provider=Mock` unless `Ai:AllowMockInProduction=true`; an unknown provider name refuses to start.
* The Mock is registered in every environment (it is just a class) but is only *selected* by configuration, so it cannot become active by accident.

## Adding a real provider later (no change to the assistant)
1. Put a small adapter service (or a vendor-specific `IAIProvider`) that translates the neutral contract to the vendor API; keep the key on the server.
2. Set `Ai__Provider=External`, `Ai__BaseUrl`, `Ai__ApiKey`, `Ai__Model`, `Ai__AllowExternalDataTransfer=true` after the data-protection review.
3. Provide evaluation sets (Phase 0 requirement) before exposing it to anyone but staff.

## Local AI later
Register an `ILocalModelRuntime` (e.g. an inference server on the same machine) and set `Ai__Provider=Local`. No heavy model is downloaded or required by this repository.

## Tests that prove it (all offline)
Mock without key · determinism · Disabled · External with no approval/URL/key ⇒ **0 HTTP calls** · neutral contract shape, bearer header, key absent from body · timeout ·
429/5xx/4xx/bad JSON/network · no secret in messages · Local contract · guards · source-only answers · provider not called without a source · audit without the question · status never shows the key.
