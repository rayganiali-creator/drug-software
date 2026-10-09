# 03 — Batch/lot tracing and manufacturer reporting

## Batch / lot records — IMPLEMENTED (manual entry), scan = NOT BUILT
A person records the batch number and expiry of a package they hold (`POST /patients/{id}/products`). Rules:
* `gtin` is optional and **typed by the person**; it is format-checked (length and GS1 check digit; nothing is looked up) but **never generated or guessed**.
* The record is compared with the reference **only to flag**, never to overwrite: `expiry.expired`, `received_after_expiry`,
  `gtin.differs_from_reference`, `manufacturer.differs_from_reference` (shown as plain sentences).
* Verification is `SelfReported` until a pharmacist/physician with the right relationship confirms it (`/confirm`, `confirm.professional_only`).
* `POST …/products/scan` answers **501** `not_available`: the endpoint exists as a *connection point only* (no scanner code).
  The UI shows the button disabled with "future connection".

## Manufacturer report — IMPLEMENTED against a **Mock** provider; no real sending exists
Flow: **draft → (person agrees to share) → human review when required → ready to send → sent → acknowledged**.

| Step | Rule |
|---|---|
| Create | needs `manufacturer-report.create`; content is the batch, issue type, severity, date, optional free text; a `clientRequestId` makes retries idempotent (`report.duplicate_request`) |
| De-identify | `ReportPayloadBuilder` writes a **fixed allow-list** of fields (below). The free text passes `FreeTextGuard` (rejects e-mail, URL, long digit runs incl. Persian digits; invisible format characters are ignored when checking). This is a **screening aid, not a guarantee of anonymity**: names, places and rare details are not detected. Other medicines are included only if the person ticks it **and** has the right consent scope |
| Consent | purpose `ManufacturerReport` must be active (grantee-free). It is **re-checked when the outbox sends** against **every scope the frozen payload contains** (batch, plus profile if age band/sex are present, plus medications if other medicines are present); a withdrawn or narrowed consent blocks the send (`consent_revoked`) |
| Review | policy `Sensitive` (default) | `Always` | `None`. Adverse events, severe/unknown severity and concomitant medicines are always reviewed by a physician/pharmacist (`manufacturer-report.review`) — the **patient cannot review their own report** (`review.not_by_patient`); the reviewer sees the de-identified payload, not the identity |
| Freeze | payload stored as text + SHA-256; verified before every send (`payload_integrity` failure otherwise) |
| Send | outbox → `IManufacturerReportProvider`. Only the **Mock** provider exists (acknowledgements are labelled `MOCK-ACK-…`, idempotency keys honoured). `ManufacturerReportsGuard` refuses the Mock provider outside Development/Testing, and with **no** provider configured sending is disabled (reports wait; nothing is lost) |
| Retry | back-off [1, 5, 30, 120, 720] min, 5 attempts, then `Failed`; an admin can retry a failed report (`/retry`) |

### De-identification policy (the allow-list)
Sent fields: `reportReference` (random, not the database id), `productName`, `genericName`, `manufacturerName`, `batchNumber`, `manufactureDate`,
`expiryDate`, `gtin`, `issueType`, `severity`, `occurredOn`, `durationOfUseDays`, `ageGroup` (a band: "0-17", "18-39", "40-64", "65+" or "unknown"; not an age), `sexGroup`,
`concomitantMedications` (names only, opt-in), `description` (screened free text), `isDemo`, `schemaVersion`.
**Never sent:** name, contact details, national/insurance ids, exact birth date, address, user/patient/report database ids, pharmacy name, any consent or audit data.
The person sees "exactly what would be sent" before submitting (web: *Reports → payload*; Flutter: report row).

### Admin queue — IMPLEMENTED
`GET /manufacturer-reports/queue` returns **counts and states only** (no content), the provider (`mock` / none) and a notice that real sending needs a
signed agreement per manufacturer. `POST …/queue/process` runs one batch; `POST …/{id}/retry` re-queues a failed one.

### What a real integration would still need — NOT BUILT
A signed agreement and a technical channel per manufacturer (or a national pharmacovigilance gateway), a legal review of the data-protection
basis, an authenticated provider implementation of `IManufacturerReportProvider`, and operational monitoring of the queue.
