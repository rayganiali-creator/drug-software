# 05 — Manufacturer-reporting boundary

The existing Phase 5 pipeline (patient-owned draft → consent → **human review** → whitelisted, identity-free payload → **mock** outbox → simulated acknowledgement) is **unchanged**. Phase 10 does not call it.
What Phase 10 adds is a **human decision note** on a product concern: "a manufacturer report may be worth considering", with a required reason, allowed only after review. It:
* creates no report, no draft, no outbox entry and contacts no one (tested: the outbox queue totals are identical before and after);
* does not claim a defect or causality (the notice and the "provisional" type basis stay visible);
* leaves the decision with the **patient**, whose own draft, consent and review are required by the existing pipeline; consent and authorization are rechecked there at disclosure, as before;
* does not weaken minimization: the payload whitelist and its identity-free test are untouched (the Phase 5 report tests ran green).
A pharmacy-initiated report, a manufacturer or regulator adapter, or any real transmission remains **[BLOCKED]**: it needs an authoritative reporting specification, a legal basis, a signed agreement or channel, and clinical/privacy review. The existing provider stays `Mock` and the UI continues to label delivery as simulated. Nothing is shared with insurers.
