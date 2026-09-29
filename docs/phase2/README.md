# Phase 2 — Design System & UI/UX

وضعیت: Prototype / Local. **هیچ وابستگی خارجی واقعی وجود ندارد**؛ همه داده‌ها Mock و با برچسب `DEMO DATA — NOT FOR CLINICAL USE` هستند.

| سند | محتوا |
|---|---|
| [01-design-system.md](01-design-system.md) | هویت، Token ها، رنگ، تایپوگرافی، حرکت، کامپوننت‌ها، دسترسی‌پذیری |
| [02-navigation-and-page-map.md](02-navigation-and-page-map.md) | تحلیل Journey، Navigation موبایل/وب، نقشهٔ صفحات |
| [03-ux-by-role.md](03-ux-by-role.md) | UX بیمار، پزشک، داروساز، داروخانه، صنعت |
| [04-ai-assistant-ux.md](04-ai-assistant-ux.md) | طراحی دستیار هوشمند |
| [05-responsive-and-platform.md](05-responsive-and-platform.md) | استراتژی Responsive، RTL/LTR، Platform-aware |
| [06-architecture.md](06-architecture.md) | معماری کامپوننت، Mock Data، ساختار Flutter و React |
| [07-decisions-and-status.md](07-decisions-and-status.md) | تصمیم‌ها، محدودیت‌ها، نحوه اجرا/تست |

## منبع واحد (Single Source of Truth)
```
design/tokens.json          → tokens.css (web) · tokens.g.ts · tokens.g.dart (Flutter)
design/i18n/strings.mjs     → en.json + fa.json (web و mobile)
design/mock/demo-data.mjs   → demo-data.json (web و mobile)
node design/build.mjs [--check]   # تولید / بررسی (CI از --check استفاده می‌کند)
```
Contrast (WCAG) هر جفت رنگ در هر دو تم هنگام Build بررسی می‌شود؛ شکست = Build ناموفق.
