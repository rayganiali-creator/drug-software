# 10 — Platform support: what was actually done and tested

One Flutter codebase (`mobile/`) covers Android, iOS, Windows, macOS and Linux; the web app is React. The domain contracts (DTOs, endpoints, permissions,
base URL) are shared; no platform has a platform-specific rule for authentication, permissions or drug data. Secrets: none in any client; the base URL is
build configuration (`--dart-define=API_BASE_URL`, `VITE_API_BASE_URL`).

| Platform | Project files | Built | Run | Automated tests | **Honest status** |
|---|---|---|---|---|---|
| **Web** (React) | yes | `vite build` ✔ | Chromium (Playwright) against the real API ✔ | 178 vitest · browser QA with axe (WCAG 2.2 AA) en/fa × light/dark × desktop/tablet/phone | **Tested** on Chromium/Linux. Firefox, Safari, mobile browsers not tested |
| **Linux desktop** (Flutter) | generated (`linux/`) | `flutter build linux --debug` ✔ | launched under Xvfb (software GL), auto-signed-in demo account, **real API**, drug reference list rendered (screenshot) | `flutter test` runs the same widget tests on the VM (not on the device) | **Builds and starts; rendering verified in a virtual display.** Not verified on a real desktop session, not release-built, no packaging |
| **Windows** (Flutter) | generated (`windows/`), title set | ✘ (needs Windows + Visual Studio) | ✘ | ✘ | **Prepared, untested.** Do not claim it runs |
| **macOS** (Flutter) | generated (`macos/`), sandbox entitlement `network.client` added so API calls work | ✘ (needs macOS + Xcode) | ✘ | ✘ | **Prepared, untested** |
| **Android** (Flutter) | existing (`android/`) | ✘ (no Android SDK here) | ✘ | VM widget tests only | **Untested on emulator/device** (not newly broken: nothing Android-specific changed, default URL uses `10.0.2.2`) |
| **iOS** (Flutter) | existing (`ios/`) | ✘ (needs macOS + Xcode) | ✘ | VM widget tests only | **Untested** |

## Desktop interface
The drug reference is not a stretched phone screen: from 1024 px the list and the selected medicine are shown **side by side** (web and Flutter);
Phase 2's navigation rail / sidebar, content max-width and gutters apply. RTL, Persian digits, language switching and the Jalali date formatting are unchanged.
No desktop-only features (window-size persistence, menus, keyboard shortcuts) were added.

## Secrets per platform (design)
* **Web:** access token in memory; refresh token in `sessionStorage` (Phase 3). No provider key exists in the bundle.
* **Mobile/desktop:** token in memory only (`ApiSession`); a real provider later should use the platform keystore for the refresh token (Keychain / Keystore / Credential Manager / libsecret) — **NOT BUILT**.
* **All:** AI provider keys live only on the server (`Ai__ApiKey`).

## What would complete the picture
Run `flutter build windows` / `flutter build macos` / `flutter build apk` / `flutter build ios` on machines with those toolchains and add them to CI (only Linux is in CI now);
run the Flutter integration tests on devices; smoke-test Firefox/Safari.
