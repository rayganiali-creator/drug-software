# 06 — Running without any API key

* **Authentication:** fictional accounts, no passwords (`Auth:Mode=DevelopmentMock`, Development/Testing only).
* **Drug data:** fictional medications loaded at start-up (`Medications:SeedDemoData`, Development/Testing only).
* **AI:** `Ai:Provider=Mock` (Development default) — deterministic, labelled `[MOCK AI - DEMO ONLY - NOT FOR CLINICAL USE]`, performs **no text generation**,
  lists the source records it was given and says "information not available" where nothing is recorded.
* **Insurance:** `Integrations:Insurance:EnableMock=true` registers a fictional insurer (`DEMO-INS-…`).
* **Tests:** no test performs an HTTP request to anything but an in-process test host or an in-memory fake handler (asserted: the External provider
  makes **zero** requests unless fully configured *and* approved).

## What happens when something is missing
| Situation | Behaviour |
|---|---|
| `Ai:Provider` unset (default) | `Disabled`: the assistant answers with a safe "not available" message (HTTP 503, `error: "Disabled"`) |
| `Ai:Provider=External` without URL/key/model or without `Ai:AllowExternalDataTransfer=true` | controlled `NotConfigured`; **no request is sent** |
| `Ai:Provider=Local` without a runtime | controlled `NotConfigured` ("No local AI runtime is installed.") |
| `Ai:Provider=Mock` in Production | the API **refuses to start** unless `Ai:AllowMockInProduction=true` (explicit opt-in) |
| `Medications:SeedDemoData=true` outside Development/Testing | the API refuses to start |
| `Auth:Mode=DevelopmentMock` outside Development/Testing | the API refuses to start (Phase 3) |
| Backend off | web/Flutter show "could not be loaded / not connected" with a retry; nothing crashes |

## Where a real key would go (later) — never in a client, never in git
`Ai__ApiKey` as an environment variable or secret store on the **server**. `.env.example` lists the names with empty values. The web and Flutter apps
only know the API base URL; they call `POST /ai/medication-assistant` and the server talks to the provider.
