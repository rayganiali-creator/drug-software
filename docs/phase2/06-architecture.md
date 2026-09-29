# 06 — Component Architecture, Mock Data, Flutter & React Structure

## 1. اصل: یک منبع، دو پیاده‌سازی
```
design/
  tokens.json           رنگ، تایپ، فاصله، radius، سایه، حرکت، breakpoint   ┐
  icons.mjs             ۶۷ آیکون (مسیر SVG مشترک)                           │ node design/build.mjs
  i18n/strings.mjs      متن UI به‌صورت [en, fa] (۵۷۳ کلید)                   │   ↓
  mock/demo-data.mjs    داده نمایشی ساختگی (با برچسب DEMO)                  ┘
                         ├─ web/src/design/tokens.css, tokens.g.ts, components/ui/icons.g.ts
                         ├─ web/src/shared/{en,fa,demo-data}.json
                         ├─ mobile/lib/design/tokens.g.dart, icons.g.dart
                         └─ mobile/assets/shared/{en,fa,demo-data}.json
```
- `node design/build.mjs --check` (CI): فایل‌های تولیدی بازتولید نشده = شکست.
- Contrast: ۴۱ جفت رنگ × ۲ تم در Build بررسی می‌شود؛ شکست = Build ناموفق.
- تست‌های Web و Flutter بررسی می‌کنند فایل‌های `assets/shared` موبایل با نسخهٔ وب یکسان باشد.

## 2. معماری کامپوننت (لایه‌ها)
```
Tokens  →  Primitives (Icon, Button, Field, Card…)  →  Composites (Table, Tabs, Chat, Timeline…)
        →  Healthcare components (MedicationCard, InteractionCard, AIInsightCard…)
        →  Feature screens (patient / physician / pharmacist / pharmacy / industry / assistant)
        →  App shell (routing, navigation, providers)
```
قواعد:
1. Feature ها فقط از لایهٔ کامپوننت استفاده می‌کنند؛ رنگ/فاصله/فونت سخت‌کد ممنوع (تست خودکار وب: «no hard-coded hex»).
2. فقط ویژگی‌های منطقی (`margin-inline`, `EdgeInsetsDirectional`) — تست خودکار وب برای `left/right` در CSS.
3. هر کامپوننت سلامت: `DemoBadge`، Loading/Empty/Error، معنای متنی (نه فقط رنگ)، Semantics.
4. Accessibility در خود کامپوننت (Tabs با roving tabindex، Modal با focus-trap، Toast با `aria-live`).
5. Feature ها فقط با **Service interface** کار می‌کنند، نه با داده مستقیم.

## 3. Mock Data Architecture
```
UI  →  Service interface (MedicationService, PatientService, PrescriptionService, AIService,
                          ADRService, NotificationService, AnalyticsService, PharmacistService, PharmacyService)
    →  createMockServices({ latencyMs, now })  ← demo-data.json (ساختگی)
```
- **همه** رکوردها `demo: true`؛ نام دارو/ماده مؤثره ساختگی و صریحاً «(fictional)»؛ بنر سراسری «DEMO DATA — NOT FOR CLINICAL USE» و `DemoBadge` روی هر کارت.
- ساعت تزریق‌پذیر (`now`) و تأخیر صفر برای تست؛ وضعیت (مصرف دوز، مرحلهٔ تحویل…) فقط در حافظه.
- AI Mock: قواعد کلیدواژه‌ای (بیشترین تطابق) + Red-flag ثابت + امتناع؛ **بدون LLM**. هیچ پاسخی توصیهٔ تغییر دوز نمی‌دهد (تست).
- صنعت: فقط داده تجمیعی؛ سلول‌های کمتر از k به‌صورت «<k» (پنهان‌سازی) — نشان می‌دهد UI با Privacy Phase 0 سازگار است.
- جایگزینی با Backend واقعی: یک پیاده‌سازی جدید از همان interface ها در `ServicesProvider` (web) / `AppController` (Flutter).

