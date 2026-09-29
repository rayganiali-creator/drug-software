# 01 — Design System

## 1. هویت: «Calm Intelligence»
محصول باید سه حس را هم‌زمان منتقل کند: **مراقبت انسانی** (گرم، آرام)، **اعتماد بالینی** (دقیق، شفاف) و **هوش** (مدرن، بدون هیاهو).

| تصمیم | چرا |
|---|---|
| **Teal عمیق** به‌جای آبی SaaS | آبی پیش‌فرض همهٔ Templateهای SaaS است؛ Teal هم حس سلامت/دارو می‌دهد هم آرام است |
| **Iris (بنفش-آبی)** فقط برای هر چیزی که «AI» است | کاربر همیشه می‌داند چه چیزی از هوش مصنوعی آمده؛ رنگ AI هیچ‌وقت برای وضعیت بالینی استفاده نمی‌شود |
| **Navy** برای ساختار (Secondary) | وزن و اعتبار، بدون تکیه بر مشکی |
| زمینهٔ روشن **سرد-سبزتاب** (نه سفید خالص) | خستگی چشم کمتر؛ کارت‌های سفید روی آن «شناور» می‌شوند |
| **Coral** فقط تزئینی/گرم (Greeting، Illustration، نمودار) | لحن انسانی؛ هرگز برای خطر (Danger قرمز جدا است) |
| شکل‌ها: گوشه‌های نرم (radius 12–24)، سطوح تخت با سایهٔ تیره‌شدهٔ رنگی | شبیه پنل Admin قدیمی یا Chat کلون نیست |
| **وضعیت هرگز فقط با رنگ** نمایش داده نمی‌شود | همیشه آیکون + متن (WCAG 1.4.1) |

هویت بصری خاص: «Care Ring» — حلقهٔ پیشرفت (Adherence / دوز بعدی) به‌عنوان نماد مرکزی UI؛ در Logo، Hero و کارت‌ها تکرار می‌شود.

## 2. Tokens
منبع: `design/tokens.json`. هیچ Style سخت‌کد در UI مجاز نیست (Lint/Review). خروجی‌ها: CSS Variables (web)، ثابت‌های Dart (Flutter).

| دسته | Token ها |
|---|---|
| Color (semantic) | primary, secondary, accent(AI), success, warning, danger, info — هرکدام `on*`, `*Container`, `on*Container`؛ background, surface, surfaceElevated, surfaceSunken, border, borderControl, textPrimary/Secondary/Muted, focusRing, scrim, chart1–5 |
| Palette | teal, iris, navy, ink (خنثی)، green, amber, red, blue, coral — مقیاس 50–900 |
| Typography | display, h1–h4, body, bodySmall, caption, label, button + `compactScale` برای موبایل |
| Spacing | مقیاس ۴ پیکسلی: s0…s20 (0,4,8,12,16,20,24,32,40,48,64,80) |
| Radius | none 0 · xs 4 · sm 8 · md 12 · lg 16 · xl 24 · pill |
| Elevation | ۰–۴؛ در Dark سایه قوی‌تر و سطح روشن‌تر (Surface Elevated) |
| Icon size | xs16 sm20 md24 lg32 xl48 |
| Control height | sm36 md44 lg52 (حداقل هدف لمسی ۴۴) |
| Breakpoints | compact <600 · medium 600–1023 · expanded 1024–1439 · large ≥1440 |
| Layout | sidebar 264 · rail 76 · topbar 64 · bottomNav 68 · content max 1200 |
| Motion | instant 80 · fast 140 · base 220 · slow 360ms؛ easing standard/decelerate/accelerate |

### Dark / Light
هر Token معنایی برای هر دو تم تعریف شده. تم: `system` (پیش‌فرض) · `light` · `dark`. وب با `data-theme`؛ Flutter با `ThemeMode`. در Dark، رنگ‌های Primary روشن‌تر می‌شوند (teal.300) تا Contrast حفظ شود — نه صرفاً معکوس‌سازی.

