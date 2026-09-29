# Phase 2 report (for external review)

## Context
Project AI MedSmarter (healthcare/pharma platform). Phase 0 = architecture docs; Phase 1 = repo foundation (ASP.NET Core, FastAPI, React, Flutter, Docker, CI). Phase 2 = design system + full UI/UX with Mock data only. No real external API, no LLM, no real drug data.

## What was built
- `design/`: single source of truth -> generator `node design/build.mjs` (`--check` in CI): tokens.json (colors light/dark, type scale, spacing, radius, elevation, motion, breakpoints; 82 WCAG contrast checks), icons.mjs (67 icons), i18n/strings.mjs (573 keys fa/en), mock/demo-data.mjs (fictional drugs/patients, all labelled DEMO DATA).
- Web (React 19 + TS + Vite, react-router): landing page (13 sections), design-system gallery, patient / physician / pharmacist / pharmacy / industry UIs, AI assistant (citations, confidence, evidence panel, safety notice, red-flag static message, refusal, human escalation, voice/image as prototype UI), responsive shell (sidebar/rail/drawer/bottom-nav), RTL/LTR, light/dark, Persian digits + Jalali dates, ~45 components incl. 11 healthcare cards, Mock service layer (9 service interfaces).
- Mobile (Flutter, one codebase Android+iOS): patient app, 5 tabs (Home, Medications, AI Assistant center, Check-in, Profile), adaptive bottom bar/rail, same components/tokens/icons/strings/data as web.
- Docs: docs/phase2/01..07 (design system, navigation, UX per role, AI UX, responsive, architecture, decisions).
- CI: added design check, web a11y/interaction QA, Flutter analyze/test.

## Verification
Web: 87 vitest tests, eslint/tsc/build clean; axe-core 0 violations over 160 page x theme x locale x viewport combos; interaction script passes. Flutter: analyze clean, 43 tests (RTL, 200% text, reduced motion, tap targets). Flutter compiled for web in a temp copy and screenshotted. Bugs found by tests and fixed (bordered radius crash, large-text overflows, bidi order of "500 mg").

## Not done / limits
Not run on real Android/iOS devices; no real screen-reader testing; voice/image are UI-only; role switcher is demo (no auth); Persian copy and icons need professional review; k=11 suppression threshold is a demo value (Phase 0 decision D-27 open).

## Open Phase 0 decisions still blocking real features
D-01 jurisdiction, D-16 drug knowledge source, D-17 LLM provider, D-21 hosting.

## Repo
Branch claude/amazing-darwin-kfj6sv, repo rayganiali-creator/drug-software. Run: `cd web && npm ci && npm run dev` (http://127.0.0.1:3000), `cd mobile && flutter run`.
