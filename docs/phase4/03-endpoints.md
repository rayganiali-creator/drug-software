# 03 — Endpoints

All endpoints need a bearer token (Phase 3). `403` = authenticated but missing the permission; `401` = not authenticated. Error bodies are
`application/problem+json` with `title`, `status` and (for 400/409/428) `code` + `errors[]` — **codes only**, never input echoes or stack traces.
Rate limits (per caller, per minute, per instance): search/detail 120, AI 20, edits 60 (`RateLimits__*`), then `429`.

| Method & path | Permission | Purpose |
|---|---|---|
| `GET /medications/search?q=&limit=&offset=` | `medication.read` | ranked, paged search (`limit` 1–50 default 20, `offset` 0–1000; `q` empty = browse, else 2–64 chars) |
| `GET /medications/{id}` | `medication.read` | full detail |
| `GET /medications/{id}/knowledge-document` | `medication.read` | RAG-ready, source-carrying projection |
| `POST /admin/medications` | `knowledge.manage` | create (starts as Draft, Unverified) |
| `PUT /admin/medications/{id}` + `If-Match: <version>` | `knowledge.manage` | edit with reason; withdraws validation |
| `POST /admin/medications/{id}/lifecycle` + `If-Match` | `knowledge.manage` | `{"status":"Active\|Inactive"}` |
| `POST /admin/medications/{id}/validation` + `If-Match` | `knowledge.publish` | `{"status":"Validated\|NeedsValidation\|Rejected\|Unverified","revisionId":…}` |
| `GET /admin/medications/{id}/versions` | `knowledge.manage` | change history |
| `POST /admin/ingredients` · `/manufacturers` · `/brands` · `/reference-terms` · `/interactions` | `knowledge.manage` | reference data |
| `GET/POST /admin/knowledge-sources`, `GET/POST /admin/knowledge-sources/{id}/revisions` | read: `knowledge.read` · write: `knowledge.manage` | provenance |
| `POST /admin/knowledge-revisions/{id}/status` | `knowledge.publish` | validate/reject a revision |
| `POST /ai/medication-assistant` | `ai.use` | source-grounded answer (`503` + `error` code when the provider is disabled/unavailable) |
| `GET /ai/provider` | `ai.manage` | provider kind/config state — never the key |

