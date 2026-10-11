# 07 — Sources and their limits

## Files actually read (session uploads, not stored in the repository)
| Short name | File | What it is | Read how |
|---|---|---|---|
| S1 | `Pharmacovigilance__Pharmaceutical_Industry` (11 pp) | Lecture slides, Shalviri | text extraction (title only) + the embedded **images** viewed |
| S2 | `Pharmacovigilance__Patients` and `…_Patients_2` | Lecture slides, Karimi Ghovanlou — **the two uploads are byte-identical** | text extraction; the logo images viewed |
| S3 | `Artificial_Intelligence__Pharmacovigilance` (31 pp) | Lecture slides, Mohebbi | text extraction; two figure images viewed |
| D1 | `The_global_smart_pharmacovigilance_strategy` (WHO 2025, 90 pp) | published document | **selective**: contents, glossary, executive summary, introduction, annex 1 passages, search for patient reporting / E2B / AI / causality |
| D2 | FIP *Patient safety* report 2020 (105 pp) | published document | **selective**: contents, §1.1 definitions, §5.1–5.4.5, §7.3, §8.1 |
| D3 | npj Digital Medicine scoping review (8 pp) | published article | abstract, introduction, searched for limitations |
| D4 | `pharmacovigilance_databases_comparison_1.xlsx` | Persian spreadsheet, **author and review unknown** | shared strings read; **not verified** |

**The WHO and FIP documents were reviewed selectively, not page by page. No exhaustive review is claimed.** The slides were not independently validated.

## Known source limitations (preserved, not repaired)
* **S1 slide 6** — the Iranian manufacturer/importer instruction is a **screenshot**; its exact wording has not been verified against the original document. I read that companies must have a PV system, name a trained responsible person introduced to the national center, and that this person collects ADR reports, coordinates sending them to the national ADR center and answers the center's questions. Treat this as a **source-review limitation, not a verified legal conclusion**, and not as an obligation of MedSmarter.
* **S2 slide 18** — "Case 1 Methotrexate Toxicity" and "Case 2 NSAID Anaphylaxy": **titles only**; no case content exists in the file.
* **S2 slide 14** — presumed to repeat the workflow figure of S3; not machine-readable.
* **Not established by any supplied material:** a national reporting form; reporting deadlines; a complete case-handling workflow; formal seriousness criteria; a causality method; a verified Iranian reporting pathway for patients, pharmacists or physicians.
* **D4 provenance** — kept as an **unverified supplementary source**, not discarded, **not used as authoritative evidence** anywhere in the code or the definitions. Needed: who prepared it, from which official pages, and whether anyone reviewed it. Its figures were not independently checked.
* No external web call was made to fill any gap.

## Copyright
No slide image or screenshot was copied into the repository. Quoted definitions are short, attributed excerpts.
