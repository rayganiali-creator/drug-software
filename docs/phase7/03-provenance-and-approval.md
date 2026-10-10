# 3. Provenance and approval

Five questions are kept apart and never folded into a score:
1. **Execution status** — did the engine run this rule (`RuleOutcome`: Matched, NoMatch, MissingData, StaleData, InsufficientEvidence, NotApplicable, Unavailable, Error)?
2. **Evidence provenance and quality** — `EvidenceRef(sourceId, name, version, publicationDate?, validation, reviewStatus)`. The knowledge module records only when a source was **received**, so `publicationDate` is null and the UI says "Publication date not recorded"; nothing is invented. Reference-interaction evidence is taken from the Medications reference with its own validation status.
3. **Approval status** — the rule life cycle and activation policy.
4. **Finding severity** — Informational…Critical, never below what the reference says (Contraindicated → Critical); urgency (None/Routine/Soon/Prompt) is separate.
5. **Data completeness and freshness** — see 04.

A finding is **actionable** only if its rule is Active **and** every reference datum it rests on is Validated. Otherwise it is shown but labelled (demonstration / "not actionable") and raises no guidance.

## Demonstration rules
Five built-in rules (DEMO-INT-001, DEMO-ALG-001, DEMO-DUP-001, DEMO-DQ-001, DEMO-SYM-001), inserted as **Draft** with `isDemo`, author `demo-seed`, no reviewer, evidence = the single honest entry "DEMO rule (no clinical source – not clinically validated)". They can never be approved (policy), are evaluated only when `ClinicalRules:AllowDemonstrationRules` is on (Development/Testing; the host refuses it elsewhere), and their findings are never actionable. Nothing in the repository is fabricated as a reference, date or approver.
