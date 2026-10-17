# Industry analytics — focused privacy review

Start: commit `c1d9a76`. Synthetic data only, no network, no external service. **The analytics are not guaranteed anonymous**; nothing here is a claim of anonymity, legal compliance or complete protection. Changes are uncommitted in the working tree pending your review.

## 1. What an industry user can see (current code)

`GET /organizations/{org}/industry/insights?period=yyyy-Qn&basis=Real|SimulatedOrDemo`. Membership of the organization is required (organization-kind permission `industry.insights.read`); everything else is 403. The answer is four one-attribute tables (product, problem type, patient-stated severity, month sent) of **counts** of patient-submitted manufacturer reports that reached `Sent`/`Acknowledged`, for the manufacturer(s) linked to the organization by configuration. No patient, batch, text, date of birth or exact date is returned; ADR and pharmacy-safety records are never read. The answer is a pure function of (organization, period, basis); there is no per-user state.

## 2. Threat model

Attacker: a **legitimate, authorized** industry user (or several users of one organization pooling queries) who wants to learn something about a small group the tables hide: that a particular patient reported, or the exact size of a group of fewer than *k* reports / *k* reporters. They can call the endpoint any number of times, at any time, and they know the data model. Out of scope: stolen accounts, database access, collusion with the manufacturer's own knowledge of its customers (see §8).

## 3. Attacks tried on the code at `c1d9a76` and what happened

| # | Attack | Result before | Result now |
|---|---|---|---|
| 1 | **Subtract visible cells from a total from another table.** Fixture: problem type A = 20 reports from 2 people (hidden: few reporters), B = 40 from 40 people; the month table shows 60. 60 − 40 = **20**. | **Succeeded** (exact count of a cell hidden for having few reporters; only one cell was hidden) | **Blocked**: when anything is hidden, at least two groups are hidden and together hold ≥ *k* reports (next-smallest visible group is hidden as well). The attacker learns only a sum (60), not 20. |
| 2 | **Overlapping date ranges.** March = 3 reports (hidden in the month table). Query Jan–Feb and Jan–Mar and subtract → **3**. | **Succeeded** | **Blocked**: no caller-chosen range exists. Only fixed calendar quarters (`yyyy-Qn`); `from`/`until`, months and half-quarters are rejected (400). Quarters are pairwise disjoint (tested), so no two answers overlap. |
| 3 | **Repeat the same closed period before/after a late-arriving report.** Cell hidden at 4 reports; one more report about an August problem is sent on 5 October; the same query now returns different numbers (the hidden group becomes visible: "was 4 before"). | **Succeeded** (the "closed" period changed) | **Blocked**: reports are placed by the month they were **sent** (system time, cannot be back-dated), and a quarter is published only after a closure delay (default 7 days). A late report lands in a later quarter; a published quarter returns byte-identical answers forever (tested, including after months of clock advance). |
| 4 | **Combine tables that suppress different groups / change filters.** | The only filter is the time range (see 2). Cross-table combination is attack 1. | Covered by the ≥2-hidden rule; random-shape property test (500 cases) asserts a lone hidden group is never recoverable. |
| 5 | **Patient edits a product after its report was sent** (corrects the batch; the stored manufacturer link is cleared). The published cell drops 7 → 6, revealing one report moved. Found during this review, not in the original list. | **Succeeded** (test failed at 6 ≠ 7 on the old code) | **Blocked**: a product with a sent report cannot change medicine or manufacturer (`product.locked_after_report`); ordinary corrections (note, batch) still work. |
| 6 | **Repeat requests to watch counts change.** | Possible through attacks 3 and 5. | Not possible once those are closed: identical answers for identical requests. No other changing input exists inside a published quarter. |
| 7 | **Several accounts of one organization.** | Not an attack: the answer has no per-user input (tested: identical). Pooling accounts gives nothing a single account lacks. | Same. |

Every "Blocked" row has a regression test (`IndustryAttackTests`, `Phase15DisclosureTests`, `Phase15WindowTests`, `Phase15Tests`, `Phase15ApiTests`). The attack tests were written first, asserting the *defended* outcome, and were run against the old code: attacks 1, 2, 3 and 5 failed there (the attack worked); 4/7 are not attacks.

## 4. Mitigation options and trade-offs

