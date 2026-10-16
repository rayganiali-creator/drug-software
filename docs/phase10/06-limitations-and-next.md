# 06 — Limitations, deferred requirements, blockers, next dependencies

## Known limitations
* No Flutter screens for Phase 9 or 10 (web only); the Flutter app received only the regenerated catalog and strings.
* The product-record link in the web form takes **ids** (patient id, batch record id): there is no patient or batch picker, because a pharmacy-side patient lookup was out of scope. Without ids the concern is saved as unmatched.
* No dispensing-error link to a specific patient or prescription (deliberately: no patient identifiers in the log).
* No independent-reviewer rule, no sampling, no trend or analytics view, no export. Reports are not de-identified analytics (none exist).
* The organization is the caller's **first** membership in the UI; a user with several pharmacies has no switcher.
* Persian wording is awaiting clinical review in full (`docs/phase9/06`, extended by the `ph.*` keys); there are no Persian definitions of near miss.
* Page numbers of the FIP citations are to be re-checked against the original PDF.
* Not clinically validated, not production-ready; no retention policy; no DPIA.

## Deferred (documented, not built)
Pharmacy-initiated manufacturer report; any external adapter (mock or real) for pharmacy safety events; FIP Figure 1 stage tags; trend analytics and signal detection; counselling/medication-history prompts; LASA-specific workflow; integration with inventory or dispensing records; Flutter parity; emergency/urgent escalation.

## Blockers needing an authoritative source or approval
* National reporting pathway, forms, deadlines and who may report (pharmacy, patient, physician) — **[OPEN]**.
* Taxonomy for product-quality defect, substandard/falsified product, lack of effect — **[OPEN]**.
* Legal basis and agreements for any transmission; consent model for pharmacy-originated reports — **[BLOCKED]**.
* Clinical review of the pairing rule, the example tags' wording and the status set; named Persian wording reviewer — **[BLOCKED]**.

## Dependencies for the next phase
A decision on the pharmacy's role in reporting; the answers above; a reviewer registry for wording; a patient/batch picker design that respects relationship + consent; retention policy.
