# Pharmacovigilance traceability matrix (product-wide)

Companion to [`pharmacovigilance-source-analysis.md`](pharmacovigilance-source-analysis.md) (same requirement IDs) and [`phase9/`](phase9/README.md). **Last updated: Phase 9.** Update this file whenever a later phase touches a row.

**Class:** SLIDE = directly supported by a lecture slide · SUPP = supported by a named supplementary document · DESIGN = design inference/proposal (not a source requirement) · OPEN = question needing an authoritative source · BLOCKED = blocked pending clinical / regulatory / legal / privacy / expert approval.
**Sources:** S1 Shalviri (industry) · S2 Karimi Ghovanlou (patients; uploaded twice, identical) · S3 Mohebbi (AI) — these three are the **only lecture slides**. D1 WHO 2025 · D2 FIP 2020 · D3 npj Digit Med review · D4 Persian Excel comparison (unverified, author unknown). D1–D3 were read selectively, not exhaustively. Slide 6 of S1 and slide 18 of S2 have the limitations stated in `phase9/07-sources-and-limits.md`.
**Phase 9 status:** DONE · PARTIAL · NOT STARTED (deferred) · N/A.

| ID | Requirement / statement (concise) | Class | Source (file, place) | Existing module(s) | Phase 9 status | Planned phase / reason for deferral | Acceptance tests | Approval still needed |
|---|---|---|---|---|---|---|---|---|
| E-01 | PV covers detection, assessment, understanding, prevention of adverse effects **or any other drug-related problem** | SLIDE | S2 p.4 | ADR, Guidance | PARTIAL — "other / unclassified" kept as default; wider problem types not modelled | 10 (pharmacy problems) | Default Unclassified; category set | wording (fa) |
| E-02 | Safety monitoring is continuous across the life cycle; post-market matters for rare/subgroup/long-term ADRs | SLIDE | S3 pp.3,7 | ClinicalRules, Patients (Ph 8) | N/A — **no continuous/real-time claim made anywhere** | — | Phase 8 disclaimers; Phase 9 notices | — |
| E-03 | Spontaneous reporting aims at early detection; under-reporting is its weakness | SLIDE | S2 p.5 | ADR | DONE (easy patient + professional entry) | — | patient create over HTTP and UI | — |
| E-04 | Including patients in reporting helps tackle under-reporting | SLIDE | S2 p.6 | ADR, Patients | DONE — patient can report and follow up | Flutter patient screen: later | Phase9ApiTests, phase9.test | wording (fa) |
| E-05 | Patient and HCP reports differ; patients give first-hand detail | SLIDE | S2 pp.7–9 | ADR | DONE — reporter role/origin first-class; never merged | — | origin/role tests | — |
| E-06 | Raise patient and HCP awareness of reporting | SLIDE | S2 p.10 | Guidance | NOT STARTED — only an explanatory page text | 10+ (awareness content) | — | clinical + wording review |
| E-07 | Case reports are anecdotal; hypothesis generating only | SLIDE | S3 pp.13–14 | ADR | DONE — "signal, not proof" on every record/response | — | notice tests (service, HTTP, UI) | — |
| E-08 | Approaches: spontaneous DBs, EHR, social media, devices (established/emerging/experimental) | SLIDE | S3 p.5, Fig.1 | — | N/A (context only) | later (analytics) | — | — |
| E-09 | AI roles: signal detection, literature review, NLP, data mgmt, prediction, DDI, prioritization, RWE | SLIDE | S3 pp.20–29 | AI, ClinicalRules | NOT STARTED (deliberately) — none implemented | later; each needs validation (D3) | — | **BLOCKED**: clinical validation |
| E-10 | AI only with human expertise; validation, transparency, interpretability | SLIDE | S3 p.30 | ADR, AI | DONE — assistance is a labelled **template**, no model, not stored; AI cannot classify/grade/attribute | — | assistance tests (no banned phrases, not stored) | — |
| E-11 | Real-time continuously learning AI is an opportunity ("could") | SLIDE | S3 p.17 | — | N/A — not built, not claimed | — | — | — |
| E-12 | Iranian instruction: manufacturers/importers report ADRs to the national center | SLIDE (screenshot, **unverified wording**) | S1 p.6 | ProductTrace (mock), Integrations | N/A — **MedSmarter is not a manufacturer/importer** | later, only with authoritative text + role decision | — | **OPEN/BLOCKED**: legal |
| E-13 | Company PV system + trained responsible person introduced to the center | SLIDE (same limits) | S1 p.6 | — | N/A (facilitator role) | future manufacturer-role workflow, if ever | — | **OPEN/BLOCKED**: legal |
| E-14 | Responsible person's duties: collect/record all ADRs, coordinate sending, answer the center | SLIDE (same limits) | S1 p.6 | ProductTrace (mock) | N/A | as above | — | **OPEN/BLOCKED**: legal |
| E-15 | World Patient Safety Day 2024 diagnostic-safety objectives | SLIDE | S2 pp.15–17 | — | N/A (awareness context) | — | — | — |
| E-16 | Methotrexate / NSAID anaphylaxis cases | SLIDE (**titles only**) | S2 p.18 | — | N/A — no content to use | — | — | **OPEN**: obtain case content |
| P-01 | Standard dictionaries/forms: MedDRA, ICH E2B(R3), CIOMS, WHODrug | SUPP | D1 Annex 1 | — | NOT STARTED — no coding, no schema | later | — | **BLOCKED**: licences, official spec |
| P-02 | WHO tools (VigiFlow, VigiMobile, VigiLyze); patient reporting widely adopted | SUPP | D1 glossary, Annex 1 | Integrations | NOT STARTED | later (mock adapter first) | — | **OPEN**: Iranian pathway |
| P-03 | Risk-based, practical (not a rigid checklist) strategy | SUPP | D1 foreword | — | N/A | — | — | — |
| P-04 | Pharmacists are well placed to detect and report AEs; PV belongs in routine practice | SUPP | D2 §5.4.5 | ADR, Pharmacists | PARTIAL — a pharmacist can open and review internal records | 10 (pharmacy workflow) | roles in tests | **OPEN**: Iranian pharmacist pathway |
| P-05 | Dispensing safety practices (counselling, barcodes, history/reconciliation) | SUPP | D2 §5.2–5.4 | Pharmacies, ProductTrace | NOT STARTED | 10 | — | clinical review |
| P-06 | LASA names/packaging are a medication-error risk | SUPP | D2 §7.3 | ProductTrace | NOT STARTED | 10 (product concern) | — | clinical review |
| P-07 | Shortages/recalls create risks | SUPP | D2 §5.1 | ProductTrace | NOT STARTED | 10+ | — | — |
| P-08 | Blame-free culture and error reporting/learning | SUPP | D2 §8.1 | ADR, Pharmacies | PARTIAL — neutral wording; no error log yet | 10 | wording review | wording (fa) |
| P-09 | GenAI/LLMs untested prospectively; hallucination and outdated info are limits | SUPP | D3 abstract/discussion | AI, ADR | DONE — no model used; reason recorded | any AI add-on needs validation first | assistance tests | **BLOCKED** for any real model |
| P-10 | Spontaneous reports do not prove causality/incidence | SUPP (unverified) | D4 sheet 2 | ADR | DONE (matches E-07 independently) | — | notice tests | verify D4 provenance |
| D-def-1 | Definitions of ADR, AE, ADE, ME, near miss | SUPP | D1 glossary p.xiii; D2 §1.1 pp.6–7 (printed) | ADR | DONE — quoted in `AdrDefinitions`, cited, **not** attributed to slides | — | definitions tests | re-check page numbers against originals; fa wording |
| D-def-2 | Product-quality defect, lack of effect | **OPEN** | not defined in any supplied material | ProductTrace (own Phase 5 types) | DONE as a **non-decision**: not categories | 10 (product concern, separate from ADR classification) | category-set test | **OPEN**: authoritative taxonomy |
| I-01 | Internal adverse-event record, provenance first-class | DESIGN | — | ADR | DONE | — | Phase9Tests, Phase9ApiTests | clinical review of field set |
| I-02 | Professional classification; default Unclassified; reason; reversible | DESIGN | — | ADR | DONE | — | classification tests | clinical review |
| I-03 | "Signal, not proof" labelling | DESIGN (supported by E-07) | — | ADR | DONE | — | notice tests | wording (fa) |
| I-04 | Show reporter role; never merge patient and professional accounts | DESIGN (supported by E-05) | — | ADR | DONE | — | voice-separation tests | — |
| I-05 | Patient awareness text about reporting | DESIGN | — | web | PARTIAL (short page text) | later | UI test | clinical + wording review |
| I-06 | AI strictly assistive, excluded from category/seriousness/causality | DESIGN | — | ADR, AI | DONE | — | assistance tests | — |
| I-07 | Dispensing-error / near-miss internal log, blame-free | DESIGN (basis D2 §5.2.1) | — | Pharmacies | NOT STARTED | 10 | — | clinical review |
| I-08 | Product concern intake linked to batch/lot | DESIGN | — | ProductTrace | NOT STARTED | 10 | — | — |
| I-09 | Counselling / medication-history prompts | DESIGN | — | Pharmacies | NOT STARTED | 10 | — | clinical review |
| I-10 | Reporting adapter, **mock only** until an authoritative spec | DESIGN | — | Integrations | NOT STARTED | later | — | **BLOCKED** |
| I-11 | De-identified aggregate analytics for signals as hypotheses | DESIGN | — | Analytics | NOT STARTED | later | — | **BLOCKED**: privacy, clinical |
| I-12 | Source provenance tags in the knowledge base | DESIGN | — | KnowledgeBase | PARTIAL — provenance recorded on definitions; no KB integration | later | definitions tests | — |
| I-13 | Supplements fill unknowns, never replace; corrections as follow-ups | DESIGN | — | ADR | DONE | — | supplement tests | — |
| I-14 | Reviewer queue filtered per patient by relationship + consent | DESIGN | — | ADR, Consent | DONE | — | queue HTTP test | — |
| I-15 | No automatic sharing with insurers/manufacturers/regulators | DESIGN (brief) | — | Integrations, ProductTrace | DONE — no code path; assembly-reference test | any future link: separate justification | reference test | **BLOCKED**: privacy/legal |
| Q-01 | National reporting form, fields, seriousness criteria, deadlines, who may report | OPEN | none supplied | — | NOT STARTED | — | — | authoritative document |
| Q-02 | Is MedSmarter ever a manufacturer/importer/MAH? | OPEN → **answered for now: facilitator** | your instruction | — | respected | — | — | confirm if it changes |
| Q-03 | May patients/pharmacists report to the national center directly? | OPEN | — | — | NOT STARTED | — | — | authoritative document |
| Q-05 | Seriousness and causality method (Phase 0 D-11) | OPEN/BLOCKED | — | — | NOT STARTED — **no field exists** | — | no-field reflection test | clinical + regulatory |
| Q-06 | MedDRA/WHODrug coding licence and coder | OPEN/BLOCKED | — | — | NOT STARTED | — | — | licence, legal |
| Q-07 | S2 p.14 figure; S2 p.18 case content | OPEN | — | — | N/A | — | — | obtain files |
| Q-08 | Provenance/authorship of D4 | OPEN | — | — | kept as unverified; unused as evidence | — | — | author/reviewer |
| Q-09 | Phase numbering mismatch (Phase 0 roadmap vs delivered) | OPEN → **documented** | `docs/phase0/10-roadmap.md` | docs | DONE — pointer note + mapping table | — | — | confirm the approach |
| Q-10 | Persian terms for ADR/AE/ADE/ME/near miss, named reviewer | OPEN/BLOCKED | — | i18n | all fa wording `AwaitingClinicalReview` | — | wording-status test | named qualified reviewer |
| Q-11 | Permission to reuse slide figures | OPEN | — | — | none reused | — | — | — |
| N-01 | Retention/DPIA for adverse-event records | BLOCKED | — | — | NOT STARTED | before any pilot | — | privacy/legal |
| N-02 | Emergency / urgent escalation path | BLOCKED | — | Guidance | NOT STARTED — **page states it sends no urgent alerts**; no emergency criteria invented | proposed Phase 11+ | notice test | clinical + product decision |
