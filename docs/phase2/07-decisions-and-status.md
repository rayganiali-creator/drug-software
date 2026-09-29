# 07 — Decisions, Limits, How to Run

## تصمیم‌های Phase 2 (بدون تغییر معماری Phase 0)
| # | تصمیم | دلیل |
|---|---|---|
| P2-1 | **یک منبع Token/آیکون/متن/داده** برای وب و Flutter، با Generator و بررسی CI | هم‌سانی Design Language؛ جلوگیری از Drift |
| P2-2 | فونت **Vazirmatn** (OFL) محلی | فارسی خوانا، Latin هم‌خانواده، بدون CDN (حریم خصوصی/آفلاین) |
| P2-3 | React: `react-router-dom` + CSS با Token (بدون Tailwind/کتابخانه UI) | هویت اختصاصی؛ وابستگی کم؛ کنترل کامل RTL |
| P2-4 | Flutter: `go_router`، `flutter_svg` (همان آیکون‌ها)، Material 3 با ThemeExtension | ناوبری Deep-link، آیکون یکسان، تم Token-محور |
| P2-5 | نقش‌های حرفه‌ای فقط Web (مطابق پیشنهاد D-12) | Flutter = اپ بیمار |
| P2-6 | Alerts تب جدا نیست؛ روی Home | تحلیل Journey (سند ۰۲) |
| P2-7 | AI Mock بدون LLM؛ Voice/Image فقط نمونهٔ UI | هیچ Provider/دستگاهی وصل نیست (D-17 باز است) |
| P2-8 | k=11 فقط نمایشی | D-27 هنوز تصمیم نشده |

## محدودیت‌ها و مسائل شناخته‌شده
1. همهٔ داده‌ها ساختگی است؛ هیچ اعتبار بالینی ندارد. متن‌های فارسی نیاز به ویراستار بالینی/زبانی دارد.
2. احراز هویت/نقش واقعی نیست: «Role switcher» فقط نمایشی است (Phase بعد).
3. Voice: میکروفون استفاده نمی‌شود. Image: در وب فایل محلی برای پیش‌نمایش نام انتخاب می‌شود؛ در Flutter نمونهٔ ثابت (بدون دسترسی به دوربین/گالری). هیچ آپلودی نیست.
4. خروجی گزارش‌ها، جستجوی سراسری، تماس، اعلان Push: نمایشی.
5. تقویم جلالی وب از `Intl` مرورگر می‌آید؛ در Flutter الگوریتم داخلی (تست‌شده با تاریخ‌های شناخته‌شده).
6. آیکون‌ها با طراحی داخلی و ساده‌اند؛ برای برند نهایی باید توسط طراح بازبینی شوند.
7. تست خودکار دسترسی‌پذیری فقط axe (قوانین WCAG 2.2 AA خودکار). ارزیابی با Screen reader واقعی (TalkBack/VoiceOver/NVDA) دستی است و انجام نشده.
8. Flutter روی دستگاه/شبیه‌ساز Android/iOS اجرا نشده (SDK آن‌ها در محیط نیست). انجام‌شده: `flutter analyze`، ۴۳ تست (شامل RTL، دسترسی‌پذیری، مقیاس متن ۲۰۰٪، حرکت کاهش‌یافته)، و کامپایل نسخهٔ وب Flutter در یک کپی موقت + اسکرین‌شات با Chromium. پوشهٔ `web/` برای Flutter در مخزن نیست.
9. Bundle اصلی وب ~۴۵۰KB (۱۴۰KB gzip) به‌علاوهٔ صفحات lazy؛ بهینه‌سازی بیشتر انجام نشده.
10. Landing: متن‌ها فرضیه‌های محصول‌اند و ادعای مقرراتی/بالینی ندارند.

## اجرا
```bash
node design/build.mjs            # تولید/بررسی Token، آیکون، متن، داده
cd web && npm ci && npm run dev  # http://127.0.0.1:3000  (/  ، /app/patient ، /design-system)
cd mobile && flutter run         # یا: flutter run -d chrome
```
## تست
```bash
node design/build.mjs --check
cd web && npm run lint && npm run typecheck && npm test && npm run build
cd web && npm run preview &      # سپس:
node scripts/qa.mjs --shots --out /tmp/qa   # axe، سرریز افقی، dir/lang روی ۳۲ صفحه × ۵ ترکیب (تم/زبان/اندازه)
node scripts/flow.mjs                        # سناریوهای تعاملی (دستیار، دوز، نقش، کیبورد)
cd mobile && flutter analyze && flutter test
```
