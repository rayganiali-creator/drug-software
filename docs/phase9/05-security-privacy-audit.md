# 05 — Security, privacy, audit

## Access (see 01 for the route table)
Default deny; RBAC → relationship → per-scope consent (`adr`); the record's own subject is what is authorized; uniform 403. Tested for: the patient and a consenting physician (allowed); a physician without relationship; a physician with a **relationship but no consent** (demo-physician ↔ demo-patient-2); a pharmacist without consent; another patient; industry, researcher and system-admin accounts (all refused); a missing record vs a hidden record (identical response); no body field can name another subject; the queue returns an **empty list** (not an error) to a physician with no access and never contains records of patients without consent.

Not changed: the Phase 3 role→permission mapping, the consent evaluator, the Phase 5 manufacturer-report two-step review and its identity-free payload (existing tests pass unchanged).

## No automatic sharing
There is no code path from an ADR record to an insurer, a manufacturer, a regulator or any integration. The ADR assembly has no reference to Integrations, ProductTrace or an HTTP library (tested). The existing `InsuranceSharing` and `ManufacturerReport` purposes are untouched. **Future insurance access, a manufacturer link or any reporting adapter would have to be separately justified, authorized, minimized and scoped, with consent and authorization re-checked at disclosure time.** A simulated acknowledgement is not an acknowledgement from a real organization; no acknowledgement concept exists in the ADR module.

## Audit
Actions: `ADR_RECORD_CREATED`, `ADR_ENTRY_ADDED`, `ADR_CLASSIFIED`, `ADR_STATUS_CHANGED`, `ADR_ASSISTANCE_GENERATED`, `ADR_ACCESSED` (professional reads and the queue; the queue entry carries only a count). Each line has actor, subject, resource id, source, correlation id and **only** enum names/counts as metadata (role, kind, to-status, to-category, medication count). A test writes distinctive text into the description, a medicine name, a note and a reason and asserts that none appears anywhere in the audit log. Health text is stored only in the module's own tables.

## Other
* Free-text limits (2,000 / 500 characters), control characters rejected, at most 10 medicines per record.
* Rate limiting reuses the existing write limiter on all writes.
* **A free-text screening is not anonymization**, and no screening is claimed: the record contains whatever the reporter wrote. Do not use real data.
* No outbound call exists in the module or in any Phase 9 test.
* Not production-ready: no DPIA, no penetration test, no retention policy for adverse-event records, no emergency path (the page says it does not send urgent alerts and tells the patient to contact emergency services — no emergency criteria were invented).
