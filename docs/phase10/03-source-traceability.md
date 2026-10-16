# 03 — Source traceability

## What was verified, and how
The FIP report (*Patient safety: Pharmacists' role in "Medication without harm"*, 2020) was supplied as a PDF in the session; **it is not stored in the repository** (copyright). The relevant passages were read from its extracted text again during this work:
* **§5.2.1 "Common dispensing errors"** introduces its list with "There are many kinds of errors that can happen when dispensing medicines to patients, many of which occur because the pharmacist has not performed an adequate final product check or failed to perform required patient counselling. **For example:**" and then lists eight bullets. They are **examples**, not a complete or official list.
* **§1.1** defines near miss ("a medication error that has the potential to cause harm, but has not, either by luck or because it was intercepted and corrected").
Printed page numbers in the extracted text were inconsistent (the list appears under a header reading p. 29; its introduction is on the preceding page), so the code cites "list on printed p. 29, introduction on the preceding page; **page numbers to be re-checked against the original**".

| Element | Class | Basis |
|---|---|---|
| Eight example tags (incorrect medicine/form/strength; label incorrect or inconsistent with the prescription; incorrect quantity; expired medicine; incorrectly compounded; interaction not detected or allergy not documented; someone else's prescription medicine; different brand leading to duplication) | **[SUPP]** FIP §5.2.1 — wording kept in `PharmacyDefinitions.Examples`, each with document/section/page | verified text |
| "Other (not one of the examples)" tag | **[DESIGN]** | not from FIP; labelled so |
| Near-miss concept and definition | **[SUPP]** FIP §1.1 | verified text |
| "Dispensing error" as a kind | **[DESIGN]** | FIP lists dispensing errors by example only; no formal definition is quoted |
| Reached-the-patient yes/no/unknown and its pairing with the kind | **[DESIGN]** | derived from the near-miss wording |
| Free-text fields (description, medicine as named, how noticed, action taken) | **[DESIGN]** | no source field list exists |
| Status set, history, closure reason | **[DESIGN]** | none in any source |
| Product-concern types | **[DESIGN]/[OPEN]** | provisional; **no source defines product-quality defect or lack of effect** |
| Linking to ProductTrace; snapshot; unmatched rule | **[DESIGN]** (reuses Phase 5 records) | brief |
| LASA names/packaging as a medication-error risk; shortages/recalls | **[SUPP]** FIP §7.3, §5.1 — context only; "labelling or naming" is a provisional intake label inspired by §7.3, not a FIP category | |
| Blame-free culture and error reporting/learning | **[SUPP]** FIP §8.1 — reflected in neutral wording and the "not a regulatory report" notice | |
| Anything about national reporting of dispensing errors or product concerns | **[OPEN]** | no supplied material describes an Iranian pathway |
| Medication-error **types** of FIP Figure 1 (omission, commission, prescribing, transcribing, … ) | not used | the figure's grouping could not be verified from flattened text; avoided rather than guessed |

Lecture slides (S1–S3) contain **nothing** about dispensing errors or product concerns beyond S1's picture of the WHO "Medication Without Harm" wheel (action areas "product quality & safety", "naming, labelling & packaging", "prescribing, preparation & dispensing"), which motivates the topics but defines no category.
