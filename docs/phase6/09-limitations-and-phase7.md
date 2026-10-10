# 09 — Limitations, risks and the hand-over to Phase 7

**Not clinically validated. Not a prediction system. Not production-ready. Fictional content only.**

## Limitations
1. **No real language model has ever been run.** The Mock summarises *which* evidence exists; the External adapter has only been exercised against fake handlers; Local is a placeholder.
2. **Retrieval is lexical** (database search + normalisation). It can miss paraphrases and synonyms outside the reference; no semantic search. It reads only the medication knowledge base — no guidelines, labels, literature or patient records as evidence.
3. **Publication/effective dates are not recorded**; "stale" means *received* long ago. Conflict detection covers interaction severity only; contradictory free text is not detected.
4. **Keyword screens are blunt**: the emergency screen over-triggers on general questions and can miss real emergencies; injection and output policies are pattern lists, one layer among several, not guarantees. Persian clinical wording is developer-written and needs native medical review.
5. **Free-text privacy screening is not anonymisation** (names, places, rare details pass). A question sent externally can still contain personal data the patterns cannot see — hence the consent gate.
6. Professionals cannot use an external provider (they cannot grant the consent); no organisation-level approval exists.
7. The in-memory identity store, mock login, per-instance rate limit and audit-chain limits from the Phase 5 review are unchanged (`docs/repository-review-after-phase5/REPORT.md`: H-01, M-02, M-03, M-08 …).
8. The Flutter assistant is reached from the prototype banner; on desktop/other platforms nothing beyond the Phase 5 status is claimed.

## What Phase 7 must still build (not started, not replaced here)
Versioned backend clinical rules and guidelines; the **independent deterministic safety engine** that no model can override; laboratory and structured dose data; evidence-backed medication-risk assessment; alert entities with severity, evidence, uncertainty, freshness, delivery, acknowledgement, review, escalation and resolution; professional-facing alerts; any monitoring that might later be *validated* before it is described as predictive.
The Phase 6 assistant is a **source-based information tool** and must not be presented as that engine. Its screens and refusals are product boundaries, not clinical triage.
