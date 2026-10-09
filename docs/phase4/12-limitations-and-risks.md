# 12 — Limitations and known risks

Ordered by priority to fix before any real use.

1. **No clinical content.** Everything medical is fictional. A pharmacology/clinical validation process, licensed sources and legal review are required before real data (see the redistribution-licence rule in [02](02-data-model.md)).
2. **No persistent repository.** The API serves an in-memory store re-seeded at start; admin edits are lost on restart. The PostgreSQL schema exists but the repository over it is not written. Next step: implement the repository and run the same test suite against it.
3. **Search at scale.** The engine scans up to 2000 candidates in memory (`MaxCandidates`). Fine for a demo catalogue; a real catalogue needs the `pg_trgm` index/queries that the schema already prepares.
4. **Rate limiting is in-process.** Per-node counters; behind several instances or a proxy it must be revisited. Proxy/forwarded-header trust is not configured.
5. **External AI is unproven.** The adapter follows a neutral contract; a real vendor needs a thin gateway or an adapter, plus a data-protection review (the `Ai:AllowExternalDataTransfer` flag stays off by default). Prompt-injection and output-safety evaluation is not done.
6. **Local AI is only a port.** No model runtime, no hardware sizing, no quality evaluation.
7. **Insurance is architecture only.** No real insurer contract, consent flow, or national-id handling policy.
8. **Platforms.** Only Web (Chromium) and Linux desktop (debug build, virtual display) were exercised. Windows, macOS, Android, iOS are untested; Linux release packaging, signing and notarisation are not done. Firefox/Safari are not tested.
9. **Accessibility.** Automated axe checks and manual keyboard paths on the web; no screen-reader testing on any platform.
10. **Flutter RTL detail.** Strength strings are isolated LTR; other mixed-direction strings were not exhaustively reviewed on-device.
11. **Audit store.** Hash-chained but in memory, as in Phase 3.
12. **Migrations on startup.** Applying migrations automatically is only for local development; production needs a controlled deployment step.
13. **Tests that are opt-in.** PostgreSQL tests need `MEDSMARTER_PG_TEST`; Integration tests need Docker/network services and are skipped by default.
