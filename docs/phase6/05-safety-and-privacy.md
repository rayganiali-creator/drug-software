# 05 — Safety, privacy and prompt-injection boundaries

**Honest scope:** these are *product-boundary* safeguards (calm tone, no medicine-change advice, no diagnosis, no invented sources, fail-closed external use). They do **not** validate medical content and
**do not replace the independent deterministic clinical safety engine, which belongs to Phase 7** and must not be replaced by a prompt or a keyword list.

## Before any model is asked (`QuestionScreen`, fixed backend wording, fa + en)
| Class | Examples | Result |
|---|---|---|
| **Emergency** (checked first, outranks everything) | can't breathe, chest pain, swelling of face/throat, overdose, fainting, seizure, suicidal statements, severe bleeding, stroke signs; Persian equivalents | `Escalated`, `EmergencyServices`, calm but plainly urgent text, **no model call** |
| Rule-change attempt | "ignore previous instructions", "developer mode", fake `<system>` tags, Persian equivalents, full-width/mathematical look-alikes | `Refused` (`policy_override`) |
| Medicine start/stop/change | "should I stop…", "double the dose", "skip my tablets" | `Refused` (`medication_change`), `ConsultPrescriber` |
| Diagnosis request | "diagnose me", "do I have…" (not "do I have to…") | `Refused` (`diagnosis_request`) |
The emergency screen is **keyword-based**: it over-triggers on general questions ("does X cause chest pain?" escalates, by design) and **can miss real emergencies**. Every escalation carries the limitation `screen.keyword_based`, shown to the person.

## Retrieved content is untrusted data
Evidence is passed as structured data separate from the instructions; text that looks like instructions is **quarantined** (dropped, counted, reported); rejected statements are excluded; the model has **no tools and no database access**;
retrieval cannot change access control (it runs through the same authorised service and only for the caller's request). Pattern lists are one layer, never a guarantee.

## After generation (`AnswerSafetyPolicy`) — the text is data until it passes
Blocked (status `Blocked`, text withheld, reason codes audited): scare words, diagnosis phrases, over-confidence ("definitely", "completely safe"), any instruction to start/stop/skip/change a medicine or dose,
percentages/odds/"1 in 10", links/e-mail/phone numbers, instruction-like text, citations of ids that were not supplied, empty or over-long text. The backend's own fixed messages follow the same rules (tested).

## What is sent, stored and logged
* To an external provider, only after the gate: the question (screened), the evidence items (id, medication name, kind, text, source name/version, validation, demo flag), the fixed policy, and — only with both consents — the minimised patient context (no name, contact data or ids).
* **Not stored:** question, answer, evidence text. **Audit** (`AI_REQUEST_HANDLED` per request; `AI_PROVIDER_CALLED`; `AI_EXTERNAL_BLOCKED`; `AI_ANSWER_BLOCKED`; `AI_PATIENT_CONTEXT_USED`) holds status/reason codes, counts, provider kind and yes/no flags only (tested with marker strings).
* **Logs:** the external provider logs an HTTP status code only; no key, question, answer or body (tested).
* Free-text screening (`FreeTextGuard` + `TextFolding`) is **not anonymisation**; names, places and rare details are not detected.

## Fail-closed summary
No consent evaluator, consent error, missing/insecure config, identifying question, unknown provider response, timeout, malformed answer, policy violation → nothing unsafe is sent or shown; the evidence is still displayed with a reason code.
