# Pharmacovigilance source material — analysis for AI MedSmarter

> **Update (Phase 9):** the internal adverse-event record was implemented — see [`phase9/`](phase9/README.md) and the product-wide [traceability matrix](pharmacovigilance-traceability-matrix.md), which now carries each requirement's Phase 9 status. This analysis is otherwise unchanged and is still the record of what the sources do and do not say.

**Status: analysis only (as of its writing).** No application code, rules, migrations or existing documents were changed. Nothing in this file activates a clinical rule, authorizes an external report, or defines an official reporting pathway.
**Prepared:** 2026-10-11, from the eight files supplied in the session (see §1). No external service was contacted; no patient data was used.

Labels used throughout:

* **[E] Explicit** — stated in the supplied material (the source is named).
* **[I] Inference** — a suggestion drawn from the material; **not** a requirement.
* **[Q] Question / gap** — needs your clarification or an authoritative source.

---

## 1. What was actually supplied (please read first)

You described the material as "my professor's lecture slides". Only part of it is lecture slides. Reading the files shows:

| # | File (as uploaded) | What it is | Treated as |
|---|---|---|---|
| S1 | `Pharmacovigilance & Pharmaceutical Industry` (11 pp) | Slide deck, Gloria Shalviri (PharmD, MPH, PhD; Research Center for Rational Use of Drugs, TUMS). **Mostly screenshots**: almost no slide text; the content is in pictures of other documents (WHO wheel, EU GVP, Arab GVP guideline cover, an Iranian instruction in Persian, GVP module lists) | Lecture slides |
| S2 | `Pharmacovigilance & Patients` (19 pp) — uploaded **twice** (`Patients` and `Patients_2`, byte-identical) | Slide deck, Dr. Marjan Karimi Ghovanlou (Head of Pharmacovigilance center; Director General for Assessment and Control of Prescribing and Use of Medicines and Health Products, Iran FDA, as printed on the slide) | Lecture slides |
| S3 | `Artificial Intelligence & Pharmacovigilance` (31 pp) | Slide deck, Niayesh Mohebbi (PharmD, Assistant Professor, School of Pharmacy, TUMS). Two figures are taken from a published paper | Lecture slides |
| D1 | `The global smart pharmacovigilance strategy` (WHO, 2025, 90 pp) | Published WHO document | Supporting published source — **not** a slide |
| D2 | `Patient safety — Pharmacists' role in "Medication without harm"` (FIP, 2020, 105 pp) | Published FIP report | Supporting published source — **not** a slide |
| D3 | `A scoping review on generative AI and LLMs in mitigating medication related harm` (npj Digital Medicine, 8 pp) | Published journal article | Supporting published source — **not** a slide |
| D4 | `pharmacovigilance_databases_comparison_1.xlsx` | Persian spreadsheet comparing public pharmacovigilance databases; states it was prepared 11 Oct 2026, cites official URLs by number. Author and review status are **unknown** | Secondary, unverified — **not** a slide |

Consequences:

1. The three decks are **sparse**. They give definitions, a classification of systems, a list of AI uses and a company-duty instruction, but **no report forms, no field lists, no timelines and no review workflow**. Anything of that kind below comes from D1–D4 or is labelled [I].
2. Slide text could be extracted for S3 and most of S2. Much of S1 (pages 2–10) and parts of S2 were **images**, which I read visually. The Persian instruction on S1 p.6 was legible but is a screenshot: **verify it against the original document** before relying on any wording.
3. S2 p.18 lists "Case 1 Methotrexate Toxicity" and "Case 2 NSAID Anaphylaxy" as titles only; **no case details are in the file**.
4. I read D1–D3 selectively (glossary, executive summary, the sections on definitions, dispensing errors, labelling/naming, pharmacovigilance, patient reporting, standardization, AI), not every page. Claims below cite only what I read.
5. Several slides reproduce third-party material (WHO "Medication Without Harm" wheel, CLINOSOL-branded module slides, paper figures). **No slide images were copied into the repository.**
6. Per your instruction, lecture slides alone are **not** an authority to activate clinical rules or to send reports externally. D1–D4 do not change that either: D4 is unverified, and D1/D2 are general frameworks, not Iranian regulation.

---

## 2. Terminology, definitions and classifications (preserved as stated)

Source tags: S1/S2/S3 = slides; D1/D2/D3 = published documents.

### 2.1 Definitions

