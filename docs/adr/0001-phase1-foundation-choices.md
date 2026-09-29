# ADR 0001 — Phase 1 foundation choices

Status: accepted for Phase 1 (resolves parts of D-20, D-23; deviations from Phase 0 listed explicitly).

## Decisions
1. **.NET 10 (LTS)**, pinned by `global.json`. .NET 8 LTS ends support Nov 2026, so a new project starts on 10. (D-23)
2. **EF Core + Npgsql, EF migrations**, one `PlatformDbContext` in schema `platform` (outbox/inbox). Business modules get their own schema/context later. (D-23)
3. **Outbox tables now, Kafka publisher later.** Kafka runs in compose and is health-checked (admin client only); no producer/consumer yet. (D-20: Outbox from P1, as recommended.)
4. **OpenSearch accessed via plain HTTP (`HttpClient`)** for health; typed client deferred until search features exist (avoids an unused dependency).
5. **Central package management** (`Directory.Packages.props`), warnings-as-errors, `.editorconfig`.

## Deviations from Phase 0 (with reason)
- **Module layout: 2 projects per module (`X.Contracts` + `X`) instead of 5** (Domain/Application/Infrastructure/Api). Reason: 110 empty projects add build time and noise with no benefit; layers become folders inside `X` and are split only when a module has real content. The boundary that matters (other modules may reference only `.Contracts`) is preserved and enforced by `tests/MedSmarter.ArchitectureTests`.
- **Module schema names** are not created yet (Phase 0 doc 04 names them); each module creates its schema with its first migration.

## Not decided here (still open in docs/phase0/11-decisions.md)
LLM provider (D-17), deployment environment/KMS (D-21), identity implementation (D-22), vector store (D-19).
