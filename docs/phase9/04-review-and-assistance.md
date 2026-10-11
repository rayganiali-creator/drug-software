# 04 — Professional review and assistance

## Review workflow (physician or pharmacist)
1. Open the queue (`/app/physician/adverse-events`, `/app/pharmacist/adverse-events`) — only records of patients the reviewer may read **now**, or a patient's own list.
2. Open a record: the **original** (never edited), **what is known now** (with provenance), **what is not known**, the history.
3. Actions: add a review note (professionals only), **ask the reporter for information** (patient-visible; moves the record to *InformationRequested*), record a **professional assessment** (free text; labelled "not a grade of seriousness or causality"), set the category (reason required), change the status, add a follow-up or fill unknown values.
4. Everything is an **entry**; nothing overwrites the original report.

Reviewers see every entry and the author ids. Patients see only `PatientVisible` entries (their own follow-ups and supplements, information requests), no author ids, no category, no professional notes or assessments (tested over HTTP and in the UI).

## Four voices, kept apart
| Voice | Where it lives | Label |
|---|---|---|
| Patient's own account | original report, patient follow-ups/supplements | `PatientReported` — "not verified" |
| Professional-entered information | original (if a professional created it), their follow-ups | `ProfessionalEntered` |
| Deterministic findings | **not used in Phase 9** (the Phase 7 engine is untouched; no finding is copied into an ADR record) | — |
| Clinician-reviewed conclusion | notes, assessment, classification entries | `ProfessionalReview` |
| Template assistance | generated on demand, **never stored** | `Template:no-model` |

## The assistance — what it is
`POST /adverse-events/{rid}/assistance` (professionals only) returns a navigation aid built by a **fixed template** from what is already recorded. **No language model is called, no AI provider is configured or contacted, and no network call exists in the code path.** (The brief permits AI to assist; the safest compliant implementation today is a deterministic template behind an interface that a later, gated provider could implement. Adding a real model would be a separate decision under the Phase 6 AI gate.)

It returns: summary lines each tagged with the section they come from; the missing-information codes; an uncertainty list; the notice "This summary was put together by a fixed template … It is not a conclusion and it was not checked by a professional."; the label "Template summary of the record — not a conclusion".
It **cannot**: confirm causality, give a diagnosis, grade seriousness, convert an unverified report into a confirmed event, confirm a defective batch, or decide a report should be submitted. A test checks that the text outside the quoted report contains none of: "was caused by", "is caused by", "confirmed", "diagnos", "serious", "defective", "must be reported", "should be submitted", "probable", "definite". Generating it writes one audit line (`ADR_ASSISTANCE_GENERATED`: ids and origin only) and writes **nothing** to the record (a test compares entry count and version before and after).
When no evidence exists the assistance says what is unknown; it never fills a gap.