| Term | Wording as in the source | Source |
|---|---|---|
| Pharmacovigilance | "The detection, assessment, understanding and prevention of adverse effects or any other drug related problem" | S2 p.4 (FIP uses nearly identical wording, D2 §5.4.5) |
| Pharmacovigilance (role) | "A key element in assessing, monitoring and preventing adverse drug reactions (ADRs)" | S2 p.4 |
| Adverse drug reaction (ADR) | "A response to a medicine that is noxious and unintended, and which occurs at doses normally used in humans" (D1 glossary); FIP adds "for the prophylaxis, diagnosis or therapy of disease or the restoration, correction or modification of physiological function" | D1 glossary; D2 §1.1. The slides use "ADR" without defining it |
| Adverse event (AE) | "Any untoward medical occurrence that may present during treatment with a medicine or vaccine, but which does not necessarily have a causal relationship" | D1 glossary |
| Adverse drug event (ADE) | "Any injury occurring during the patient's medication therapy resulting from either appropriate care, or from unsuitable or suboptimal care"; the definition **includes ADRs and medication errors** | D2 §1.1 (S3 uses "ADEs" without a definition) |
| Medication error (ME) | "Unintended mistakes in the medicines-use process caused by omissions or commissions, as well as other errors" | D2 §1.1 |
| Near miss ("close call", "potential adverse drug event") | "A medication error that has the potential to cause harm, but has not, either by luck or because it was intercepted and corrected" | D2 §1.1 |
| Adverse event of special interest (AESI) | "A pre-specified, medically-significant event that has the potential to be causally associated with a medicine or vaccine that needs to be carefully monitored and confirmed by further special studies" | D1 glossary |
| AEFI | "Any untoward medical event that follows immunization and that does not necessarily have a causal relationship with the use of the vaccine" | D1 glossary (vaccines are outside the MedSmarter scope as built) |
| Drug safety vs medication safety | Drug safety: pharmaceutical products, harm–benefit ratio, ADRs. Medication safety: preventing and managing medication errors | D2 §1.1 |
| Spontaneous reporting system | Main purpose "early detection of new, rare and serious ADRs and covering the entire population in a cost-effective way"; weakness: "Under-reporting" | S2 p.5 |
| Real-world evidence | "Evidence of the potential benefits of the medical product in a clinical setting derived from RWD"; RWD = data collected during routine clinical care | S3 pp.4, 9–10 |
| Product-quality defect, substandard/falsified product, lack of effect | **Not defined in any supplied slide.** D1's glossary (as read) does not define them either | [Q] see §8 |

The relationship the sources draw: ADEs ⊃ {ADRs, medication errors}; near misses are medication errors that did not cause harm (D2 p.9, Figure 2). **AE ≠ ADR** (AE needs no causal relationship; ADR implies a response to the medicine).

### 2.2 Types of medication error (D2 Figure 1, as listed)

Omission; commission; prescribing/ordering error; transcribing error; documenting error; distributing error; dispensing error; preparing error; administration error; storage error; monitoring error; error in medicine information or education.

### 2.3 Common dispensing errors (D2 §5.2.1, as listed)

Incorrect medicine, form or strength; label with incorrect information or inconsistent with the original prescription; incorrect quantity; expired medicine; incorrectly compounded medicine; undetected drug interaction or undocumented allergy information; someone else's prescription medicine; a different brand of a medicine the patient already has (duplication).

### 2.4 Classification of pharmacovigilance systems (S3 Figure 1, from the paper)

| Tier | Data sources | Systems |
|---|---|---|
| Established | Case reports | Journal publications; FAERS |
| Emerging | Electronic health records; insurance claims | Sentinel; real-world evidence |
| Experimental | Online discussions; sensor data; reporting apps; social-media monitoring; mobile devices | — |

Analysis methods listed: classical statistical analysis, digital signal processing, machine learning, natural-language processing. S3 states "Some of these platforms are well-established while others are still emerging or experimental."

### 2.5 Workflows shown on the slides

* **S3 Figure 2** "General pharmacovigilance workflows for emerging and experimental systems": EHR row — patient visitation → EHR documentation → de-identification of patient data → pharmacovigilance research and analysis. Mobile row — participant downloads app → participant reports side-effects / device sensors capture relevant information → participant data is normalized and aggregated → research and analysis. Social-media row — discussions → APIs + web scraping → identification of drugs and adverse reactions (optional cohort identification).
  S2 p.13 carries the same heading ("General Pharmacovigilance Workflows for Emerging and Experimental Systems"); p.14 presumably shows the same figure but it was not machine-readable — **[Q] confirm**.
* **No slide shows a case-handling workflow** (receipt → validation → classification → causality → submission). That is a gap, see §8.

