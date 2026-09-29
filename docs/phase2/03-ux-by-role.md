# 03 — UX by Role

اصل مشترک: **Information Architecture هر نقش متفاوت است** — نه فقط منو؛ زبان، تراکم، و «اولین سؤالی که صفحه پاسخ می‌دهد».

| | Patient | Physician | Pharmacist | Pharmacy | Industry |
|---|---|---|---|---|---|
| سؤال اول | «الان چه کنم؟» | «کدام بیمار نیاز به توجه دارد؟» | «کدام مورد را باید بازبینی کنم؟» | «چه چیزی باید امروز تحویل/سفارش شود؟» | «چه الگوی ایمنی در جمعیت دیده می‌شود؟» |
| واحد اصلی | دوز | بیمار | مورد بازبینی (Review) | نسخه/موجودی | سیگنال / روند تجمیعی |
| تراکم | کم، بزرگ، گرم | متوسط، جدول+کارت | بالا، صف کاری | بالا، عملیاتی | بالا، تحلیلی |
| لحن | همدلانه، ساده | بالینی، مختصر | بالینی-عملیاتی | عملیاتی | آماری، محتاط |
| داده فردی؟ | فقط خودش | بیماران با Grant | بیماران با Grant | نسخه‌های مرتبط | **هرگز** (فقط تجمیعی، k-suppression) |

## Patient
**Home** (از بالا): Greeting (نام، تاریخ، پیام گرم) → **Next Dose Hero** (دارو، ساعت، شمارش معکوس، دکمهٔ «مصرف کردم» + «بعداً») → Important Alerts (حداکثر ۲ نمایش، بقیه پشت «همه») → Today's Medications (Timeline: گذشته/الان/بعدی، وضعیت هر دوز) → Health Overview (Adherence Ring + ۳ آمار) → AI Assistant Entry (کارت Iris با ۳ سؤال سریع) → Check-in CTA (اگر امروز انجام نشده) → Recent Activity.
قانون شلوغی: بالای صفحه فقط یک اقدام اصلی (دوز بعدی). آمار عددی زیر Fold.

**Medications**: فهرست کارت‌ها با فیلتر (امروز/همه) → **Medication Detail**: Drug Name + Active Ingredient (Hierarchy: نام بزرگ، ماده مؤثره زیرش) → Strength · Dosage Form · Route (Chipهای سه‌گانه) → Schedule (ساعت‌ها) → Next Dose → Instructions (متن) → Warnings (AlertCard رنگ Warning با آیکون، جدا از Instructions) → «از دستیار بپرس» (پرسش از پیش پر شده دربارهٔ همین دارو) → منبع اطلاعات + DemoBadge.

**Check-in**: ۳ گام سبک (حال کلی با ۵ ایموجی-آیکون → علائم Chip چندانتخابی → یادداشت اختیاری) → خلاصه → «ارسال» (Mock). اگر علامت پرخطر انتخاب شود: کارت «با پزشک/داروساز تماس بگیرید» (بدون تشخیص).

**Profile**: اطلاعات، زبان (fa/en)، تم، اندازهٔ متن، اعلان‌ها، حریم خصوصی (چه کسی داده را دید — Mock)، Design System (Dev).

## Physician
Dashboard: Stat row (بیماران فعال، نیاز به توجه، ADR جدید، پایبندی میانگین) + «بیماران نیازمند توجه» (RiskCard مرتب‌شده) + خلاصهٔ AI کل لیست (AIInsightCard با منبع).
Patients: جدول (نام، سن، ریسک، پایبندی Sparkline، آخرین Check-in) با جستجو/فیلتر/Pagination.
Patient Overview: Header ثابت (PatientCard فشرده + آلرژی + ریسک) + Tabs: Overview (Timeline رویدادها) · Medications · Prescriptions (PrescriptionCard) · Adherence (نمودار ۳۰ روز) · Symptoms · ADR (ADRCard) · **AI Summary** (AIInsightCard + EvidenceCard؛ برچسب «پیش‌نویس هوش مصنوعی — نیازمند تأیید پزشک»).
Reports: فهرست گزارش‌های نمونه + خروجی (غیرفعال/Mock).

## Pharmacist
Dashboard = صف کاری اولویت‌دار: Medication Reviews (با پرچم تداخل) · Patient Questions (با پیش‌نویس پاسخ AI که فقط پس از تأیید ارسال می‌شود) · ADR جدید · Follow-ups امروز.
Medication Review: نسخه (PrescriptionCard) کنار InteractionCard‌ها؛ اقدامات: تأیید / نیاز به تماس با پزشک / ثبت مداخله (Mock).
سایر: ADR، Adherence (بیماران با افت)، Questions (Inbox دو-پنلی)، Follow-up (Timeline + وظایف).

## Pharmacy
Overview → Inventory (Table: موجودی، سطح سفارش، انقضا؛ Badge وضعیت) · Prescriptions (ورودی) · Dispensing (Kanban ساده: دریافت شد ← آماده‌سازی ← آماده تحویل) · Patient Requests (تمدید/پرسش) · Alerts (کمبود، انقضا، فراخوان نمونه).

## Industry
لایهٔ **تحلیلی-تجمیعی**: Analytics Overview (KPIها + روند) · ADR Trends (نمودار خطی/ستونی بر اساس دارو؛ سلول‌های کمتر از k به‌صورت «<k» ⇒ Suppression) · Patient Experience (نمرات تجمیعی) · Signals (جدول سیگنال‌ها با وضعیت بازبینی و قدرت شواهد) · Reports. هیچ Patient List/Detail وجود ندارد؛ Banner دائمی «Aggregate only · k ≥ 11 (Demo)». مقدار k در Phase 0 هنوز تصمیم نشده (D-27) ⇒ در UI صرفاً Demo.