### Contrast (اعمال‌شده)
۴۱ جفت رنگ × ۲ تم (۸۲ بررسی) در Build بررسی می‌شود (متن ۴.۵:۱ تا ۷:۱، مرز کنترل و Focus و نمودار ۳:۱).

## 3. Typography
**Vazirmatn** (SIL OFL): طراحی‌شدهٔ فارسی، خوانا در اندازهٔ کوچک، شامل Latin هم‌خانواده؛ یک Family برای fa و en ⇒ یکپارچگی Web/Mobile. Bundle محلی (بدون CDN). ارتفاع خط سخاوتمند (body 28/16 = 1.75) چون خط فارسی Ascender/Diacritic بلند دارد. ارقام: در fa، ارقام فارسی (`Intl` / تبدیل در Flutter)؛ ارقام داروها/دوزها (`10 mg`) همیشه LTR داخل متن RTL با `<bdi>`/`unicode-bidi`.

## 4. Motion
Subtle · Fast · Purposeful: تغییر State (hover/focus/press ≤140ms)، ورود پنل (220ms decelerate)، خروج (accelerate). ممنوع: Parallax، Bounce، انیمیشن مداوم غیرلازم. Reduced motion ⇒ همهٔ مدت‌ها ≈ ۰ (CSS media + `MediaQuery.disableAnimations`). تنها انیمیشن مداوم مجاز: «AI در حال فکر کردن» که با Reduced motion به نشانگر ایستا تبدیل می‌شود.

## 5. کتابخانه کامپوننت
| گروه | Web (React) | Flutter |
|---|---|---|
| Actions | Button, IconButton | AppButton, AppIconButton |
| Inputs | TextField, Search, Select, Dropdown(Menu), Checkbox, Radio, Switch | AppTextField, AppSearchField, AppSelect, Checkbox/Radio/Switch (adaptive) |
| Navigation | Tabs, SegmentedControl, Pagination, Sidebar, Topbar | Tabs, SegmentedControl, NavigationBar/Rail |
| Containers | Card, Modal, Drawer, BottomSheet | AppCard, dialog, BottomSheet |
| Feedback | Toast, Snackbar, Tooltip, Progress, Skeleton, Empty/Error/LoadingState | Snackbar/Toast, Tooltip, Progress, Skeleton, states |
| Data | Table, List, Timeline, ChartCard, StatCard, Avatar, Badge, Chip | List, Timeline, ChartCard, StatCard, Avatar, Badge, Chip |
| AI/Chat | ChatBubble, MessageComposer, AIInsightCard, EvidenceCard | همان |
| Healthcare | MedicationCard, DrugCard, PrescriptionCard, PatientCard, ADRCard, RiskCard, InteractionCard, AdherenceCard, CheckInCard, AlertCard | همان (زیرمجموعهٔ مرتبط با بیمار) |
Table/Pagination/Drawer/Dropdown در Mobile عمداً ندارند (الگوی Native: List / BottomSheet / Menu).

هر Healthcare Component: (۱) Visual hierarchy مشخص — نام دارو و «دوز بعدی» بزرگ‌ترین، هشدار بلافاصله زیر آن با آیکون؛ (۲) `DemoBadge` وقتی داده Mock است؛ (۳) حالت‌های Loading/Empty/Error.

## 6. دسترسی‌پذیری
Contrast تضمین‌شده با Build · Focus ring دو-لایه ۲px iris · ناوبری کامل با کیبورد (Tab/Shift+Tab/Esc/Arrow در Tabs/Menu) · Skip-link · Landmarkها (`header/nav/main/aside`) · `aria-live` برای Toast و پاسخ AI · Modal با Focus-trap و بازگرداندن Focus · هدف لمسی ≥44px · Reduced motion · معنا فقط با رنگ نیست · Flutter: `Semantics` روی کارت‌ها، `MergeSemantics` برای ردیف‌ها.
