# 02 — The internal record and conservative classification

## What the record is — and is not
| It is | It is not |
|---|---|
| an internal record for human review inside MedSmarter | a submitted regulatory report |
| a **signal** | proof of causality |
| the reporter's own words, kept unedited | a confirmed clinical finding (a patient report is not verified) |
| possibly linked to the patient's own medication record | evidence that a product is defective |
| `ExternalStatus = NotSubmitted`, always | acknowledged by any real organization |

Every response carries `signalNotice` ("This record is a signal for human review. It is not proof that a medicine caused anything.") and `internalRecordNotice`, and the UI shows both.

## Default and categories
Every record starts **Unclassified** and stays so until a *permitted human review action* changes it. Only a professional (`adr.review`, role Physician or Pharmacist) can set a category; a reason is required; the category can be returned to Unclassified; every change is an append-only history entry (from → to, author, time, reason). The patient never sees the category (a professional label).

The only categories are the five terms **defined in the supplementary documents** — none is defined in the lecture slides:

| Category | Definition as quoted | Source [SUPP] |
|---|---|---|
| Adverse drug reaction (ADR) | "A response to a medicine that is noxious and unintended, and which occurs at doses normally used in humans." | WHO, *The global smart pharmacovigilance strategy* (2025), Glossary, printed p. xiii (FIP §1.1, printed p. 6, gives a longer wording; both are in `AdrDefinitions`) |
| Adverse event (AE) | "Any untoward medical occurrence that may present during treatment with a medicine or vaccine, but which does not necessarily have a causal relationship." | WHO 2025, Glossary, printed p. xiii |
| Adverse drug event (ADE) | "Any injury occurring during the patient's medication therapy resulting from either appropriate care, or from unsuitable or suboptimal care." (the FIP report states this includes ADRs and medication errors) | FIP 2020, §1.1 with Figure 2, printed p. 7 |
| Medication error (ME) | "Unintended mistakes in the medicines-use process caused by omissions or commissions, as well as other errors." | FIP 2020, §1.1, printed p. 6 |
| Near miss | "A medication error that has the potential to cause harm, but has not, either by luck or because it was intercepted and corrected." | FIP 2020, §1.1, printed p. 6 |

These were quoted from the PDFs supplied in the session. **The PDFs are not stored in the repository** (copyright); the quoted excerpts are short and cited. Page numbers are the documents' printed page numbers as read; they should be re-checked against the originals. The Persian rendering of each category is **awaiting clinical review** — there is no reviewed Persian definition.

## What is deliberately not a category
* **Product-quality defect** and **lack of effect** — not defined anywhere in the supplied materials. They are *not* categories. A product concern linked to a batch is a **Phase 10 proposal** (ProductTrace already holds its own, Phase 5, issue types; those were never slide-derived and are unchanged).
* **Seriousness** and **causality** — no field, enum, score or algorithm exists (a reflection test fails the build if a property or type name contains "serious", "causal", "Naranjo", "E2B", "MedDRA" or "deadline"). The governing method is still open (Phase 0 decision D-11).
* **Any regulatory submission schema** — none.

## Unknown stays unknown
* A date left empty is stored as null and listed in `missingInformation` (`onset.unknown`, `resolution.unknown_or_ongoing`); it is never defaulted or inferred.
* A medicine is either linked to **the patient's own** record (verified at creation) or kept as a **name as given** (`ReportedNameOnly`) — never matched to the reference, never guessed. If the link cannot be checked at creation the request is refused (`medication.lookup_unavailable`) rather than linked on a guess. If a linked record later disappears the report keeps the name given at the time and shows `LinkedUnavailable`.
* Later information is added as **supplements** that fill only what is still unknown, shown beside the original with their provenance (`SupplementByPatient` / `SupplementByProfessional`); a value already recorded is never replaced (`field.already_recorded`) — a correction is written as a follow-up.
* Missing-information codes: `onset.unknown`, `resolution.unknown_or_ongoing`, `outcome.not_stated`, `medication.none_named`, `medication.not_linked`, `medication.reference_unavailable`, `details.none`. "Stale" is handled as *unavailable* (a linked record that cannot be found) and by showing times (`reportedAt`, `lastActivityAt`); **no staleness threshold was invented**.
