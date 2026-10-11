# 10 — Phase 9 report

## 1. Sources actually read
Lecture slides (the only three unique lecture files): **S1** Shalviri (industry) — pictures of other documents, read as images; **S2** Karimi Ghovanlou (patients; uploaded twice, byte-identical); **S3** Mohebbi (AI). Supplementary, **not** slides: **D1** WHO 2025, **D2** FIP 2020, **D3** npj Digit Med review (all three read **selectively**, not page by page), **D4** the Persian Excel comparison (read, **unverified**, author unknown, not used as evidence). Limitations: S1 slide 6 wording unverified (screenshot); S2 slide 18 titles only; no national form, deadlines, workflow, seriousness criteria, causality method or verified Iranian pathway in any file. Details: `07-sources-and-limits.md`.

## 2. Implemented vs deferred
**Implemented:** internal adverse-event record (stable id, reporter role/origin, suspected medicines linked to the patient's own record or kept as a name, event text, dates when known, missing-information indicators, provenance, status, append-only history, "signal not proof" and "internal record" notices, `NotSubmitted`); conservative classification (default Unclassified; five defined terms; professional only; reason; reversible); human review workflow (queue, notes, information requests, free-text professional assessment, status moves, follow-ups, supplements); a template-based assistance with **no model**; default-deny access with relationship + consent; audit; web workspace for patient, physician, pharmacist; Persian wording registry; traceability matrix; roadmap reconciliation.
**Deferred (documented, not built):** Phase 10 pharmacy workflows; product-concern intake; seriousness/causality (no field); any reporting adapter, E2B/MedDRA, analytics; real AI; Flutter ADR screens; emergency path; retention policy.

## 3. How the record and classification work
See `02`. The original is immutable; later information is appended; the category starts **Unclassified** and changes only by a professional's reasoned, versioned, reversible action, recorded in the history. Patients never see the category. Unknown dates/outcomes/medicines are kept unknown and listed.

## 4. Design proposals vs source requirements
Everything about the **field set, the status set, the visibility rules, the supplement mechanism and the queue** is a **design proposal** (`03`). The only source-derived elements are: keeping the reporter type distinct (S2), the "signal not proof" framing (S3 + D4 caveat), human-in-the-loop AI (S3), and the five **quoted definitions** (D1/D2, supplementary). No field is an official national form field.

## 5. Files and modules changed
Added: `Modules/ADR.Contracts/AdrContracts.cs`; `Modules/ADR/AdverseEventService.cs`, `Persistence/AdrDbContext.cs` + migration `InitialAdrSchema`; `Host/Adr/AdrEndpoints.cs`; web `adr/`, `features/adr/`, `scripts/phase9-qa.mjs`, `__tests__/phase9.test.tsx`; tests `Phase9Tests.cs`, `Phase9ApiTests.cs`; docs `docs/phase9/*`, `docs/pharmacovigilance-traceability-matrix.md`.
Modified (small): `ADRModule.cs`, `ADR.csproj`, `AuditContracts.cs` (5 constants), `security/access-catalog.json` (+1 permission) and its generated files (C#, TS, Dart, JSON copies), `Program.cs` (one `MapAdr()` line), `Fixture.cs` (register the ADR module), `routes.tsx`, `layouts/roles.ts`, `design/i18n/strings.mjs` and generated JSON, `docs/phase0/10-roadmap.md` (pointer note only), `docs/pharmacovigilance-source-analysis.md` (pointer note only). No Phase 5 manufacturer-report code, no clinical-rule code and no historical phase report was changed.

## 6. Test results
See `09-test-matrix.md`: .NET in memory 6 + 4 + 8 + 290 (15 skipped) + **405 (6 skipped)** + 133, IntegrationTests **3 skipped, not run**; PostgreSQL Patients **411/411**, Knowledge **305/305**; 8 contexts no EF drift; fresh DB OK; web **288/288**, eslint/tsc clean, build OK; browser QA **178 checks, 0 failures**; Flutter **157** passed, analyze clean.

## 7. Reviews still required
* **Clinical:** the field set, the category list, the review workflow wording, the meaning of "Assessed".
* **Regulatory/legal:** whether any record may ever be reported externally, by whom and how (S1 slide 6 is unverified); the facilitator role; the manufacturer/importer duties are **not** MedSmarter's.
* **Privacy:** retention, DPIA, access review for a pilot; the free text can contain anything the reporter writes (no screening, no anonymization claim).
* **Persian:** every Persian string awaits a named qualified reviewer; no Persian definitions exist.
* **Source:** verify D4's provenance; re-check the quoted definitions and printed page numbers against the original PDFs; obtain S2 slide 18 case content.

## 8. Traceability matrix
`docs/pharmacovigilance-traceability-matrix.md` (maintained; Phase 9 status in every row).

## 9. Roadmap mismatch
Phase 0's `10-roadmap.md` numbering differs from the delivered phases. Nothing was renumbered; a **pointer note** was added at the top of that file and the mapping is in `08-roadmap-reconciliation.md`.

## 10. Confirmation
**No external service, paid resource, real reporting connection or outbound call was used.** Tests ran locally with synthetic demonstration accounts; the PostgreSQL databases were local scratch databases (dropped afterwards); no web request was made to fill source gaps; no AI provider was configured or called; nothing in the product can send an ADR record anywhere.

## Limitations (honest list)
Not clinically validated; no emergency/urgent path; no retention policy; no Flutter screens; the assistance is a template (it adds no insight, only structure); date inputs carry a date only (stored as the start of that local day); the queue checks consent per distinct patient on every call (fine for a demo, to be optimised before scale); the audit line is written after the commit (the Phase 5 trade-off).

## Decisions needed from you
1. Approve (or change) the **proposed field set** and **status set**.
2. Approve the **five-term category list** as the only categories for now.
3. Who is the **clinical reviewer** and who is the **Persian wording reviewer**; may I build a reviewer registry + approval states (the `Approved` wording state is unreachable today)?
4. Provide the authoritative national reporting document(s) and legal basis, or confirm external reporting stays out of scope.
5. Decide whether a real (gated) AI provider may ever assist this workflow, given D3's "no prospective validation".
6. Verify or discard the Excel comparison (D4).
7. Flutter ADR screens: now (before Phase 10) or later?
8. Retention period and emergency-path design: product/clinical decisions.

## Proposed Phase 10 — pharmacy workflows (**not started**)
Grounded in the supplementary FIP source (not an Iranian legal requirement): (a) an **internal dispensing-error and near-miss log** using the typed lists of D2 §2.2–2.3 as optional tags, blame-free wording, no regulatory output; (b) **product-concern intake** linked to a ProductTrace batch record, kept **separate** from ADR classification, with no claim that a product is defective; (c) pharmacy-side access rules reusing relationship + consent; (d) Flutter parity for patient reports. Needs your answers to 3–4 above before clinical wording is written. **Wait for your approval; nothing of Phase 10 was started.**
