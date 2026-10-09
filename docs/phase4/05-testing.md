# 05 — Running the tests

None of these tests needs the internet, an API key or a database server (except the optional PostgreSQL group).

```bash
node design/build.mjs --check && node security/build.mjs --check          # generated artefacts and catalog rules
dotnet build MedSmarter.sln -c Release -warnaserror && dotnet test MedSmarter.sln -c Release --no-build
cd web && npm run lint && npm run typecheck && npm test && npm run build
cd mobile && flutter analyze && flutter test
# optional, against a THROW-AWAY LOCAL PostgreSQL (see 07):
MEDSMARTER_PG_TEST='Host=localhost;Database=…;Username=…;Password=…' dotnet test tests/MedSmarter.Knowledge.Tests --filter PostgresSchemaTests
# browser checks (need Chromium): see below
```

| Area | Where | Covers |
|---|---|---|
| Domain & data | `ModelTests`, `ValidationAndProvenanceTests`, `VersioningAndLifecycleTests` | single/multi-ingredient, brand–ingredient–form links, strength per ingredient, missing information, demo labelling, no official identifier on demo, GTIN checksum / ATC format, provenance required, demo/real source separation, versioning, optimistic conflict, lifecycle, validation rules, audit |
| Search | `NormalizerTests`, `QueryValidationTests`, `SearchTests` | Persian/English, ي/ی ك/ک, digits, ZWNJ, partial names, ingredient/synonym search, ranking, paging, empty result, invalid input, inactive hidden |
| API & permissions | `KnowledgeApiTests` (real host) | 401 on every new route, role matrix, system admin excluded, edit ≠ publish, 400/404/409/428/429 with codes only, no stack traces, drafts hidden, full edit→validate flow, CORS preflight, production guards |
| Providers | `ProviderTests`, `AssistantTests` | Mock without key, deterministic, Disabled, External not approved ⇒ **zero HTTP calls**, timeout/5xx/4xx/bad JSON/network mapping, key never in body/messages/status, Local contract, guards, source-only answers, no-source ⇒ provider not called, audit without the question |
| Insurance | `InsuranceTests` | permission & denial audit, unmapped never creates patients, idempotent re-import, batch-id reuse conflict, stale/same-time conflicts, per-record rejection, duplicates, link rules, provider outage ⇒ failed import, retry, audit without member ids |
| PostgreSQL (optional) | `PostgresSchemaTests` | migration applies, graph round-trip, trigram search, unique identifiers, FK provenance, interaction order, demo≠validated, concurrency token, name/strength checks |
| Web | `knowledge.test.tsx` | API client (URL, mapping, no-token ⇒ not connected), search page states (loading, empty, error+retry, not connected, 403, 429, 400), paging, debounce, 1-char rule, Persian/RTL, master-detail, detail sections, missing info, notices, navigation per role |
| Web browser | `scripts/knowledge-qa.mjs` | real login, real API: list/search/empty/detail, axe WCAG 2.2 AA, RTL/LTR, overflow, console errors — en/fa × light/dark × desktop/tablet/phone |
| Flutter | `drugs_test.dart` | `ApiSession` (memory-only tokens, refresh, failures), `MedicationClient` (URL, parsing, error mapping), screens (all states, paging, detail, RTL, wide two-pane, 200 % text) |
| Phases 0–3 | unchanged suites | architecture boundaries, host health, 133 security tests, 138→ web tests, Phase 2 Flutter tests |

Browser checks:
```bash
# terminal 1: API (see 04)      terminal 2: cd web && VITE_AUTH_MODE=api VITE_API_BASE_URL=http://127.0.0.1:5080 npx vite build --outDir /tmp/web-api && npx vite preview --outDir /tmp/web-api --port 4174
CHROMIUM_PATH=… node web/scripts/knowledge-qa.mjs --base http://127.0.0.1:4174
```
