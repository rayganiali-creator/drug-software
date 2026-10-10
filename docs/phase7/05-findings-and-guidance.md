# 5. Findings and guidance integration

**Finding** = key (stable hash of rule + version + exact records), rule id/version, domain, severity, urgency, actionable, demo flag, subjects (labels only; symptoms carry no text), evidence refs, reference severity, conflict/stale flags, limitations, emergency signs (only if sourced and rule Active; none exist).
Patient wording comes from Guidance templates `safety.interaction | allergy_conflict | duplicate_ingredient | unregistered_medication | symptom_reported` (fa/en, six parts: observed, why, next step, when to contact a pharmacist or physician, no unsourced emergency text, basis). They never tell anyone to start, stop or change a medicine ("please do not change anything on your own") and pass the Phase 5 `PatientMessagePolicy`. **Persian clinical wording is marked unreviewed** (banner in both clients).
Because Phase 7 has no sourced emergency signs, messages are never raised at the `Urgent` level (urgency Prompt is capped at ReviewSoon in the message; the assessment still shows Prompt).

## Reuse of the Guidance module (no competing alert system)
Additive only: nullable origin columns on `guidance.guidance_message` (kind, assessment id, rule id/version, finding key, data-as-of; migration `AddGuidanceOrigin`), `GuidanceMessageDto.Origin`, `IGuidanceService.CreateFromOriginAsync` and `ListOpenByOriginAsync`. Lifecycle Sent/Seen/Reviewed/Referred/Resolved is unchanged; **creating a record never sets Seen** and the UI labels `Sent` as "New". Messages are created only for actionable findings, in the requester's locale.
* One open message per finding key; a resolved finding is raised again only if the data changed after it was resolved (resolved messages are never reopened or deleted).
* Re-assessment never closes anything: findings that no longer match are reported in `openGuidanceNoLongerMatching` (only when that rule positively evaluated to NoMatch); a person decides.
* No insurer sharing, no notification, no worker.

## Non-atomic limits (documented, tested)
Assessment store, guidance store and link-state update are separate. Order: store assessment (link state pessimistically `Failed` if actionable findings exist) → create messages → update link state. If messages fail, findings stay visible, the state says `Failed`/`Partial`, and a re-run repairs it without duplicates. If the assessment cannot be stored, nothing is shown and no guidance is created. If only the final link-state update fails, the row keeps the pessimistic `Failed` although messages exist; a re-run reports `existing`.