### 2.6 Good Pharmacovigilance Practices (GVP) modules (S1 pp.7–10, EU framework)

S1 p.4: "Good pharmacovigilance practices (GVP) are a set of measures drawn up to facilitate the performance of pharmacovigilance in the European Union (EU)", covering centrally and nationally authorized medicines and applying to marketing authorization holders in the EU, the EMA and regulatory authorities in member states. Modules: I Pharmacovigilance systems and their quality systems · II PSMF (system master file) · III Inspection · IV Audits · V Risk management systems · VI Management and reporting of adverse reactions to medicinal products · VII Periodic safety update report · VIII Post-authorization safety studies · IX Signal management · X Additional monitoring · XI Public participation in pharmacovigilance · XII Continuous pharmacovigilance, ongoing benefit-risk evaluation, regulatory action and planning of public communication · XIII Incident management · XIV Referral procedures for safety reasons · XV Safety communication · XVI Risk minimization measures: selection of tools and effectiveness indicators.
S1 p.5 also shows the cover of the "Guideline on good pharmacovigilance practice (GVP) for Arab Countries — Guideline for Marketing Authorization Holders" (1st published March 2014; first effective 1 July 2015; version 3, December 2015). Only the cover is shown; its content is not in the file.

### 2.7 WHO "Medication Without Harm" action areas (S1 p.2)

Four domains and their key action areas, as printed on the wheel: **Patients and the public** (involvement of patient organizations; reporting by patients; patient engagement; public awareness and medication literacy) · **Health care professionals** (education and training; communication and teamwork; capability at point of care; incident reporting and learning) · **Medicines** (right product at point of care; logistics, storage and disposal; naming, labelling and packaging; product quality and safety) · **Systems and practices of medication** (leadership and governance; prescribing, preparation and dispensing; administration and patient monitoring; monitoring and evaluation). High-risk situations named: polypharmacy, transitions of care.

---

## 3. Requirements explicitly stated in the slides

Only statements that appear on S1–S3. Each is something the **material says**; whether MedSmarter must do it is a separate decision (see §6–7).

| ID | Statement (paraphrase kept close to the slide) | Source |
|---|---|---|
| E-01 | Pharmacovigilance covers detection, assessment, understanding and prevention of adverse effects **or any other drug-related problem** (so scope is wider than ADRs alone) | S2 p.4 |
| E-02 | Medicines safety monitoring is continuous and dynamic across the product life cycle; it continues after approval, and matters especially for ADRs that are rare, occur only in subgroups, or appear after long exposure (thalidomide given as an example) | S3 pp.3, 7 |
| E-03 | Spontaneous reporting aims at early detection of new, rare, serious ADRs; its weakness is under-reporting, which delays detection and action | S2 p.5 |
| E-04 | Including patients in spontaneous reporting is a trend toward proactive pharmacovigilance; patient-sourced information can tackle under-reporting and identify new risks | S2 p.6 |
| E-05 | Patient reporting is increasingly accepted (WHO and EU acknowledge its value); HCP reports may capture only part of the patient's story; patients can give first-hand, detailed accounts of nature, significance and consequences of ADRs; ADRs reported by patients and by HCPs are similar in some respects and differ in others | S2 pp.7–9 |
| E-06 | Awareness of patient reporting is still low; raising patient **and** HCP awareness should be a priority of national regulators, especially where reporting is low | S2 p.10 |
| E-07 | Published case reports are rich but "fundamentally anecdotal", "cannot support conclusions in broader populations", are unstructured text, and are useful for **hypothesis generation** | S3 pp.13–14 |
| E-08 | Modern approaches: spontaneous reporting databases, EHR monitoring, research frameworks, social-media surveillance, digital devices — some established, some emerging/experimental | S3 p.5 |
| E-09 | AI uses named: signal detection and ADR identification; automated literature review; NLP of unstructured text; PV data management (entry, validation, reconciliation); predictive patient-specific risk; drug–drug interaction prediction; safety-signal prioritization; real-world-evidence analysis | S3 pp.20–29 |
| E-10 | "AI in pharmacovigilance … should always be used in conjunction with human expertise. Continuous validation, transparency, and interpretability are essential." | S3 p.30 |
| E-11 | A real-time, continuously learning AI integrating heterogeneous data is described as an **opportunity** ("could"), not as something that exists or is validated | S3 p.17 |
| E-12 | (Iranian instruction, screenshot) The instruction on ADR reporting by **manufacturing and importing pharmaceutical companies** to the national ADR registration/review center is based on a commission decision dated 1384/2/12; it applies to all companies producing or importing pharmaceutical products | S1 p.6 |
| E-13 | (Same instruction) Each company must have an appropriate pharmacovigilance system and act on safety findings; must appoint a **trained responsible person** and introduce them to the national center; that person must have enough information on all aspects needed to review ADRs, specific safety regulations, and the benefit/risk of the company's products on the Iranian market | S1 p.6 |
| E-14 | (Same instruction) The responsible person's duties: (3-1) create and maintain a system to collect and record information on all ADRs reported through different channels; (3-2) coordinate preparation and sending of collected ADR reports to the national ADR center; (3-3) respond fully and quickly to any question from the center and facilitate access to information needed to evaluate the reports. The center may run training workshops for company liaisons | S1 p.6 |
| E-15 | World Patient Safety Day 2024 (diagnostic safety) objectives: raise awareness of diagnostic errors; give prominence to diagnostic safety in policy and practice; foster collaboration among stakeholders; empower patients and families to engage with health workers | S2 pp.15–17 |
| E-16 | Remaining slide content is **title-only**: Methotrexate toxicity and NSAID anaphylaxis cases (S2 p.18). Nothing can be extracted from them | S2 p.18 |