## 4. ساختار React (web/src)
```
main.tsx · App.tsx · AppProviders.tsx (I18n, Theme, Services, Toast) · routes.tsx (lazy per page)
design/        tokens.css (gen) · tokens.g.ts (gen) · base.css · fonts/ (Vazirmatn، OFL)
i18n/          I18nProvider (locale، dir، t، loc، فرمت‌کننده‌ها: ارقام فارسی، تقویم جلالی)
theme/         ThemeProvider (system | light | dark → data-theme)
hooks/         useBreakpoint, useReducedMotion
components/
  ui/          Icon, buttons, inputs, navigation, overlays, display, states, charts, chat (+ ui.css)
  health/      cards.tsx (۱۱ کامپوننت سلامت) + health.css
  brand/       Logo (Care Ring)
layouts/       AppShell (Sidebar/Rail/Drawer/BottomNav)، PageHeader، roles.ts (IA هر نقش)
services/      types.ts (قراردادها) · ServicesProvider (useAsync) · mock/createMockServices.ts
features/      landing · patient · assistant · physician · pharmacist · pharmacy · industry · designSystem
shared/        (generated) en.json fa.json demo-data.json
__tests__/     services, i18n, tokens, components, pages, api
scripts/       qa.mjs (axe + overflow + RTL روی ۳۲ صفحه × ۵ ترکیب) · flow.mjs (سناریوهای تعاملی)
```

## 5. ساختار Flutter (mobile/lib)
```
main.dart · app.dart (MaterialApp.router، locale/theme از AppController) · router.dart (go_router، ۵ شاخه)
design/        tokens.g.dart (gen) · icons.g.dart (gen) · theme.dart (ThemeData + ThemeExtension + context.text/colors)
core/          l10n.dart · formatters.dart (ارقام فارسی، جلالی) · models.dart · services.dart (interfaceها + MockServices)
               app_controller.dart (زبان، تم، سرویس‌ها، ساعت) · app_scope.dart (InheritedNotifier + context.t/loc/fmt)
components/    app_icon · tone · buttons · inputs · containers (Card، Dialog، BottomSheet، Toast)
               display (Avatar، Badge، Chip، List، States، Skeleton، Progress/Ring، Timeline، Stat، Alert، Charts)
               chat (ChatBubble، ThinkingDots، MessageComposer) · health (۱۱ کارت سلامت) · screen (ScreenScaffold، AsyncView)
features/      shell (BottomBar/Rail) · home · medications · assistant · checkin · profile · design_system
api/           health_client.dart (Phase 1، اختیاری، برای Profile → Developer)
assets/        fonts/ (Vazirmatn) · shared/ (generated)
test/          core (فرمت‌کننده‌ها، سرویس‌ها، هم‌سانی متن) · app (ناوبری، RTL، دستیار، چک‌این، دسترسی‌پذیری)
```
وابستگی‌های جدید Flutter: `go_router` (ناوبری اعلانی و Deep-link)، `flutter_svg` (همان آیکون‌های وب)، `intl` + `flutter_localizations` (ویجت‌های Material در RTL)، `shared_preferences` (ذخیرهٔ زبان/تم؛ اختیاری و مقاوم در برابر خطا).
وابستگی‌های جدید Web: `react-router-dom`؛ dev: `@testing-library/user-event`, `playwright-core`, `axe-core`, `@types/node`.
فونت Vazirmatn (OFL) به‌صورت محلی Bundle می‌شود؛ هیچ CDN/شبکه‌ای در زمان اجرا نیست.

## 6. Responsive در پیاده‌سازی
| | Web | Flutter |
|---|---|---|
| تشخیص | `useBreakpoint` (tokens) + CSS media | `MediaQuery` + `Breakpoints` |
| Patient compact | BottomNav ۵ مقصد + AI برجسته | BottomBar ۵ مقصد + AI برجسته |
| medium | Rail | Rail |
| expanded | Sidebar (Collapsible) | Rail + دو ستون در Home/Assistant |
| Table | compact → Card List | – |
| Overlay | Modal / BottomSheet / Drawer | Dialog / BottomSheet |
