# 4. Completeness and freshness

Per input category (profile, medications, allergies, symptoms) the snapshot records availability (`Available`, `NeverRecorded`, `NotAuthorized`, `Unavailable`), last update, record count, staleness (from Phase 5 freshness) and source.

| Situation | Outcome |
|---|---|
| required input never recorded / not authorized / unavailable / absent | **MissingData** (`input.<category>.<why>`), never "no match" |
| required input stale, nothing matched | **StaleData** |
| required input stale, something matched | Matched, finding flagged `inputsStale` (+ lower confidence in the message) |
| age-limited rule, age unknown | MissingData `input.age_unknown`; age outside → NotApplicable |
| fewer than two medicines checkable in the reference (but ≥2 taken) | MissingData `reference.insufficient_for_comparison` |
| some medicines not in the reference | result partial (`medication.not_in_reference`), real findings kept |
| only free-text allergies | InsufficientEvidence; free text beside linked allergies → partial |
| reference entry with unknown severity | InsufficientEvidence; rejected entry ignored and reported |
| conflicting reference severities | highest kept, `evidenceConflict` stated |

**Status**: `NoApprovedCoverage` (no Active rule) › `CompletedWithFindings` (a finding from an Active rule) › `Incomplete` (an Active rule is Missing/Stale/Insufficient/Error) › `CompletedNoMatches`; `Failed` when the safety layer objects. `complete` is true only if no active rule has a gap or partial coverage. `safetyClaimAllowed` is always false; every result lists unsupported domains (doses/route, adherence, labs, condition contraindications, ingredient/free-text allergies, pregnancy, organ function, food/alcohol).
