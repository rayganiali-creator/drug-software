# Phase 13 — Web application completion (bounded)

> Not production-ready. The web client holds no business rule; the server decides access and state.

## 1. Audit
* **Routes vs navigation:** a test (`phase13.test.tsx`) walks every role's navigation (`navByRole`) and requires a matching route in that role's group. **No dead link was found** for the seven role groups (patient, physician, pharmacist, pharmacy, industry, admin, workspace). It also asserts professional-only workspaces are absent from the patient navigation and the physician navigation does not include the pharmacy safety log, and that a patient opening a pharmacy URL directly is not shown the page.
* **Status honesty:** the manufacturer-report list showed "Sent"/"Acknowledged" next to a separate "mock delivery" badge. A reader could take the status alone as real. **Fixed:** with a simulated delivery the status itself now reads "Sent (simulated — no real recipient)" / "Acknowledged (simulated — not from a real organization)" and the acknowledged state no longer uses the success tone/check icon. Regression test added. (Delivery labels for guidance messages were already correct since Phase 8.)
* **Professional/patient separation and organization scoping:** already enforced server-side and covered by Phase 9/10 HTTP tests; the web tests confirm the patient view has no professional panel and pharmacy pages are organization-scoped by the API (no organization id is taken from the page).

## 2. Implemented
* **Patient/batch picker** for pharmacy product concerns (`BatchPicker`): patients come from `/care-relationships` (the caller's **active** relationships only); that patient's batch records come from `/patients/{id}/products` (server: relationship + `products` consent; a refusal shows "not available to you … nothing was linked; you can save as unmatched"); the chosen ids are posted — **the typed-id fields were removed**. No cross-patient search endpoint was created.
* **List filters:** status + text filter on the pharmacy safety log and product concerns; text filter on the adverse-event queue/lists (client-side over the bounded server result, with an explicit "nothing matches" state). Pagination: the server caps lists (100–200); true paging was **not** added (documented).
* i18n: new `ph.pick.*`, `*.filter.*`, `rec.rep.status.*.simulated` keys in the single source; Persian wording awaiting clinical review.

## 3. Not done / needs review
* Pagination beyond the server caps; server-side search.
* Role dashboards were not redesigned; industry views are Phase 15.
* Broader a11y pass beyond the existing axe browser QA of the ADR and pharmacy pages (re-run in the final verification).
* Wording for consent/report-review states is unchanged and **still needs clinical/legal wording review**.

## 4. Tests
`phase13.test.tsx`: 8 tests (nav/route audit; patient navigation; direct URL guard; picker success; picker refusal; pharmacy filters; ADR filter; simulated-status labels). Exact suite results are in the repository-wide review.
