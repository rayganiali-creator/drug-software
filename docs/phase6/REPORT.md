# Phase 6 — End-of-phase report

Branch `claude/amazing-darwin-kfj6sv`. Phase 6 only; **Phase 7 was not started.** Nothing was bought or activated; no real AI provider, server, domain or cloud service was used or contacted.
All results are from runs in this session (2026-10-10).

## 1. Files and modules changed
* **AI module** (`src/Modules/AI/`): `Evidence.cs` (new), `Safety.cs` (new), `ExternalProcessing.cs` (new), `AssistantService.cs` (rewritten), `Providers.cs` (extended), `AIModule.cs`; **AI.Contracts** (evidence model, answer contract, retriever seam); csproj references to Consent/Identity contracts.
* **Small additive changes:** `KnowledgeSourceRef` (+ optional `ReceivedAt`, `Validation`) and `Medications/Reading.cs`; `TextNormalizer` (+ look-alike folding); `BuildingBlocks/TextFolding.cs` (new) and `FreeTextGuard`; 3 audit action names; `Program.cs` (`preserveStaticLogger`).
* **Web:** `ai/types.ts`, `ai/assistantApi.ts`, `features/assistant/{GroundedAssistant,AssistantEntry}.tsx`, `grounded.css`, routes (`/assistant/prototype`), prototype banner, ~115 new `grounded.*` strings (fa + en), `scripts/assistant-qa.mjs`.
* **Flutter:** `api/assistant_client.dart`, `features/assistant/grounded_screen.dart`, route `/assistant/live`, prototype banner, `ApiSession.send(timeout)`, `AppBadge` wraps long labels.
* **Tests:** `Phase6Tests.cs` (new, 107 tests), updates to `AiTests.cs`, `ContextAndAiTests.cs`, `ReviewTests.cs`, both API test fixtures, `grounded.test.tsx` (25), `grounded_test.dart` (16).
* **Docs:** `docs/phase6/` (README + 9 documents + this report), notes in `docs/phase4/08`, the Phase 5 review report (M-01 resolved), root README, `.env.example`.

## 2. Existing components reused
`IAIProvider` and its four providers, `ConfiguredAIProvider`, `AiGuard`/`AuthGuard`/other guards (not weakened), `PatientContext` + `IPatientContextService`, `IPurposeConsentEvaluator` and the existing `AiExternalProcessing` purpose, `IMedicationService` search and knowledge documents, the audit writer, `FreeTextGuard`, the rate limiter, Phase 3 permissions (`ai.use`/`ai.manage`), the design tokens, `apiFetch`/`ApiSession`, the shared i18n pipeline. See `docs/phase6/01`.

## 3. Implemented vs placeholder
| | Status |
|---|---|
| Provider abstraction, Mock, Disabled, External adapter (off by default) | IMPLEMENTED (External only against fake HTTP) |
| `LocalAIProvider` | **PLACEHOLDER** (contract + `ILocalModelRuntime` seam; no model) |
| Evidence retrieval over the medication knowledge base, provenance, staleness/conflict/missing flags | IMPLEMENTED (lexical, no vectors) |
| Structured answer contract `ai-answer-1`, refusal/escalation/blocked/unavailable states | IMPLEMENTED |
| Emergency/refusal screens, output policy, injection quarantine | IMPLEMENTED as keyword/pattern layers — **not** clinical triage |
| Web page + Flutter screen | IMPLEMENTED; platform status as in Phase 5 |
| Real model answers, semantic/hybrid retrieval, organisation-level external approval, voice, image, monitoring | **NOT BUILT** (voice/image remain labelled prototype) |

## 4. Retrieval and provenance
Question → normalisation (Persian/Arabic letters, digits, diacritics, ZWNJ, look-alikes) → medication search → knowledge documents → evidence items `E1…` each with source id, name, version, publisher, **received-on** date, validation, demo/stale/undated flags. Publication/effective dates are **not recorded**, so every answer says so. Rejected statements are excluded, instruction-like text quarantined, conflicts (interaction severity) reported without choosing, missing kinds named, quality stated as `None|DemoOnly|Unverified|Limited|Validated` — provenance status, never a number. No evidence → no model call. Generated text and evidence are separate fields. Details: `docs/phase6/03`, `04`.

