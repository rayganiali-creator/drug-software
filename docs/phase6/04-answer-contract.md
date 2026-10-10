# 04 — Answer contract `ai-answer-1`

`POST /ai/medication-assistant` ← `{ "question": "...", "locale": "fa|en", "medicationIds": [guid]|null, "includePatientContext": false }` (question ≤ 500 characters, no control characters; else `400 question.invalid`).
Response status: **200** for every answer including refusals, escalations, "nothing found", blocked and "not authorized"; **503** only when a provider problem occurred (the body is still the full answer, evidence included).
Enums travel as strings. There is **no numeric confidence anywhere.**

```jsonc
{
  "contractVersion": "ai-answer-1",
  "status": "Answered",            // Answered | NoEvidence | Refused | Escalated | Blocked | Unavailable
  "reason": "answered",            // machine code: emergency_signs, medication_change, diagnosis_request, policy_override, no_evidence, answer.blocked,
                                   //   provider.timeout|unavailable|disabled|rejected|invalid_response|not_configured, external.* (see 02)
  "text": "…",                     // generated (or fixed backend) text; may cite evidence ids like [E1]
  "answered": true, "isMock": true, "provider": "mock",
  "generation": { "provider": "mock", "kind": "Mock", "model": "mock-deterministic-2", "isMock": true, "external": false },   // null when no model was involved
  "evidence": {                    // kept SEPARATE from the text
    "items": [ { "id": "E1", "medicationName": "Nocturin", "kind": "Warning", "text": "…", "qualifier": null,
                 "source": { "sourceId": "…", "name": "…", "version": "demo-0.1", "publisher": "…", "receivedAt": "2026-09-01T00:00:00Z", "validation": "Demo" },
                 "validation": "Demo", "isDemo": true, "stale": false, "sourceDateUnknown": false } ],
    "conflicts": [], "missingInformation": ["kind.AdverseReaction"], "limitations": ["evidence.publication_date_not_recorded"],
    "quality": "DemoOnly", "quarantinedCount": 0, "excludedCount": 0
  },
  "evidenceQuality": "DemoOnly", "limitations": ["…"], "missingInformation": ["…"],
  "nextStep": "ConsultProfessional",   // None | ConsultProfessional | ConsultPrescriber | EmergencyServices
  "notice": "DEMO DATA - NOT FOR CLINICAL USE",
  "patientContextUsed": false, "patientContextNote": null, "error": "None"
}
```
| Status | Meaning | Text written by |
|---|---|---|
| `Answered` | model text passed the safety policy | provider (Mock/External/Local) |
| `NoEvidence` | nothing relevant found; no model call | backend |
| `Refused` | rule-change attempt, medicine start/stop/change request, diagnosis request; no model call | backend |
| `Escalated` | possible warning signs; fixed urgent message; no model call; `nextStep=EmergencyServices` | backend |
| `Blocked` | the model's text failed the policy and is **withheld**; evidence still shown | backend |
| `Unavailable` | provider disabled/failed/timed out or external processing not authorized; evidence still shown | backend |

Clients translate the codes (`reason`, `limitations`, `missingInformation`, `nextStep`, `quality`); the fa/en strings live in `design/i18n/strings.mjs` (`grounded.*`).
