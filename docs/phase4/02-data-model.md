# 02 — Data model and design decisions

Source: `src/Modules/Medications/MedSmarter.Modules.Medications/Domain.cs` (entities) and `Persistence/MedicationsDbContext.cs` (PostgreSQL mapping,
schema `medications`, snake_case). Applied to a real local PostgreSQL 16 and exercised by `PostgresSchemaTests` (9 tests).

## Entities → tables
| Brief's entity | Table(s) | Notes |
|---|---|---|
| Medication | `medication` | stable internal id (UUID v7), `version` (concurrency token), `lifecycle` (Draft/Active/Inactive), `validation` (Demo/Unverified/NeedsValidation/Validated/Rejected), `is_demo` |
| ActiveIngredient | `active_ingredient`, `ingredient_synonym` | EN/FA names, optional ATC code (format-checked) |
| MedicationIngredient (+ Strength) | `medication_ingredient` | **strength lives here**: value + unit + "per" basis, per ingredient, so combinations are correct |
| Brand | `brand` | optional on a medication (a generic product has none) |
| Manufacturer | `manufacturer` | `manufacturer_code` unique when present, country ISO-2 |
| DosageForm, Route, TherapeuticClass, DrugClass | `reference_term` (+ `medication_route`, `medication_classification`) | one table, `kind` discriminator, unique `(kind, code)` |
| Indication, Contraindication, Warning, Precaution, AdverseDrugReaction, AdministrationInstruction, StorageInstruction | `medication_statement` | one table, `kind` discriminator; optional severity / frequency (ADR only) / population |
| DrugInteraction | `drug_interaction` | between two **ingredients**, stored once with `ingredient_a_id < ingredient_b_id` (check constraint), unique pair |
| KnowledgeSource | `knowledge_source` | publisher, type, URL, version, **licence name, redistribution allowed (nullable = not checked)**, restrictions, received-at |
| KnowledgeRevision | `knowledge_revision` | one ingested version of a source; status Draft/InReview/Validated/Rejected |
| MedicationVersion | `medication_version` | append-only snapshot (jsonb) of the full detail at each version + who/why |
| (search index) | `medication_search_term` | derived: normalized names/brand/ingredients/synonyms/codes; trigram GIN index |
| Identifiers (NDC, GTIN, ATC, …) | `medication_identifier` | scheme + value, **mandatory source revision**, unique `(scheme, value)` |
| MedicationSynonym | `medication_synonym` | |

## Design decisions
| # | Decision | Reason |
|---|---|---|
| D4-1 | Everything reference-related stays in the `Medications` module | avoids cross-module FKs before the other modules have behaviour; extractable later (contracts are DTO-only) |
| D4-2 | Seven clinical statement kinds share **one** table | identical shape (text FA/EN + source); seven tables would be seven copies of the same code. Kinds are still distinct in DTOs and UI |
| D4-3 | Dosage form / route / classes share **one** `reference_term` table | same reason; the code validates the kind when a medication references a term |
| D4-4 | Strength belongs to `medication_ingredient`, not to the medication | a combination product has several strengths; one string would lose which ingredient has which |
| D4-5 | Interactions are between **ingredients**, not products | the pharmacology is about substances; every product containing them inherits the information |
| D4-6 | Official identifiers are optional, need a **source revision**, are format-checked (GTIN check digit, ATC pattern) and are **forbidden on demo records** | no invented codes: an identifier without provenance is indistinguishable from a made-up one |
| D4-7 | No statement, identifier or interaction without a `knowledge_revision` (FK, restrict) | "no source, no statement" is enforced by the database, not only by code |
| D4-8 | Validation of a statement is **derived** from its revision and source, not stored | it cannot drift from the source it depends on |
| D4-9 | Medication validation `Validated` needs: a validated revision, a non-demo source, and `redistribution_allowed = true` | licensing must be confirmed before anything is shown as verified; demo/fictional can never be validated (also a DB check constraint) |
| D4-10 | Editing content **withdraws** validation (back to Unverified) | a validated record that was edited is no longer what was validated |
| D4-11 | Optimistic concurrency: `If-Match: <version>`, `409` on a stale edit; every change appends a `medication_version` | two editors cannot overwrite each other silently; full history |
| D4-12 | Deactivate, never delete | history and references stay valid; deactivated records disappear from search/detail for readers only |
| D4-13 | Search is a derived normalized-term index, not a search engine | enough at this scale; no Elasticsearch/OpenSearch dependency (the stack already has one but it is not needed) |
| D4-14 | Demo and real information never mix: a demo medication can only cite a Demo source, a real one cannot | prevents fictional text acquiring an official look |

## Constraints worth knowing (all verified against PostgreSQL)
unique `(scheme,value)` identifiers · FK restrict from statements/identifiers/interactions to revisions · `ck_drug_interaction_order` (A &lt; B) ·
`ck_medication_demo_not_validated` · name required (`name_en` or `name_fa`) on medication/ingredient/brand/manufacturer/term · strength needs value
**and** unit · concurrency token on `medication.version`.

## ER sketch
```
manufacturer 1─* brand 1─* medication *─1 reference_term(DosageForm)
medication 1─* medication_ingredient *─1 active_ingredient 1─* ingredient_synonym
medication *─* reference_term (route / class)        medication 1─* medication_synonym
medication 1─* medication_statement *─1 knowledge_revision *─1 knowledge_source
medication 1─* medication_identifier *─1 knowledge_revision
active_ingredient *─* active_ingredient  via drug_interaction (A<B) *─1 knowledge_revision
medication 1─* medication_version        medication 1─* medication_search_term
```
