# 07 — Tests: what they prove and how to run them

No test needs a key or the internet. External-provider behaviour is tested with in-memory HTTP handlers (`FakeHandler`, `CapturingHandler`) that count requests; the single "no redirects" test uses
two **loopback** listeners on this machine. No test contacts any real provider.

## What is proven (backend — `tests/MedSmarter.Knowledge.Tests/Phase6Tests.cs`, `…/AiTests.cs`, `tests/MedSmarter.Patients.Tests/ContextAndAiTests.cs`, `ReviewTests.cs`)
| Area | Tests |
|---|---|
| **Disabled external AI makes zero outbound requests** | 10 fail-closed scenarios (not approved, no URL, insecure URL, no key, no model, no consent evaluator, evaluator throws, no consent, identifying question, identifying question hidden by an invisible character) each assert `handler.Calls == 0`, status `Unavailable`, the exact reason code, evidence still returned, an `AI_EXTERNAL_BLOCKED` audit line and **no** `AI_PROVIDER_CALLED`. Disabled/Mock/Local providers never touch the HTTP client |
| Gate success path | exactly one request; body has `evidence` + `policy`, no key, no patient data unless the patient consented to external processing; patient-context-with-consent combinations |
| Provider defence in depth | the provider refuses without the gate's grant even when called directly; only https/loopback URLs without credentials are accepted (and others stop start-up); the HTTP client does not follow redirects |
| Retrieval | Persian/Arabic-letter and English queries find the same medication; input normalisation; provenance fields on every item; "nothing found"; missing kinds named; stale and undated sources; conflicting sources reported without choosing; rejected excluded; unverified lowers quality; only a fully validated, current, consistent, complete set is `Validated`; cap reported |
| Prompt injection / untrusted content | injected evidence is quarantined and never reaches the provider body; injected questions (en, fa, full-width, mathematical look-alikes) are refused without a model call; a model answer containing instructions is withheld |
| Calm language and emergencies | 11 emergency phrasings (en/fa) → `Escalated`, no model call, urgent plain text, honest `screen.keyword_based` note; emergency outranks an injection attempt; medicine-change and diagnosis requests refused calmly; ordinary medication questions are *not* refused; documented over-escalation case; 17 output-policy violation classes caught; calm text accepted; fixed backend messages obey the same policy |
| Provider failures | HTTP 401/400/429/500, malformed or empty bodies, timeout, network failure, disabled provider → `Unavailable` with a reason code, evidence kept, no leaked detail |
| Privacy / audit | markers in question, answer, evidence and key never appear in audit entries or logs; one `AI_REQUEST_HANDLED` line per request with codes/counts only |
| Contract shape | JSON has the documented fields, enums as strings, **no numeric confidence** |
| API | endpoint returns the structured answer; refusal and escalation are normal 200s; anonymous 401; oversized/invalid input 400; provider status is manager-only and never shows the key; every route still names an explicit permission (`ReviewTests`) |
| Free-text screening | invisible, full-width and mathematical look-alike characters no longer hide an e-mail, link or number (`ReviewTests`) |

## Clients
* Web (`web/src/__tests__/grounded.test.tsx`): API client mapping (200/503/401/403/429/400/500/network/timeout/not connected); page: prototype labelled when not connected; source-based page when API-connected; answer with MOCK label, evidence apart from text, collapsible source details, no number; every status (no evidence, refused, escalated alert, blocked, unavailable, external label, timeout with sources); conflict/stale/missing; HTTP errors in words; 500-character limit; Persian RTL.
* Flutter (`mobile/test/grounded_test.dart`): client mapping incl. timeout and network; screen states (answer + collapsible source details, escalation, refusal, withheld, external-blocked, HTTP error, Persian); prototype banner.
* Browser QA against the **real API** (Mock provider, PostgreSQL): `web/scripts/assistant-qa.mjs` — answer/refusal/escalation/nothing-found in en + fa × light + dark × desktop + phone, direction, overflow, axe WCAG 2.2 AA, screenshots.

## Commands
```bash
dotnet test MedSmarter.sln                              # all .NET suites, in memory
MEDSMARTER_PG_TEST='<local PostgreSQL connection string>' make test-pg   # + scratch-database suites
cd web && npm test && npm run lint && npx tsc --noEmit && npm run build
node scripts/assistant-qa.mjs --base http://localhost:4174             # needs make api-memory/api + a VITE_AUTH_MODE=api build served with `vite preview`
cd mobile && flutter analyze && flutter test
```
Note: the per-caller AI limit is 20 requests/minute; raise `RateLimits__AiPerMinute` for the QA script (it asks ~80 questions).