## Real responses (captured from the running API; fictional data)
```text
$ GET /medications/search?q=نوكتورين&limit=2   (Arabic ك/ي letters, role: Patient)
{"items":[{"id":"01a120f5-51e1-7522-9569-0cffbfb47af7","name":{"en":"Nocturin","fa":"نوکتورین"},"brandName":{"en":"Nocturin","fa":"نوکتورین"},"dosageForm":{"en":"Tablet","fa":"قرص"},"strengthSummary":"5 mg","ingredients":[{"en":"nocturami … (truncated)
HTTP 200

$ GET /medications/search?q=demoprilate&limit=3
{
 "total": 3,
 "items": [
  {
   "name": "Demoprilate 10 mg tablet",
   "score": 860,
   "matchedOn": "Demoprilate 10 mg tablet",
   "validation": "Demo"
  },
  {
   "name": "Demopril",
   "score": 840,
   "matchedOn": "demoprilate (fictional)",
   "validation": "Demo"
  },
  {
   "name": "Duodemo",
   "score": 840,
   "matchedOn": "demoprilate (fictional)",
   "validation": "Demo"
  }
 ]
}
HTTP 200


$ GET /medications/{id}   (detail, trimmed)
{
 "id": "01a120f5-51e1-7522-9569-0cffbfb47af7",
 "version": 2,
 "name": {
  "en": "Nocturin",
  "fa": "نوکتورین"
 },
 "dosageForm": {
  "en": "Tablet",
  "fa": "قرص"
 },
 "strengthSummary": "5 mg",
 "missingKinds": [
  "Indication",
  "Contraindication",
  "Precaution",
  "AdverseReaction",
  "Storage"
 ],
 "lifecycle": "Active",
 "validation": "Demo",
 "isDemo": true,
 "notice": "DEMO DATA - NOT FOR CLINICAL USE. Fictional record created for testing.",
 "ingredients": [
  {
   "name": "nocturamide (fictional)",
   "strengthValue": 5,
   "strengthUnit": "mg"
  }
 ],
 "statements": [
  {
   "kind": "Warning",
   "text": "Fictional warning: may cause demo drowsiness. Avoid driving after taking.",
   "validation": "Demo"
  },
  {
   "kind": "Administration",
   "text": "Take one tablet at bedtime.",
   "validation": "Demo"
  }
 ],
 "interactions": [
  {
   "with": "demoprilate (fictional)",
   "severity": "Moderate"
  }
 ],
 "sources": [
  {
   "name": "DEMO seed (fictional)",
   "version": "demo-0.1",
   "licenseName": "Fictional test data - no external licence"
  }
 ]
}
HTTP 200

$ GET /medications/search?q=a   (too short)
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Invalid search","status":400,"code":"q.too_short","errors":["q.too_short"],"traceId":"00-0444cc99140317633ba08739a72ef9c3-09bd03092e290a6e-00"}
HTTP 400

$ GET /medications/search?q=demo&limit=1000
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Invalid search","status":400,"code":"limit.range","errors":["limit.range"],"traceId":"00-3139454682350c5b76f1f06d9bd1bda1-ed2a673f78c4f786-00"}
HTTP 400

$ GET /medications/00000000-0000-0000-0000-000000000001   (unknown id)
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not found","status":404,"traceId":"00-db59f9fb25e474cfa97e3332014fbd52-65d93096fbc9b9d2-00"}
HTTP 404

$ GET /medications/search?q=demo   (no token)
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.2","title":"Authentication required","status":401,"traceId":"00-1c4f7801aeaf0950dd1e19cafbb13306-e33837f40ddd3dab-00"}
HTTP 401

$ POST /admin/ingredients   (Patient: no knowledge.manage)
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"Access denied","status":403,"traceId":"00-7c1b7da74bb9f36ac8afce974f1e94ed-5c9549e1013d7ddf-00"}
HTTP 403

$ POST /admin/medications   (Content manager, invalid draft)
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Invalid request","status":400,"code":"name.required","errors":["name.required","ingredients.count"],"traceId":"00-5e22af24146bfd587c1c731c749ca2b9-25301c26a318aa7e-00"}
HTTP 400

$ POST /admin/medications/{id}/validation   (no If-Match header)
{"title":"Invalid request","status":428,"code":"if_match.required","errors":["if_match.required"],"traceId":"00-fbf732be0769d8602b2871c62c102d6f-5ff66e19ccc7f476-00"}
HTTP 428

$ POST /admin/medications/{id}/validation   (fictional record can never be verified)
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Invalid request","status":400,"code":"demo.cannot_change_validation","errors":["demo.cannot_change_validation"],"traceId":"00-f8b1b2c56bb147109ace708bda2d239c-70f7f3b8e493e29f-00"}
HTTP 400

$ POST /ai/medication-assistant   (Patient, Mock provider, no API key)
{
 "text": "[MOCK AI - DEMO ONLY - NOT FOR CLINICAL USE] No language model was used; this lists the supplied source records.\n\n- Nocturin / نوکتورین (Tablet, 5 mg) [status: DEMO]\n  Active ingredients: nocturamide (fictional)\n  Warning: Fictional warning: may cause demo drowsiness. Avoid driving after taking. [Demo]\n  Administration: Take one tablet at bedtime. [Demo]\n  Interaction with demoprilate (fictional) (Moderate): Fictional: taken together, dizziness may feel stronger. [Demo]",
 "provider": "mock",
 "isMock": true,
 "answered": true,
 "sources": [
  {
   "sourceId": "01a120f5-50f2-7250-a7ab-11b40f95286a",
   "name": "DEMO seed (fictional)",
   "version": "demo-0.1",
   "publisher": "AI MedSmarter test fixtures"
  }
 ],
 "notice": "DEMO DATA - NOT FOR CLINICAL USE",
 "error": "None"
}
HTTP 200


$ POST /ai/medication-assistant   (no source in the knowledge base -> provider NOT called)
{"text":"No information about this is available in the medication knowledge base. Please ask a pharmacist or physician.","provider":"none","isMock":false,"answered":false,"sources":[],"notice":"No sources were found.","error":"None"}
HTTP 200

$ GET /ai/provider   (AI manager; the key is never shown)
{"provider":"mock","kind":"Mock","configured":true,"isMock":true,"model":null,"apiKeyPresent":false,"externalTransferApproved":false}
HTTP 200

$ GET /ai/provider   (Patient)
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"Access denied","status":403,"traceId":"00-158ca38f7a3f6b7abfc08f75d7cfdac8-d206a74cf952933b-00"}
HTTP 403
```