**Not stated anywhere in the slides:** reporting forms or data elements; seriousness criteria; causality method; timelines; who may report what to whom in Iran; pathways for pharmacists, physicians or patients; handling of medication errors or product-quality complaints as reportable categories; any rule content or threshold.

---

## 4. Supporting statements from the published documents (not slides)

Kept separate because they are **not** the professor's material. They are useful context but are general frameworks, not Iranian regulation.

| ID | Statement | Source |
|---|---|---|
| P-01 | Standardization of reporting uses MedDRA (dictionary), ICH E2B(R3), the CIOMS reporting form and WHO-recommended core variables; WHODrug Global is the reference medicinal-product dictionary | D1 Annex 1 |
| P-02 | WHO tools named: VigiFlow (ICSR management, E2B-compatible), VigiMobile (field reporting, online/offline forms), VigiLyze (signal detection/management); patient reporting adopted by many countries (EU Directive 2010/84/EC cited) | D1 glossary, Annex 1 |
| P-03 | The strategy is "not a rigid checklist, but a practical framework"; it promotes a risk-based approach, reliance on existing networks and safety communication across the product life cycle | D1 foreword, exec. summary |
| P-04 | Pharmacists: community pharmacists, "due to their regular contact with patients, are in an ideal position to detect and report" adverse events; pharmacovigilance "should feature in medical and pharmacy educational curricula"; greater integration into routine pharmacy practice is still needed | D2 §5.4.5 |
| P-05 | Dispensing safety practices: patient counselling, barcode scanning for product selection, unit-dose, medication history and reconciliation, checking prescribed dose/frequency/route before dispensing | D2 §5.2–5.4 |
| P-06 | Look-alike/sound-alike (LASA) names and packaging are a medication-error risk; names and labelling should be assessed at registration | D2 §7.3 |
| P-07 | Medicine shortages create risks (LASA packaging, foreign-language labelling); good-manufacturing breaches can cause recalls | D2 §5.1 |
| P-08 | A blame-free culture and robust error reporting and learning are described as lessons from aviation | D2 §8.1 |
| P-09 | GenAI/LLMs (30 studies, searched to Oct 2024) showed promise for early identification and classification of adverse drug events, but **no study tested them prospectively**; hallucination, outdated information and weak real-time data are listed limits | D3 abstract, discussion |
| P-10 | D4 (unverified) notes that spontaneous reports **do not prove causality or incidence** and are for signal detection and hypothesis generation; it lists common structured fields in public databases (patient age/sex/weight, drug, dose, route, dates, indication, action taken, rechallenge, reaction, outcome, seriousness, reporter type) and caveats on duplicates and missing data | D4 sheet 2 |

---

## 5. Category analysis — what the sources do and do not support

