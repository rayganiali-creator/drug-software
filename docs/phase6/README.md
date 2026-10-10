# Phase 6 — AI assistant, evidence-grounded retrieval, safe provider architecture

**LOCAL / DEV / MOCK.** Nothing here is deployed or bought; no paid API, server, domain or cloud service is used, and no test needs a key or the internet.
All medical content is **fictional** (DEMO DATA — NOT FOR CLINICAL USE). The assistant is **not clinically validated**, makes **no prediction**, and is
**not** the Phase 7 safety engine. Status words: **IMPLEMENTED** (works and is tested), **MOCKED** (works with a stand-in), **NOT BUILT**.

| # | Document |
|---|---|
| 01 | [Architecture and reuse](01-architecture.md) |
| 02 | [Provider configuration and the external-processing gate](02-provider-configuration.md) |
| 03 | [Retrieval pipeline and provenance](03-retrieval-pipeline.md) |
| 04 | [Answer contract `ai-answer-1`](04-answer-contract.md) |
| 05 | [Safety, privacy and prompt-injection boundaries](05-safety-and-privacy.md) |
| 06 | [Web and Flutter integration](06-clients.md) |
| 07 | [Tests: what they prove and how to run them](07-testing.md) |
| 08 | [Local setup (no key, no internet)](08-local-setup.md) |
| 09 | [Limitations, risks and the hand-over to Phase 7](09-limitations-and-phase7.md) |
| — | [End-of-phase report](REPORT.md) |

Phase 5 review finding **M-01** (external AI needs explicit authorization, consent and input screening; fail closed) is **resolved** here: see 02 and 05.
All other review findings stay documented in `docs/repository-review-after-phase5/REPORT.md` and were not expanded into a rewrite.
