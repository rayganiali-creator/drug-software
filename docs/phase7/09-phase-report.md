# 9. Phase 7 report

## Implemented (where)
* `ClinicalRules.Contracts` (rule/assessment contracts), `ClinicalRules` module: `ClinicalSafetyEngine`, `ActivationPolicy`, `SafetyLayer` + `RuleTextPolicy`, `RuleValidator`, `RuleCatalogService`/`RuleStore`, `ClinicalSafetyService`, `DemoRules`, persistence (`clinical_rules` schema, migration `InitialClinicalRulesSchema`).
* Guidance: origin fields, `CreateFromOriginAsync`, `ListOpenByOriginAsync`, 5 `safety.*` templates (fa/en), migration `AddGuidanceOrigin`.
* Host: `SafetyEndpoints` (7 + 6 routes), 6 permissions (catalog regenerated), 8 audit actions, guards for demonstration switches.
* Phase 6: `triage.not_performed` limitation on non-emergency assistant answers (external-AI gate untouched).
* Web: safety page, physician/pharmacist patient page, rules page, message provenance line. Flutter: safety screen. fa/en strings (225 keys).

## Evidence-supported domains
Interactions between reference-linked medicines (by ingredient id), medication-linked allergy ↔ medicine, duplicate ingredients (by id), data-quality (medicine not in reference), patient-reported severe symptom (reported, not interpreted). **Unsupported and stated on every result:** dose/route, adherence, labs, condition contraindications, ingredient/free-text allergies, pregnancy/lactation, organ function, food/alcohol.

## Demonstration rules (all DEMO / NOT CLINICALLY VALIDATED, Draft, never approvable)
| Id | Domain | Criteria | Severity / urgency |
|---|---|---|---|
| DEMO-INT-001 | Interaction | reference entry ≥ Moderate between two active reference medicines | Moderate / Soon |
| DEMO-ALG-001 | AllergyConflict | medication-linked allergy equals/shares ingredient with an active medicine | Major / Soon |
| DEMO-DUP-001 | DuplicateIngredient | ≥2 active medicines share an ingredient id | Moderate / Routine |
| DEMO-DQ-001 | DataQuality | active medicine not in reference or without ingredients | Informational / Routine |
| DEMO-SYM-001 | ReportedSymptom | symptom recorded Severe, unresolved, onset ≤7 days | Moderate / Soon |

## External network / paid services
None. No API key, no internet, no server/domain/cloud/vector DB. Local PostgreSQL 16 only (scratch role and databases, dropped afterwards).

## Decisions needed from you
1. Who may be reviewers/authors for real, and which validated source(s) the first approved rules will rest on (nothing can be approved without one).
2. Whether Validated-only evidence for approval is the right bar (current policy), and whether Unverified sources should ever count.
3. Clinical review of the Persian wording and of the `safety.*` messages.
4. Whether unregistered/"free-text allergy" gaps should keep making a result `Incomplete` (current, conservative) or be shown as a softer note.
5. Whether a resolved finding should be re-raised after *any* change of the underlying category (current, coarse) or only on change of the specific records (needs record versions).
6. Authoring/review UI scope (API only now).

## Known limitations and risks
See 07. Notably: stored assessments hold display names (same sensitivity as the record); guidance link state is pessimistic after partial failures; coarse re-raise rule; reference interaction coverage depends entirely on data that is demo here.

## Phase 8 should address
Adherence and monitoring (scheduled re-assessment, workers, notification delivery with real delivery status), sourced emergency-sign content and an `Urgent` path, authoring/review UI, condition/dose rules only with full context and sources, record-level versions for finer re-raise, clinical review of fa wording.

**Stopped here, awaiting your explicit approval. Phase 8 has not been started.**
