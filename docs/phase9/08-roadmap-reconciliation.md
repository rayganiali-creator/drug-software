# 08 — Roadmap reconciliation (planned vs delivered numbering)

`docs/phase0/10-roadmap.md` (written in Phase 0) numbers 19+ planned phases. The **delivered** phases follow a different order. Historical reports are **not renumbered or rewritten**; this table is the navigation aid, and a short pointer note was added at the top of the Phase 0 roadmap.

| Delivered | What it delivered | Closest Phase 0 roadmap item(s) |
|---|---|---|
| Phase 1 | foundation, modular monolith skeleton | P1 |
| Phase 2 | design system, UI/UX prototype | (P13/P14 groundwork) |
| Phase 3 | identity, RBAC, consent, audit | P2, P3 |
| Phase 4 | medication knowledge core, AI architecture | P5, P6 (+ part of P11) |
| Phase 5 | patient record, medicines taken, batch/lot, manufacturer reports, guidance contract | parts of P4, P9, P10 |
| Phase 6 | AI assistant, evidence-grounded retrieval | P11 (+ part of P12) |
| Phase 7 | clinical safety engine, versioned rules | P7 |
| Phase 8 | adherence, scheduled reassessment, rule review UI | parts of P9, P10, P13 |
| **Phase 9 (this)** | **internal adverse-event record, human review** | P10 (the "ADR" part) |
| Phase 10 (proposed, not started) | pharmacy workflows (dispensing-error/near-miss log, product-concern intake) | none (Phase 0 had no pharmacy-error module) |
| later | reporting adapter, E2B/MedDRA, analytics | P15, P16, P19+ ("official ADR reporting" was already post-MVP) |

Phase 0 decision **D-11** ("ADR: receiving body, official format, causality method — internal recording in MVP, official output later") is consistent with Phase 9 and remains **open** for everything beyond internal recording. [OPEN] confirm whether the Phase 0 roadmap should be replaced by the current plan or kept as history (a pointer note is in place either way).
