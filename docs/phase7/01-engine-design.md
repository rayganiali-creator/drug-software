# 1. Engine design and data flow

```
request ─▶ endpoint (RBAC → ownership/relationship → consent, per category) ─▶ ClinicalSafetyService
   snapshot  = frozen, minimal copy of what the requester may read (no free text, no dose, no notes)
   rules     = RuleStore (bounded, ≤500 rows)
   result    = ClinicalSafetyEngine.Evaluate(rules, snapshot, allowDemonstration)      ← pure, deterministic
   result    = SafetyLayer.Apply(result)                                             ← independent invariants
   store assessment (clinical_rules.assessment) ─▶ guidance for ACTIONABLE findings (Guidance module) ─▶ update link state
```

* `ClinicalSafetyEngine` is a pure function: no clock, database, network, free text, model or randomness. Two equal inputs give byte-equal JSON (tested, including a 400-case randomised run and shuffled rule order). Matching, severity and urgency are decided only here. An LLM is never involved: the module has no reference to the AI module and no HTTP package (architecture test).
* Criteria are a closed set implemented once in code: `ReferenceInteraction`, `AllergyMedicationMatch`, `DuplicateIngredient`, `UnregisteredMedication`, `SevereSymptomRecent`. Rules carry only parameters (minimum reference severity, number of days). No rule can carry code, SQL, a shell command or a prompt; rule text is screened (`RuleTextPolicy`) and rendered as plain text.
* Matching rules: interactions and duplicates by **ingredient id** only; allergy conflicts only for **medication-linked** allergies (same reference medicine or shared ingredient id). A medicine that is not in the reference is never matched by name and never guessed. Free-text allergies, conditions, doses, routes, labs and adherence are not read.
* `SafetyLayer` re-checks the result after the engine and never trusts it: no safety claim; no actionable finding without an Active rule; every finding has an evaluation, evidence and a subject; the status agrees with the evaluations; a clean status never coexists with findings or gaps; `Complete` never coexists with gaps. A violation turns the result into `Failed` with the findings **kept** (a failure must not hide a serious finding) and no guidance is raised.
* Failure is conservative: a rule that throws is reported as outcome `Error` (other rules still run, result is incomplete); a technical failure while gathering data is `503 Unavailable`, never a partial or empty result; a result that cannot be stored is not shown and raises no guidance.
* Phase 6 is unchanged: the external-AI gate is untouched and the engine works with Mock/Disabled providers because it does not use a provider. One small Phase 6 addition: every non-emergency assistant answer now carries the limitation `triage.not_performed`.
* Monitoring foundation (no workers, queues or notifications, no claim of continuous monitoring): `AssessCommand.Trigger = "reassessment"` is the minimal re-evaluation interface; `GET …/assessments/latest` reports `outdated` with reasons (`rules.changed`, `data.changed.<category>`) by comparing the stored inputs and rule-set version with today's. Nothing re-runs by itself. Adherence and monitoring services are Phase 8.
* Bounds: ≤60 active medicines per assessment (more → 400 `medications.too_many`), ≤120 reference reads, ≤100 symptoms, 50 assessments per list, rule list ≤200. Reference reads are one per distinct reference medicine (not per pair).
