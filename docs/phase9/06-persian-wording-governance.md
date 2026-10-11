# 06 — Persian wording governance

Statuses: **Draft**, **AwaitingClinicalReview**, **Approved** (only by an identified, qualified reviewer — none exists), **Retired**. The same enum (`WordingStatus`) is in the API contract.

| Item | Where | Status | Reviewer | Approved on |
|---|---|---|---|---|
| 135 `adr.*` UI strings (Persian column) + `nav.adverse-events` | `design/i18n/strings.mjs` | **AwaitingClinicalReview** | — (none identified) | — |
| 14 ADR-related error texts (`rec.err.*` added in Phase 9) | same | **AwaitingClinicalReview** | — | — |
| Signal notice, internal-record notice, assistance notice (Persian) | same (`adr.signal`, `adr.internal`, `adr.assist.notice`) | **AwaitingClinicalReview** | — | — |
| Category labels (`adr.cat.*`) | same | **AwaitingClinicalReview** | — | — |
| Persian rendering of the five definitions | **does not exist** — the definitions are shown in the English source wording | not written | — | — |
| Server-side notices (`AdrNotices`) | `AdrContracts.cs`, English only | English source; every response carries `persianWordingStatus = AwaitingClinicalReview` | — | — |

Visible behaviour: with the Persian locale every ADR page shows the existing banner "Persian wording not yet reviewed" (`sf.fa.unreviewed.*`); the API reports the status on every record. The definitions panel says they are quoted from supplementary documents, not lecture slides.
No reviewer identity or approval was invented; the `Approved` state is unreachable today because no reviewer registry exists. **A reviewer registry and an approval workflow for wording are a decision needed from you** (see 10).
Persian terms were chosen from the vocabulary of the existing screens; the only Persian pharmacovigilance wording seen in the sources is the screenshot of the manufacturer instruction ("عوارض ناخواسته داروها"), which is unverified. [OPEN] preferred Persian terms for ADR, adverse event, ADE, medication error, near miss.