| Category | Defined in the material? | Where it appears | Handling recommendation |
|---|---|---|---|
| Adverse drug reaction | Yes (D1, D2) | S2, S3 throughout | Distinct category |
| Adverse event | Yes (D1) | S3 "ADEs", D1 | Distinct from ADR: no causality assumed |
| Adverse drug event | Yes (D2) | S3 uses the term | Umbrella of ADR + medication error; do **not** use as a synonym for ADR |
| Medication error | Yes (D2) with a type list | D2 only; S2/S3 do not discuss | Supported as a category only by D2 |
| Near miss | Yes (D2) | D2 only | Supported by D2 only |
| Dispensing error | Yes, typed list (D2) | D2 only | Supported by D2 only |
| Product-quality defect | **No definition** | S1 wheel names "product quality and safety"; D2 §5.1/§7.3 mention LASA, recalls, GMP | Keep as a free "product concern", unclassified, pending a definition [Q] |
| Lack of effect | **Not defined or mentioned** as a category | — | Existing code has `ApparentLackOfEffect`; not slide-derived (see §6) |
| "Any other drug-related problem" | Mentioned in PV definition (S2 p.4) | S2 | Justifies an "other / unclassified" bucket, nothing more |
| Vaccine AEFI / AESI | Defined (D1) | D1 | Out of current scope |

The sources never give a rule for deciding **which** category a patient's report belongs to; classification must stay a **professional** act (consistent with E-10).

---

## 6. Existing repository state relevant to this analysis

Verified by reading the repository (not assumed):

| Area | State today |
|---|---|
| `Modules/ADR` | **Placeholder** — `ADRModule` registers nothing ("Phase 1 placeholder"). The Phase 0 design lists entities `AdverseEvent`, `AdverseEventReview`, `AdverseEventDrug` and decision **D-11**: "internal recording in MVP; official output later" (recipient, official format, causality method undecided) |
| Phase 0 roadmap | `docs/phase0/10-roadmap.md` numbers P10 "Adherence, Symptoms, ADR" and P19+ "ADR reporting (official)" as post-MVP. **Its phase numbers do not match the phases actually delivered** (delivered Phase 5–8 differ), so the mapping below uses your naming (Phase 9 physician/pharmacist, Phase 10 pharmacy) and flags the mismatch |
| `Modules/Patients` | Patient-reported symptoms (`SymptomInput`: text, severity, onset, resolved, optional link to a patient medication); Phase 8 adherence (Taken/Skipped/Missed, patient-reported, never inferred) |
| `Modules/ProductTrace` | Batch/lot product records; **manufacturer reports**: statuses `Draft → PendingConsentOrReview → ReadyToSend → Sent → Acknowledged / Failed / Cancelled`; issue types `AdverseEvent, AbnormalAppearanceOrPackaging, ApparentLackOfEffect, QualityProblem, Other`; severity `Unknown/Mild/Moderate/Severe`; identity-free payload; two-step review; outbox; **mock delivery only**. These categories were chosen in Phase 5 and are **not derived from the slides** |
| `Modules/Guidance`, `ClinicalRules` | Phase 7–8: deterministic engine, two-person rule review, no approved clinical rule, demonstration rules never active, simulated delivery only, no emergency path |
| `Modules/AI` | Phase 6: information-only, external AI gated off |
| `Modules/Consent`, `Audit`, `Identity` | Phase 3–5: per-scope consent, care relationships, hash-chained audit |
| `Modules/Integrations` | Insurance contracts only; no pharmacovigilance gateway |
| `Modules/Pharmacists`, `Pharmacies`, `Physicians` | Profile modules; no pharmacy workflow (dispensing, intervention, error capture) yet |
| `docs/phase5/10-external-dependencies.md` | Already records that real delivery of manufacturer reports "needs an agreement and a technical channel with each manufacturer (or the national pharmacovigilance gateway) and a legal basis for the transfer" |

---

## 7. Mapping to modules and phases

Recommended placement. **This is a proposal; no existing decision is overridden.** "Needs source" = cannot be built beyond a placeholder until you supply an authoritative document.

### 7.1 Phase 8 (patient monitoring) — delivered, no change

| Requirement | Relation to Phase 8 | Action |
|---|---|---|
| E-02 continuous monitoring | Phase 8 deliberately does **not** claim continuous or real-time monitoring; this matches the slides' framing of continuous monitoring as a life-cycle process performed by systems and professionals | None. Keep the disclaimers |
| E-04/E-05 patient first-hand information | Patient-reported adherence and symptoms already labelled "patient-reported, not verified" | None |
| E-10 human expertise | Reassessment output is "not a diagnosis"; rule review is two-person | None |
| E-11 real-time AI | Not implemented and not claimed | None |

### 7.2 Phase 9 — physician / pharmacist workflows (recommended home for the core PV items)

