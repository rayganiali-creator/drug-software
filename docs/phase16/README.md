# Phase 16 — Clinical safety and evidence management

Proposed scope, reuse and exclusions: `docs/ROADMAP_PHASES_16_20.md`. No schema change, no migration, no network, no new clinical content.

## What changed (code)

| Area | Change | Files |
|---|---|---|
| Traceability | `KnowledgeStatementView` / `KnowledgeInteractionView` and `EvidenceItem` gained optional `RecordId` (stored statement/interaction id) and `RevisionId` (source revision id); the reader fills them | `MedicationContracts.cs`, `Reading.cs`, `AiContracts.cs`, `Evidence.cs` |
| Conflicts | A severity disagreement inside **one** source is now a conflict (`interaction.severity_disagrees`), as it already was across sources. Both sides stay visible; nothing is chosen | `Evidence.cs` |
| Stated uncertainty | `SafeMessages.EvidenceCaveat` (fa/en, written by the backend, chosen only by limitation codes `evidence.conflict/incomplete/not_validated/stale_source/truncated`) is put in front of the generated text; the demo-provider label stays first (`AssistantService.WithCaveat`). The model still receives only the evidence items | `Safety.cs`, `AssistantService.cs` |
| Grounding | `AnswerSafetyPolicy.Check` adds `uncited_answer` when evidence exists and the text cites nothing; the answer is then withheld (`Blocked`) and the sources shown | `Safety.cs` |

## Added in the pre-commit review

* **Citation exception: none for model text.** A model saying "the sources do not cover this" is never trusted. The only deterministic, backend-controlled insufficiency statements are the existing fixed `NoEvidence` message (zero evidence, no model call) and the new backend caveat, which is chosen from the recorded evidence limitations. A withheld (blocked) answer now shows that caveat in front of the fixed `Withheld` message, so when the evidence is really incomplete the person is told so in backend words, and when it is complete the model's insufficiency claim is not echoed.
* **Filter gap fixed:** "You should stop taking it [E1]." passed every filter (the medicine-change pattern needed a noun such as "medication"). `stop/skip/discontinue/quit` + `taking/using` is now caught (English only; the Persian patterns are unchanged and unreviewed).
* **Defect fixed in conflict detection:** the interaction qualifier was split at the first `|`; an ingredient name containing a bar shifted the severity and could hide a conflict. It is now split at the last `|`.
* **Known limit (tested, not solved):** a sentence that cites `[E1]` but is not true to E1 passes the citation rule; nothing deterministic can prove faithfulness.

## Behaviour decisions to review

* **Uncited text is now withheld.** Previously a calm sentence such as "The available sources do not cover this" passed. That test case was moved from "passes" to "caught (`uncited_answer`)". Trade-off: an honest "not covered" reply from a model is replaced by the generic withheld message plus the sources list. Decision for the owner (roadmap §Decisions 4).
* The caveat appears only when the evidence has one of the listed weaknesses; complete, validated, fresh evidence is returned unchanged. `evidence.publication_date_not_recorded`, `evidence.source_date_unknown` and `evidence.demo_data` keep their existing channels (limitations list and the demo notice) and do not add a caveat.
* Persian caveat wording is unreviewed, like all Persian text.

## Not done (blockers)

* Real publication / effective date of a source (needs a migration and a data-entry decision).
* Conflicts other than interaction severity (need structured data; free text is not interpreted).
* Any new clinical rule, evidence source or Urgent path.

## Tests

`tests/MedSmarter.Knowledge.Tests/Phase16Tests.cs` (13): ids from the real reference reach the items; ids copied unchanged; same-source conflict; non-conflicts stay non-conflicts; conflict caveat in front of a model reply that never mentioned it (en and fa); missing/unverified/stale each add their sentence; clean evidence gets no caveat; demo label first; uncited answer withheld; citation rules; AI module and rule engine do not reference each other. Two existing test inputs were changed for the new citation rule: the `Phase6Tests.cs` case described above, and the fake gateway reply in `tests/MedSmarter.Patients.Tests/ContextAndAiTests.cs` (`fake gateway answer` → `fake gateway answer [E1]`; those tests are about data gating, not grounding, and the old reply was uncited).
