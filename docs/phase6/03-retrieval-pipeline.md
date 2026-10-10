# 03 — Retrieval pipeline and provenance

**Backend:** the existing medication knowledge service (PostgreSQL / in-memory repository, `pg_trgm`-style search). **No vector database, no embedding service, no GPU, no external search** was added.
The seam is `IEvidenceRetriever`; a hybrid or vector retriever can replace `MedicationEvidenceRetriever` later without touching the answer logic.

Steps (`MedicationEvidenceRetriever`):
1. **Normalise** the question: Persian/Arabic letter variants (ك→ک, ي→ی …), Persian/Arabic-Indic digits, diacritics, ZWNJ/format characters, full-width and mathematical look-alikes, case.
2. **Find medications** through `IMedicationService.SearchAsync` (whole question, then meaningful words after a Persian/English stop-word list), or use the medication ids the client supplied.
3. **Read knowledge documents** through `GetKnowledgeDocumentAsync` (active medications only).
4. **Build evidence items** (`E1…`): one per source-bearing statement and interaction. Identity facts without a source are not evidence.
5. **Quarantine** any text that looks like an instruction to the assistant (it is dropped, counted, and reported); **exclude** statements whose validation is `Rejected`; **cap** at 20 items and say so.
6. **Assess** (no numbers): quality, conflicts, missing information, limitations.

## Provenance kept on every item
`source.sourceId, name, version, publisher, receivedAt, validation`; item `kind`, `validation`, `isDemo`, `stale`, `sourceDateUnknown`; medication name and record version.
**Not available and therefore not shown:** publication/effective dates — the system records only **when it received** a source (`ReceivedAt`). Every answer with evidence therefore carries the limitation
`evidence.publication_date_not_recorded`. Nothing is invented: no citation, no official approval status, no fact without a source.

## Missing, stale, conflicting, insufficient
| Situation | What the pipeline does |
|---|---|
| Nothing found | `EvidenceSet.Empty`, quality `None`, `evidence.none_found`; **the model is not called**; status `NoEvidence` |
| Source received longer ago than `EvidenceStaleAfterDays` | item `stale`, limitation `evidence.stale_source` |
| No receipt date | item `sourceDateUnknown`, `evidence.source_date_unknown` |
| Two sources disagree on an interaction's severity | `conflicts[]` (`interaction.severity_disagrees`) — both items stay visible, no side is chosen |
| The topic asked about has no statement (e.g. side effects) | `missingInformation: ["kind.AdverseReaction"]`, `evidence.incomplete` |
| Not source-validated / demo | `evidence.not_validated`, `evidence.demo_data` |

## Evidence quality (`EvidenceQuality`)
`None` · `DemoOnly` · `Unverified` · `Limited` · `Validated`. It describes **where the information comes from** (provenance status), **not** whether it is clinically correct, and it is never a probability.
`Validated` requires: every item validated, none stale/undated, no conflict, nothing missing for the topic, nothing cut or quarantined.

Known limit: conflict detection only covers interaction-severity disagreement. Semantic disagreement between free-text statements is **not** detected and needs human review.
