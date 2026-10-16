# 02 — Workflows and states

## A. Dispensing error / near miss log
* **Two kinds, kept apart by whether it reached the patient.** `NearMiss` + "reached the patient: Yes" is refused (`near_miss.reached_patient_conflict`); `DispensingError` + "reached: No" is refused (`dispensing_error.not_reached_patient_use_near_miss`). "Not known" is allowed for either and is listed as missing. This pairing rule is a **design proposal** built on the FIP near-miss wording ("has the potential to cause harm, but has not … intercepted and corrected").
* **Example tags (optional, several allowed):** the eight examples of FIP §5.2.1 (see `03`) plus MedSmarter's own "Other (not one of the examples)". An empty list stays empty and is listed as `error_examples.not_stated`.
* **Unknown stays unknown:** `occurred_on`, medicine, how noticed/intercepted, immediate action are optional and null when not given; the response lists each as missing (`occurred_on.unknown`, `medicine.not_stated`, `how_detected.not_stated` / `how_intercepted.not_stated`, `immediate_action.not_stated`, `reached_patient.unknown`). No patient is named or looked up; the form tells the user not to enter identifying details (no screening is claimed).
* **Idempotent:** a retry with the same client request id returns the record already made (unique per organization + reporter + id).
* The medicine is **free text as named**; nothing is matched to the reference.

## B. Product-concern intake
* **Type** (provisional MedSmarter labels, `TypeBasis = ProvisionalNotSourceDefined`): appearance or packaging, suspected quality problem, labelling or naming, apparent lack of effect, other. The sources do not define "product-quality defect" or "lack of effect"; these are intake labels only.
* **Linked** — when the caller passes a ProductTrace record they may read: product name, batch, expiry, manufacturer, GTIN are **copied as a snapshot** with the record's version and verification state at that time (identifiers only; the patient's id is stored for audit tracing but **never returned**). A later edit of the patient's record does not rewrite the snapshot (tested).
* **Unmatched** — no record given: the identifiers typed by the reporter are kept **exactly as typed**; missing ones are flagged (`product_record.not_linked`, `product.name_missing`, `batch.missing`, `expiry.missing`, `manufacturer.missing`). Nothing is looked up or chosen.
* **Linked and typed values both present:** both are kept and a difference is flagged (`batch.differs_from_linked_record`, `product.differs_from_linked_record`, `expiry.differs_from_linked_record`); the system never picks one.
* A concern is **distinct** from ADR and dispensing-error records. Optional **links** to an ADR record (host-verified) or to a safety event (same organization) record a justified relationship; they copy nothing.
* Every concern carries the notice "A product concern is a suspicion. It does not show that a product is defective …". No "confirmed" or "defective" state exists.

## C. Status machine (both kinds) — [DESIGN]
`Submitted → InReview | Closed`; `InReview → FollowUpRequired | Closed`; `FollowUpRequired → InReview | Closed`; `Closed → InReview`. Closing and reopening need a note. Status changes need the expected `version` (409 on a stale one). Review notes and follow-ups are entries; the original is never edited. Reviewers are Pharmacist or PharmacyAdmin of the same organization; the reporter may also review (a one-person pharmacy must be able to close its own record). [OPEN] whether an independent reviewer should be required.

## D. Manufacturer-report note
Allowed only on a concern that is **under review** (not on a first report, not closed), once, with a required note and the current version. It sets a flag and adds a history entry. It **creates, queues and sends nothing** (see `05`).
