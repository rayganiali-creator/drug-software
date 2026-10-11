# 4. Delivery states, provenance, urgent findings

| State | Meaning | Never means |
|---|---|---|
| (no row) | the message exists in the app only | a notification was scheduled |
| `Scheduled` | a delivery should be attempted | sent |
| `Attempted` | an attempt started (written *before* the channel call) | delivered |
| `Confirmed` | the channel confirmed — with `Mode = Simulated` it only means the local simulation accepted it | seen, read, or (if simulated) that anything left the system |
| `Failed` | gave up after the attempt limit or a permanent refusal; the message is still in the app | the message is lost |
Guidance lifecycle (`Sent/Seen/Reviewed/Referred/Resolved`) is unchanged and separate: delivery never sets `Seen`; only the patient's own action does.
* The only channel is `LocalSimulatedChannel` (name `local-simulated`, mode `Simulated`): it sends nothing. UIs say "Simulated notification: nothing real was sent to any device"; audit lines carry `simulated`. A real channel needs a new, separately approved adapter; none exists.
* A channel receives only `DeliveryRequest(messageId, patientId, level, locale)` — never message text or health data.
* Provenance per message: `AutomatedAssessment` (a rule matched) vs `PatientReported` adherence vs `ClinicianReviewed` (own flag, true once a professional moved it to Reviewed). They are never merged.
* **Urgent findings.** Delivery and scheduled checks are *routine*. A manual assessment still creates the in-app message immediately and synchronously; a finding is never held back for the worker. There is **no emergency delivery path**: no real channel exists and no sourced emergency criteria exist (Phase 7: messages are capped below the Urgent level). Nothing is invented here; this is a documented gap for Phase 9.
