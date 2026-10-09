# 08 — Platform status (what was really run)

One Flutter codebase (`mobile/`) for Android, iOS, Windows, macOS, Linux; React for the web. Contracts, endpoints, permissions and the configurable base URL
(`--dart-define=API_BASE_URL`, `VITE_API_BASE_URL`) are shared; no secret is in any client.

| Platform | Phase 5 screens | Status — only what was actually done |
|---|---|---|
| **Web** (React 19) | all new pages (patient: record, medicines, batches, reports, sharing, messages · physician/pharmacist: report reviews, care requests · admin: report queue) | **Tested**: 199 vitest tests; browser QA on Chromium/Linux against the real API (PostgreSQL), en + fa × light + dark × desktop + phone, axe WCAG 2.2 AA clean. Firefox, Safari and mobile browsers **not tested** |
| **Linux desktop** (Flutter) | same screens as the app | `flutter build linux --debug` **builds** (this session). Earlier phase: launched under Xvfb with the real API. Phase 5 screens covered by widget tests only; **not** looked at in a real desktop session |
| **Windows** (Flutter) | same | **Prepared, untested** (needs Windows + Visual Studio). Do not claim it runs |
| **macOS** (Flutter) | same | **Prepared, untested** (needs macOS + Xcode) |
| **Android** (Flutter) | same | **Untested** on emulator/device (no Android SDK here). Default URL `10.0.2.2:5080` |
| **iOS** (Flutter) | same | **Untested** (needs macOS + Xcode) |

Flutter entry points: *Profile tab → My health record · Medicines I take · Batches (+ reports) · Sharing · Messages* (`/profile/records|taking|batches|sharing|messages`).
Professional/admin pages (report reviews, care requests, queue) exist **on the web only**; the Flutter app is the patient app.
Desktop layout: web uses the Phase 2 sidebar + two-column cards from 1024 px; Flutter uses the existing responsive `ScreenScaffold` (max width + gutters), it does not stretch a phone layout.
Not done: window-size persistence, native menus, keyboard shortcuts, secure token storage in the platform keystore (tokens live in memory only).