| Requirement(s) | Proposed work | Modules | Basis |
|---|---|---|---|
| E-01, E-04, E-05 | **Internal adverse-event record** created from a patient's report or by a professional: link to a medication, onset/resolution, patient description, source = *patient-reported / professional-entered*; never labelled an ADR automatically | `ADR` (currently empty), `Patients` (symptoms as input), `Consent`, `Audit` | [I] — D-11 already approves internal recording; E-04/E-05 give the rationale |
| E-10, §5 | **Professional review** of an internal record: a physician or pharmacist (with a care relationship and consent) assigns the category from a **closed list limited to what §5 supports** (ADR, adverse event, medication error, near miss, product concern, other/unclassified), with default "unclassified"; classification and any later causality note are professional entries, never automatic | `ADR`, `Physicians`, `Pharmacists`, `Consent` | [I]; category list [Q] |
| E-07, P-10 | Make clear in the UI that a single report is a **hypothesis/signal, not proof of causality** | `ADR`, web/Flutter | [I] from E-07 and D4 caveat |
| E-06, E-03 | Patient-facing, calm explanation of why reporting matters and what happens to a report (all wording marked unreviewed Persian) | `Guidance`, web/Flutter | [I] |
| E-09/E-10 | If AI assists (e.g. drafting a plain-language summary, NLP extraction), it stays **assistive**, labelled, gated, and never sets category, seriousness or causality; P-09 says no prospective validation exists | `AI` | [I] |
| E-05 (patients vs HCPs differ) | Keep **who reported** (patient / physician / pharmacist) as a first-class field and show it | `ADR` | [I] |

### 7.3 Phase 10 — pharmacy workflows

| Requirement(s) | Proposed work | Modules | Basis |
|---|---|---|---|
| P-04, E-01 | Pharmacist can open an internal adverse-event record while counselling or dispensing | `ADR`, `Pharmacists`, `Pharmacies` | D2 supports the role; the Iranian pathway for pharmacist reporting is **not in the slides** [Q] |
| §2.2–2.3 (D2) | **Dispensing-error and near-miss internal log** using the D2 typed lists as optional tags, blame-free wording, links to the batch record where one exists | `Pharmacies`, `ProductTrace`, `Audit` | D2 §1.1, §5.2.1, §8.1 — supported by D2 only |
| P-06, P-07, S1 wheel (naming, labelling, packaging, product quality) | **Product concern** intake tied to a batch/lot record (appearance, packaging, LASA concern) | `ProductTrace` (already holds `AbnormalAppearanceOrPackaging`, `QualityProblem`) | Existing Phase 5 choice; slides name the topic but give no definition |
| P-05 | Counselling, medication history/reconciliation prompts | `Pharmacies`, `Patients` | [I] from D2 |

### 7.4 Later phases (reporting, integration, analytics, AI)

| Requirement(s) | Item | Why later | Prerequisite |
|---|---|---|---|
| E-12–E-14 | **External reporting to the national center** | The slides describe duties of **manufacturers/importers**, not pharmacies, physicians, patients or software vendors. No form, field list, timeline or channel is given | Authoritative Iranian instruction + legal basis; **Fake adapter only** until then |
| P-01/P-02 | ICSR / E2B(R3) export, MedDRA/WHODrug coding | Licensing and standards not in the slides; coding must come from the official source | Decision + licences + official spec |
| E-13/E-14 | If MedSmarter operated as/for a company: designated responsible person, collection system, response to center queries | Only applies if you decide the platform or its operator takes the manufacturer/importer role | Legal/regulatory status decision |
| E-07, P-10, E-09 | Aggregate analytics and **signal detection** | Slides frame signals as hypotheses; D4 stresses no incidence/causality; needs de-identification (S3 Fig. 2) | Existing analytics/privacy decisions; no clinical conclusions |
| E-09 | NLP, automated literature review, predictive risk, DDI prediction | S3 lists them as roles of AI; P-09 reports no prospective validation; rules need clinical review | Validation plan; none of this activates a rule |
| E-02 | Anything marketed as continuous/real-time monitoring | Not established | Do not claim |

### 7.5 Cross-cutting (all phases)

| Topic | Placement |
|---|---|
| Source provenance: tag each source "lecture slide / published guideline / unverified spreadsheet" in the knowledge base; unverified or lecture-only sources are **never sufficient** for activating a rule (existing Phase 7/8 policy) | `KnowledgeBase`, `ClinicalRules` |
| Persian terminology: the slides are in English; the only Persian material is the instruction screenshot ("عوارض ناخواسته داروها"); any Persian PV wording stays flagged unreviewed | `Guidance`, i18n |
| Audit and consent for every PV record view/change | `Audit`, `Consent` — no change to existing architecture |

---

