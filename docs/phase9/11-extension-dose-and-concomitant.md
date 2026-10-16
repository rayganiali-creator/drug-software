# 11 — Phase 9 extension (combined 9 + 10 session)

The combined brief asked the internal adverse-event record to also support "relevant available dose/administration details, concomitant medicines, and outcome where known". Outcome (`outcomeAsStated`, plus later supplements) already existed. Added, **additively** (migration `AddAdrDoseAndConcomitant`):
* `DoseAndAdministrationAsReported` — free text up to 500 characters, part of the immutable original, never completed or corrected by the system. Missing → `dose.not_stated`.
* `ConcomitantMedications` — up to 10, each a link to **the patient's own** medication record (verified) or a name as given; stored as part of the original; **separate from suspected medicines** so listing a medicine never implies suspicion. Missing → `concomitant.not_stated`. The template assistance says "none named (this is not the same as none taken)".
* Web form and detail page show both; the original block shows the dose text as written.
Everything else in Phase 9 is unchanged. Basis: **[DESIGN]** (brief); no source defines these as mandatory fields and none is mandatory here.
Tests: one new service test (`Dose_and_other_medicines_are_kept_as_reported…`) and one new web test; the Phase 9 browser QA (178 checks) was re-run green.