| Option | Prevents a demonstrated attack? | Cost / trade-off | Decision |
|---|---|---|---|
| Fixed non-overlapping periods (calendar quarters) | Yes, attack 2 | Coarser reporting; no ad-hoc ranges | **Implemented** |
| Closed-data window (publish after a delay) + place by send month | Yes, attack 3 | Data appears up to ~3 months + 7 days late; month = month sent, not month of the problem | **Implemented** (delay configurable) |
| Consistent / complementary suppression (≥2 hidden, sum ≥ *k*) | Yes, attack 1 | Hides some larger-than-*k* groups (documented on the page) | **Implemented** |
| Freeze attribution after sending | Yes, attack 5 | A patient can no longer re-attribute a product that already has a sent report | **Implemented** (small change in `ProductTraceService.UpdateAsync`) |
| Rate limiting | No (output is deterministic; repeating gives nothing new). Useful only for load | Extra policy/config | Not added. The insights route is covered by no limiter today (verified); that is a load concern, not a disclosure one |
| Auditing repeated/overlapping queries | Detects, does not prevent | Already one audit line per read (org, basis, period, user id) | Period added to the audit line; overlap monitoring unnecessary now that overlap is impossible |
| Rounding | Would hide ±1 changes | Distorts totals; weak alone | Not added (no demonstrated attack remains that it would stop) |
| Noise / differential privacy | Would bound repeated-query risk | Needs a stated mechanism, ε/δ, per-person contribution limit and a query budget; none of that is defined or tested here | **Not implemented**; needs a privacy expert |

## 5. Implementation summary

* `IndustryInsightsService.GetAsync(org, period, basis)`; options `ClosureDelayDays` (7), `MaxPeriods` (8), `MinCellSize` (5, floor 3). Month counts are separate range queries (no provider-specific date functions).
* `DisclosureRule.Apply`: primary suppression (reports < *k* or reporters < *k*) then complementary (≥ 2 hidden, hidden reports ≥ *k*).
* Endpoint: `period` param; `from`/`until` rejected; audit line carries org, basis, period.
* `ProductTraceService.UpdateAsync`: lock after a sent report.
* Web: quarter selector (no date inputs), period badge, explanation text; new i18n keys (Persian awaiting review).
* Files: see the report; no migration (no schema change).

## 6. Limitations of what was implemented

* **Not anonymity.** Each published cell is a count ≥ *k* reports from ≥ *k* distinct patient ids; that is a convention, not a guarantee.
* The sum of hidden groups per table is still derivable (that is intended); a hidden *pair* could still be partly narrowed by outside knowledge.
* Product/problem/severity/month are independent one-way tables, but an attacker with outside knowledge (e.g. the manufacturer knows which customers called them in a given month) can narrow a cell.
* "Distinct reporters" counts patient ids. A person with several accounts, or a household, counts as several.
* Attribution to a manufacturer is patient-asserted; counts are unverified and can be inflated or poisoned.
* The 7-day closure delay is a design choice, not derived from any rule; a report in flight at the boundary is placed by its own send time and cannot change an already-published quarter, but quarter *publication* itself reveals when the cut-off happened.
* A product record that is hard-deleted (not possible while a non-cancelled report exists) or a patient-erasure process, if one is built later, would change published counts; it must be designed with this in mind.

## 7. Decisions that need a privacy expert / legal review / product owner

1. The value of *k* (5 is a placeholder), whether a reporter threshold is meaningful, and whether noise or rounding is required before any real use.
2. Whether quarterly, month-sent counts are an acceptable product at all for a manufacturer, and under what lawful basis and consent (matrix Q-14). No legal requirement is asserted here.
3. Contribution limits per patient and a query budget if differential privacy is ever adopted.
4. Behaviour on patient data erasure/withdrawal of consent for already-published aggregates.
5. The product decision to lock manufacturer/medicine edits after a report is sent (patient-facing consequence).
6. A DPIA / re-identification assessment with realistic small populations.

## 8. Tests, commands, results, skips

See the report appended to the chat (`INDUSTRY_PRIVACY_REVIEW_REPORT.md`) for exact commands and counts. Environment limits: IntegrationTests (3) need Docker and are skipped; Python `ai/` tests need pytest (not installed, no network); no Android/iOS device tests. PostgreSQL tests ran against a local scratch database only.