## 8. Data fields, workflow states, review and documentation — what is actually supported

### 8.1 Data fields

| Level | Fields | Source |
|---|---|---|
| **Explicit in the slides** | Only the *duty* to "collect and record information on all ADRs reported through different channels" (E-14). **No field list is given.** | S1 p.6 |
| **Explicit in D2 (not slides)** | Dispensing-error tags (§2.3); medication-error type tags (§2.2) | D2 |
| **Described in D4 (unverified)** | Typical public-database fields: age, sex, weight, height, drug (generic/brand), dose, route, dates, indication, action taken, rechallenge, reaction term, outcome, seriousness, reporter type | D4 |
| **Already present in MedSmarter** (Phase 5/8; reuse, not slide-derived) | Product name, generic, manufacturer, batch, dates, GTIN; issue type; severity; occurred-on; duration of use; age/sex group; concomitant medications; description; symptom text/severity/onset/resolved; reporter role; consent flag; review decision/note | repository |

[I] An internal record should capture at minimum: who reported (role), patient-reported vs professional-entered, linked medication(s) and optional batch, onset/resolution dates, free-text description, outcome as *stated*, category (default "unclassified"), classification author and time, and record version. The slides do not require this exact set; it follows from E-05, E-14 and existing Phase 5 patterns.

### 8.2 Workflow states

* **Slides:** none defined.
* **Existing:** manufacturer-report states (§6).
* [I] Candidate internal states, **not** supported by any slide: `Draft → Submitted → UnderProfessionalReview → Classified (category set) → Closed (retained internally / no further action)`, with an explicit `ExternalReportPrepared` state only once an authoritative pathway exists. Each transition recorded in the hash-chained audit.

### 8.3 Professional review steps

* **Slides:** only the general principle that AI is used with human expertise (E-10) and that published case reports are rich because physicians evaluate histories, exposures and outcomes (S3 p.13).
* [I] Review by a professional with a relationship and consent; reviewer ≠ author when the record is professional-entered (mirrors the two-person rule used for rules); no automated causality.

### 8.4 Documentation requirements

* **Slides:** the company's duty to maintain a collection/recording system and answer the center (E-14); GVP module titles list a system master file, inspections, audits, signal management etc. (S1 p.8–10) but **give no content**.
* [I] MedSmarter already keeps an immutable audit trail; whether it satisfies any regulatory documentation duty is **unknown** [Q].

### 8.5 Reporting responsibilities

* **Explicit:** manufacturers/importers must appoint a trained responsible person and report ADRs to the national center (E-12–E-14).
* **Not stated:** any duty of pharmacists, physicians, patients or a software platform. Patient reporting is described as *valuable and increasingly accepted* (E-04, E-05), not as an obligation, and no Iranian patient-reporting channel is described.

---

## 9. Separation: explicit vs inferred vs open

### 9.1 Explicit (see §3 for detail)
E-01 … E-16, plus published-source statements P-01 … P-10 (supporting, not slides).

### 9.2 Implementation suggestions (inferred, not requirements)

| ID | Suggestion | Phase |
|---|---|---|
| I-01 | Internal adverse-event record in the existing empty `ADR` module, source/provenance first-class | 9 |
| I-02 | Professional classification step; default "unclassified"; category list limited to §5 | 9 |
| I-03 | "Signal/hypothesis, not proof" labelling on every report view | 9 |
| I-04 | Show reporter role (patient/physician/pharmacist) and never merge patient and professional accounts | 9 |
| I-05 | Patient awareness content explaining reporting (E-06) | 9 |
| I-06 | AI strictly assistive; excluded from category/seriousness/causality | 9+ |
| I-07 | Dispensing-error / near-miss internal log with blame-free wording | 10 |
| I-08 | Product concern intake linked to batch/lot | 10 |
| I-09 | Counselling / medication-history prompts for pharmacists | 10 |
| I-10 | Reporting adapter built as **Fake only**; real adapter only after an authoritative specification | later |
| I-11 | De-identified aggregate analytics for signals as hypotheses only | later |
| I-12 | Knowledge-base source tags for slide/published/unverified provenance | 9 |

### 9.3 Questions and gaps (decisions needed from you are in §10)

