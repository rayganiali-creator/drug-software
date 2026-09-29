# AI MedSmarter – mobile (Flutter, Android + iOS)

Patient app, Phase 2 prototype: **Mock services and fictional demo data only** (no network needed).
Design tokens, icons, strings and demo data are generated from `../design` (`node ../design/build.mjs`).

```bash
flutter pub get
flutter run                                   # device / emulator
flutter run -d chrome --no-web-resources-cdn  # optional: needs `flutter create . --platforms web` locally
flutter analyze && flutter test
```
Structure and decisions: `../docs/phase2/06-architecture.md`. Cleartext HTTP is allowed in debug builds only.
