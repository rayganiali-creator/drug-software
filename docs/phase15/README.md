# Phase 15 — Pharmaceutical-industry workspace and safe analytics

No external service, paid resource, server or outbound call was added. All data shown is local.

## What exists

One read-only endpoint and one web page for the `PharmaceuticalCompany` role:

`GET /organizations/{orgId}/industry/insights?basis=Real|SimulatedOrDemo&from=yyyy-mm-dd&until=yyyy-mm-dd`

It returns **counts of patient-submitted manufacturer product reports that reached `Sent`/`Acknowledged`**, for the manufacturer(s) linked to the caller's organization, in four independent one-attribute tables: by product, by type of problem, by patient-stated severity, by month.

## Boundaries (each has a test)

| Rule | How |
|---|---|
| Organization-scoped | New permission `industry.insights.read` has kind *organization*: the actor must be a member of the organization in the path. Non-member, wrong role and unknown organization all get the same 403. The older `analytics.read`/`signals.read` permissions are *aggregate* kind (no organization check) and **no route uses them**; they stay unused. |
| Product-scoped, fail-closed | The organization → manufacturer link is configuration only (`IndustryInsights:Organizations:{orgId}:0 = <manufacturer id from the medication reference>`). No link → `linked:false` and no data. Nothing is guessed from names. |
| No identifiers or free text | The response type has no patient id, batch, description, reference, exact date or typed product name. Products outside the reference are lumped into one "not in the reference" row. A test serializes the result and asserts the seeded batch number and free text are absent. |
| Small-group suppression | A cell is hidden when it has fewer than *k* reports **or** fewer than *k* different reporters (default 5, `IndustryInsights:MinCellSize`, floor 3 — a lower configured value is raised). A hidden cell carries no number. Tables are ordered by name, never by size. |
| Bounded | Window 28–366 days (too short would isolate single reports); at most 25 products (flagged `productsTruncated`); counting happens in the database, no rows are loaded. |
| Real vs simulated | `basis=Real` counts only non-mock, non-demo reports/products. `SimulatedOrDemo` counts the rest. They are never added together and the page shows a warning banner and badge on the simulated view. Legacy prototype pages (trends, signals, experience, reports) under the same role now also carry a visible DEMO badge and say MedSmarter does not detect or confirm safety signals. |
| No causality, no defect inference | Notice on every view: patient-submitted, unverified, not proof of a defect or causation, not an incidence. Labels are the reporter's categories, never MedSmarter's conclusion. |
| No sending | There is no route here that sends, exports or creates a report; the manufacturer-report outbox is untouched. |
| Audited | Every read writes `INDUSTRY_INSIGHTS_ACCESSED` with the organization id and basis only. |
| Replaceable | The contract is `IIndustryInsightsService` (ProductTrace.Contracts); the disclosure rule is a separate pure function (`DisclosureRule`). |
| Separate from pharmacovigilance records | Adverse-event records (ADR module) and pharmacy safety records are **not read at all**. The industry role has no route to them (tested: 403). |

## Known limits (not hidden)

* **Cross-table differencing.** Each table is suppressed independently. A reader could combine tables (e.g. a total from one table minus visible cells in another) to infer a hidden small cell. No totals are published, windows cannot be narrowed below 28 days, and every read is audited, but this is a residual risk that needs a privacy review before any real use.
* The link configuration is manual; there is no admin screen. With no link (the default, including the demo account) the page says so and shows nothing.
* Real data will be empty today: no real manufacturer delivery exists (the provider is a mock), so every `Sent` report is simulated. The "Real" view is correct but will show nothing until a real, approved delivery exists.
* Nothing says what may lawfully be shared with a manufacturer (matrix Q-14). The view is therefore minimal and counts-only.
* Persian wording awaits clinical/legal review (`AwaitingClinicalReview`).

## Tests

* .NET `Phase15Tests` (6): disclosure rule, link/no-link, other manufacturers invisible, drafts not counted, unregistered names hidden, real vs simulated, bounds and the k floor, month grouping. `Phase15ApiTests` (3): 401, membership + role matrix + audit, no patient/ADR/pharmacy routes. Also passing on PostgreSQL.
* Web `phase15.test.tsx` (6).