| ID | Gap |
|---|---|
| Q-01 | Which authoritative Iranian documents govern reporting (the national center's current form, required fields, seriousness criteria, deadlines, who may report, electronic submission)? The slides show one instruction for companies only, as a screenshot |
| Q-02 | Is MedSmarter (or its operator) a manufacturer/importer/marketing-authorization holder for any purpose? If not, E-12–E-14 do not apply to it directly |
| Q-03 | May patients and pharmacists report to the national center directly or only via a professional? (S2 says patient reporting is valued and increasing; it does not describe Iran's channel) |
| Q-04 | Definitions of product-quality defect, substandard/falsified, lack of effect, and whether they are "reportable categories" in the intended setting |
| Q-05 | Seriousness criteria and causality method (Phase 0 D-11 is still undecided) |
| Q-06 | MedDRA / WHODrug coding: licence, edition, who codes |
| Q-07 | S2 p.14 workflow figure and S2 p.18 case details (Methotrexate, NSAID anaphylaxis) were not readable/extractable |
| Q-08 | Status and authorship of the database-comparison spreadsheet (D4); its figures were not independently verified |
| Q-09 | Whether the phase numbering in `docs/phase0/10-roadmap.md` should be replaced by the current plan (Phase 9 = physician/pharmacist, 10 = pharmacy) |
| Q-10 | Persian terminology to use for ADR, adverse event, medication error, near miss, with a named reviewer |
| Q-11 | Copyright/permission for reusing any slide figure (none were copied) |

---

## 10. Decisions needed from you

1. **Role of the platform:** only a facilitator that keeps internal records and lets professionals decide, or something that must meet the manufacturer/importer duties (E-12–E-14)? *Recommended default: facilitator; no external reporting.*
2. **Authoritative sources:** supply the current national pharmacovigilance form/instruction(s) and the legal basis for sharing patient-derived reports, or confirm that external reporting stays out of scope until then.
3. **Phase 9 scope:** approve an internal adverse-event record + professional review (I-01…I-05, I-12), or defer all PV items.
4. **Category list:** approve the restricted list in §5 (ADR, adverse event, medication error, near miss, product concern, other/unclassified), or give the professor's list.
5. **Phase 10 scope:** approve dispensing-error/near-miss log and product-concern intake based on D2, or defer.
6. **Existing manufacturer-report categories** (`ApparentLackOfEffect` etc.): keep as a Phase 5 decision, or align to definitions you provide.
7. **Seriousness and causality:** who decides the method (D-11), and whether MedSmarter records them at all in Phase 9.
8. **Persian wording reviewer** for all PV-facing text.
9. **How to treat D4 (spreadsheet):** discard, or have its author/reviewer confirm it before it is used even as background.
10. **Roadmap numbering:** update `docs/phase0/10-roadmap.md` to the current plan, or leave it as a historical document.

---

## 11. Safeguards that stay in force

* No lecture slide, published document or spreadsheet activates a clinical rule; the Phase 7/8 activation policy (verified source + two-person review + no demonstration rule active) is unchanged. No clinically approved rule exists.
* No real patient report is transmitted outside the system; the existing manufacturer-report channel remains a **mock**.
* No invented regulatory obligation, form, deadline, code or pathway appears in this document; anything not in the sources is marked [I] or [Q].
* AI remains information-only and cannot classify, grade or attribute causality.
* Persian clinical/PV wording remains flagged as unreviewed.

---

## 12. Short Persian summary (خلاصه)

* از ۸ فایل، فقط ۳ فایل اسلاید استاد است (صنعت دارو، مشارکت بیماران، هوش مصنوعی و فارماکوویژیلانس)؛ فایل بیماران دو بار آپلود شده و یکسان است. بقیه مقالات/گزارش‌های منتشرشده (WHO، FIP، مقالهٔ مروری) و یک فایل اکسل با نویسندهٔ نامشخص هستند.
* اسلایدها تعریف‌ها، دسته‌بندی سامانه‌ها (established / emerging / experimental)، نقش هوش مصنوعی (همیشه همراه با تخصص انسانی) و دستورالعمل شرکت‌های دارویی (انتصاب مسئول فارماکوویژیلانس و ارسال گزارش به مرکز ADR) را می‌دهند؛ **فرم گزارش، فهرست فیلدها، مهلت‌ها و روند بررسی را نمی‌دهند.**
* پیشنهاد: ثبت داخلی رویداد نامطلوب با بازبینی حرفه‌ای در فاز ۹؛ ثبت خطای تحویل/نزدیک‌به‌خطا و نگرانی کیفیت محصول در فاز ۱۰ (بر پایهٔ FIP)؛ گزارش رسمی خارجی فقط پس از دریافت سند مرجع و مبنای قانونی، با آداپتر Fake.
* هیچ قاعدهٔ بالینی فعال نمی‌شود و هیچ گزارش واقعی ارسال نمی‌شود.
