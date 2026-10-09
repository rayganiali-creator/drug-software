# 11 — Limitations and known risks (honest list)

**Security / privacy**
1. The identity store and login are still the **development mock** (in-memory accounts, no passwords). Nothing in this phase is safe to expose to real users.
2. Clients keep tokens in memory only (web refresh token in `sessionStorage`); no platform keystore yet.
3. De-identification is allow-list + free-text screening (`FreeTextGuard`). Free text can still hold a *quasi-identifier* a regex cannot see (a rare event + date + place). That is why adverse events and severe reports are always human-reviewed; a real deployment needs a documented review procedure and a DPIA.
4. A pseudonymous subject id equals the user id; it is not returned in manufacturer payloads, but the reviewer endpoint exposes it to the authorised reviewer (needed to act). Review it before real use.
5. Audit chain verification is a function/endpoint (`/audit/verify`); there is no external anchoring of the chain head, so a database administrator who rewrites the *whole* chain would not be detected.
6. Rate limits are per API instance (in memory).

**Data / medical**
7. All medical content, patients, batches and messages are fictional; guidance templates are **not clinically validated**.
8. Interaction logic only knows reference data; an unregistered medicine gets **no** interaction checking (clearly flagged to the person).
9. No unit conversion or dose-range validation; dose text is stored as the person typed it.
10. Time zones: doses are computed per patient time zone (default `UTC` if unset); daylight-saving edge cases are only lightly tested.

**Engineering**
11. Docker integration tests could not run here (no Docker daemon). PostgreSQL behaviour is covered by the scratch-database suites run locally and in CI.
12. Flutter: Windows, macOS, Android, iOS were **not** built or run. Professional/admin pages are web-only.
13. Outbox processing runs **only when an administrator calls `POST /manufacturer-reports/queue/process`**; there is no background worker, scheduler, dead-letter UI or alerting (deliberately: nothing may send by itself while only a Mock exists).
14. The web app's "my reports" payload view is read-only; there is no edit-after-submit (cancel and recreate).
15. Persian wording for clinical terms was written by the developer assistant and needs a native medical-language review.

**Performance**
16. Lists are not paginated (symptoms use `take=50`); fine for one person's data, not for a professional's patient list at scale. `LIKE`-based search uses `pg_trgm` indexes; no benchmark was run beyond the seeded data.

**Priority to fix first:** 1 (real identity) → 3/4 (privacy review) → 7 (clinical validation) → 12 (build the other platforms) → 5 (anchor audit head) → 16.
