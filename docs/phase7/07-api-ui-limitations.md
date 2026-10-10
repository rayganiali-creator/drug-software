# 7. API, UI, limitations

| Method + route | Permission | Notes |
|---|---|---|
| POST `/patients/{id}/safety/assessments?locale=fa\|en` | safety.assess.run | 201; 503 `assessment.unavailable`/`persistence.failed` |
| GET `/patients/{id}/safety/assessments[?take]` / `/latest` / `/{aid}` | safety.assess.read | `outdated` + reasons on latest/get |
| GET `/safety/coverage` | safety.assess.run | counts, covered/uncovered/unsupported domains |
| GET `/clinical-rules`, `/{ruleId}/versions/{v}` | clinicalrules.read | |
| POST `/clinical-rules`, `…/submit`, `…/review`, `…/retire` | author / author / review / retire | 409 on wrong state, 403 `review.separation_of_duties` |

## Clients
Web (`/app/patient/safety`, `/app/physician|pharmacist/patients/:id/safety`, `/app/physician|pharmacist/rules`) and Flutter (`/profile/safety`, patient only). States: completed with findings, completed with no matches (coverage limits shown), incomplete (named reasons), no approved coverage, failed, blocked by policy (own state), technical unavailable ("does not mean nothing was found"), not connected, empty, outdated, demonstration (badge + dashed border + "not approved advice"). Severity, status and demo are words with icons, never colour alone. Provenance, rule id/version, evidence validation, publication-date-not-recorded and timestamps are in each finding's details. The check runs only on request.

## Limitations (explicit)
No dose/route/schedule/adherence/lab/condition/pregnancy/organ rules; interaction coverage is whatever the reference holds (demo data here); allergies by free text or ingredient text are unmatched; paused medicines are not checked; no authoring/review UI; Persian clinical wording unreviewed; professional guidance locale defaults to `en` unless `?locale=` is given; a stored view hides findings the viewer may not read but the stored row itself is unredacted; guidance link state is pessimistic after a partial failure; no real clinical source, reviewer or approved rule exists, so every real assessment today reports `NoApprovedCoverage`.