## 5. External AI safeguards and fail-closed tests
`ExternalProcessingGate`: operator approval + acceptable https/loopback URL + key + model, **and** the caller's own `AiExternalProcessing` consent verified on every request, **and** privacy screening of the question; any unverifiable condition denies. The provider independently refuses without the gate's grant, does not follow redirects, never logs content. Patient context additionally needs the in-house and the external consent. M-01 is resolved (the previous behaviour — sending the question after the global flag alone — now fails closed). Tests: 10 fail-closed scenarios each assert **zero HTTP requests**, the reason code, retained evidence and the audit line; the other providers never touch the HTTP client; one success path sends exactly one request without the key; a loopback redirect test shows the second host is never contacted. See `docs/phase6/02`, `05`, `07`.

## 6. Test and build results (actual)
| Check | Status |
|---|---|
| `dotnet build -c Release -warnaserror` | **PASS** (0 warnings, 0 errors) |
| `.NET` in-memory, 2 consecutive full runs | **PASS** — Architecture 5 · BuildingBlocks 4 · Api 8 · Patients 218 (3 skipped) · Knowledge 286 (15 skipped) · Security 133, identical both runs |
| Scratch PostgreSQL | **PASS** — Patients 221/221 · Knowledge 301/301 · Security 133/133 |
| EF drift, 7 contexts | **PASS** (no changes) |
| `design/build.mjs --check`, `security/build.mjs --check` | **PASS** |
| Web eslint / `tsc` / vitest ×2 / build | **PASS** — clean / clean / 224/224 both runs (25 new) / OK |
| Web browser QA against the real API (Mock provider), `assistant-qa.mjs` | **PASS** — 120 checks, 0 failures; answer/refusal/escalation/nothing-found, en+fa × light+dark × desktop+phone; axe WCAG 2.2 AA clean; direction and overflow checked. Screenshots in `docs/phase6/screenshots/` |
| Flutter `analyze` / `test` ×2 | **PASS** — no issues / 125/125 both runs (16 new) |
| `MedSmarter.IntegrationTests` (3) | **NOT RUN** (Docker/OpenSearch/Kafka unavailable; skipped without `MEDSMARTER_IT=1`) |
| Docker image build, GitHub Actions CI | **NOT RUN** |
| Windows, macOS, Android, iOS, Firefox, Safari, Flutter Linux desktop run | **NOT RUN** — no compatibility claimed |
| Any real AI provider | **NOT RUN, by design** |

**Defects found and fixed during the phase (test-reliability, with evidence):**
* Two API test classes sharing one fixture type started hosts in the same process and **shared a process-wide `ConnectionStrings__Postgres` environment variable**, so two hosts seeded the same database (`pk_consent` duplicate) and failed to start — an intermittent failure that surfaced when more API test classes were added. Fixtures now pass their scratch database per host (`UseSetting`). Confirmed: failing before (42 failures in a combined run), 3/3 combined runs + the full PostgreSQL run pass after.
* Serilog's static bootstrap logger could be frozen by the first host and break a second host starting in the same process ("The logger is already frozen"); `UseSerilog(preserveStaticLogger: true)` removes that race.
* Invariant globalization makes `Normalize(FormKC)` a no-op, so full-width and mathematical look-alike characters bypassed the e-mail/link/number and instruction checks; `TextFolding` now folds them (tested).
* `AppBadge` overflowed on narrow screens with long translated labels (Flutter); it now wraps.
* Escalation/refusal answers wrongly carried "no source found" (no retrieval had happened); fixed and tested.

## 7. Remaining risks and dependencies on Phase 7
All Phase 5 review findings stay as documented (identity mock/in-memory H-01, audit-chain limits, re-identification risk, scale items). Phase 6 specifics: no real model was ever run; lexical retrieval only; publication dates unrecorded; conflict detection is interaction-severity only; keyword screens over/under-trigger; Persian clinical wording unreviewed; professionals cannot use an external provider. **Phase 7 must build** the versioned clinical rules, the independent deterministic safety engine (not replaceable by a prompt), labs/structured doses, evidence-backed risk assessment and alert lifecycle; the Phase 6 assistant is an information tool and is not that engine. See `docs/phase6/09`.

## 8. No paid service or real external integration
Confirmed: no server, domain, paid API, cloud service or real AI provider was purchased, activated or contacted; the only network use is loopback and local PostgreSQL. External AI is disabled by default and was exercised only against fake in-memory/loopback handlers. No key exists anywhere in the repository or clients.

## 9. No clinical prediction model
Confirmed: none implemented; no probability, risk score or predictive claim is produced; answers carry no numeric confidence and the output policy rejects percentages and odds.

## 10. Phase 7 not started
Confirmed. Work stops here and waits for approval.
