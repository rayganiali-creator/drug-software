# 08 — Local setup (no key, no internet)

```bash
make api-memory                      # API on http://localhost:5080, in-memory store, Mock AI provider (appsettings.Development.json)
make web-run-api                     # web, signed in through the API: Assistant page = the source-based assistant
cd mobile && flutter run -d linux --dart-define=API_BASE_URL=http://localhost:5080   # Profile → … or Assistant tab → "Open source-based assistant"
```
Sign in as `demo-patient` (fictional). Try: "Tell me about Nocturin", "عوارض نوکتورین چیست", "Should I stop taking Nocturin?", "I can't breathe", "xyzzy". With the default `Ai:Provider=Mock` every answer is labelled MOCK.

## Switching providers (server environment only)
```bash
Ai__Provider=Disabled      # default: evidence is shown, no text is generated
Ai__Provider=Mock          # development only (refused elsewhere)
Ai__Provider=External Ai__AllowExternalDataTransfer=true Ai__BaseUrl=https://… Ai__ApiKey=<from your secret store> Ai__Model=<name>
                           # AND the person must have given the AiExternalProcessing consent (consent API/UI) — see 02. NOT enabled or tested against any real service in this phase.
Ai__Provider=Local         # placeholder: reports "no local runtime installed"
```
Check what blocks external processing: `GET /ai/provider` as `demo-ai-manager` → `externalBlockers`.
