# 05 — Responsive, RTL/LTR & Platform-aware

## Breakpoints (Window size classes)
compact <600 · medium 600–1023 · expanded 1024–1439 · large ≥1440. همان Token در CSS و Dart.

| Class | Web | Flutter (Patient) |
|---|---|---|
| compact | Patient: تک‌ستون + Bottom Nav؛ سایر نقش‌ها: Drawer؛ جدول ⇒ **Card List** (نه اسکرول افقی) | تک‌ستون + NavigationBar |
| medium | Rail + محتوای ۱–۲ ستونه؛ Detail به‌صورت Drawer/تمام‌صفحه | NavigationRail + ۲ ستون (Home: دوز بعدی | Timeline) |
| expanded | Sidebar 264 + محتوا تا 1200 + پنل ثانویه (مثلاً Evidence کنار Chat) | Rail + Master-detail (Medications: لیست | جزئیات) |
| large | همان + حاشیهٔ متعادل، Gridهای ۴ستونه | همان |

قواعد: طراحی **Mobile-first برای بیمار** (اقدام اصلی در ناحیهٔ شست)، **Desktop-first برای نقش‌های حرفه‌ای** (تراکم) با تبدیل معنادار در Compact — نه صرفاً Scale.

## RTL / LTR
- Web: `<html lang dir>` از i18n؛ فقط **Logical Properties** (`margin-inline`, `padding-inline`, `inset-inline`, `text-align: start`, `border-start-*`). ممنوع: `left/right` در CSS کامپوننت‌ها (تست ساده در CI).
- Flutter: `Directionality` از Locale؛ `EdgeInsetsDirectional`, `AlignmentDirectional`, `PositionedDirectional`؛ آیکون‌های جهت‌دار (Chevron/Back) با `matchTextDirection`.
- آیکون‌هایی که جهت معنایی دارند (ارسال، بازگشت، Chevron) Mirror می‌شوند؛ آیکون‌های غیرجهت‌دار (ساعت، قرص، قلب) نه.
- نمودار: محور زمان در RTL از راست به چپ (Mirror داده‌ای)؛ ارقام محلی.
- متن دوجهته: نام دارو/دوز `dir=auto`/`<bdi>`.
- تقویم: fa ⇒ جلالی (`Intl … u-ca-persian`)، en ⇒ میلادی.

## Platform-aware
| موضوع | Android | iOS | Web |
|---|---|---|---|
| Navigation transition | Material (Predictive back) | Cupertino slide + swipe-back | بدون انیمیشن صفحه‌ای سنگین |
| Switch/Picker | Material | `Switch.adaptive` / Cupertino sheet | Native `<select>`/Custom Menu |
| Haptics | Light impact روی «مصرف کردم» | همان | – |
| Scroll physics | Clamping | Bouncing | Native |
| Sheets | Modal bottom sheet | همان با گوشهٔ گرد بزرگ | Modal/Drawer |
| Input | Keyboard inset handling | همان | Keyboard shortcuts |
| Safe area | Edge-to-edge | Notch/Home indicator | – |
