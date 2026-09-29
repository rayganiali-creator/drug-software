# 04 — AI Assistant UX

هدف: «همراه دارویی قابل‌اعتماد»، نه Chat عمومی. تمایز: **هر پاسخ منبع‌دار، با میزان اطمینان، اعلان ایمنی و مسیر انسانی**.

## اجزای صفحه
| بخش | توضیح |
|---|---|
| Header | هویت دستیار (Iris) + «پاسخ‌ها اطلاعاتی‌اند، نه تشخیص» + دکمهٔ History |
| Context Chip | «دربارهٔ: Demopril 10 mg» (وقتی از Medication آمده) — کاربر می‌تواند حذف کند |
| Quick Questions | Chipهای پرتکرار («دوز فراموش‌شده چه کنم؟»، «با غذا؟») |
| Conversation | ChatBubble کاربر (Primary container) و دستیار (Surface + نوار Iris) |
| **Assistant message anatomy** | متن ← **Confidence** ← **Sources** (Chipهای [1][2]) ← **Safety Notice** ← Actions (Evidence، سؤال ارجاعی، Escalate) |
| Evidence Drawer/Sheet | EvidenceCard: عنوان منبع، نسخه، بخش، گزیدهٔ متن (Mock)، تاریخ نسخهٔ دانش |
| Suggested follow-ups | ۲–۳ Chip زیر آخرین پاسخ |
| Human Escalation | «پرسش را به داروساز بفرست» ⇒ Sheet تأیید (چه چیزی ارسال می‌شود) ⇒ وضعیت «در انتظار داروساز» |
| Composer | Text + دکمهٔ Voice + دکمهٔ Image + Send؛ Enter=ارسال، Shift+Enter=خط جدید |
| History | فهرست گفتگوها (جستجو، حذف — Mock)؛ Web: پنل کناری؛ Mobile: صفحهٔ جدا |

## Confidence
سه سطح **High / Medium / Low** با آیکون+متن (نه فقط رنگ) و توضیح: «اطمینان یعنی میزان تطابق با منابع تأییدشده، نه قطعیت پزشکی». Low ⇒ لحن محتاط + دکمهٔ Escalate برجسته. اگر منبع کافی نباشد: **امتناع** («اطلاعات کافی در منابع ندارم») + Escalate — مطابق Phase 0 (RAG groundedness).

## Safety
- **Red-flag** (کلیدواژه‌های اورژانسی در Mock): پاسخ AI جایگزین می‌شود با کارت ثابت اورژانس (بدون LLM، مطابق Phase 0). Mock فقط نمونه است و **جایگزین ارزیابی بالینی نیست**.
- Safety Notice ثابت زیر هر پاسخ دارویی؛ بدون توصیهٔ تغییر دوز.
- هیچ‌جا «تشخیص» یا «دستور مصرف جدید» تولید نمی‌شود؛ Mock هم همین را رعایت می‌کند.

## Voice / Image (Prototype)
- Voice: دکمه وضعیت «در حال ضبط» را با UI نشان می‌دهد و متن نمونه درج می‌کند؛ **میکروفون واقعی استفاده نمی‌شود**.
- Image: انتخاب فایل محلی برای Preview؛ **هیچ آپلودی انجام نمی‌شود**؛ برچسب «در نسخهٔ Prototype تصویر تحلیل نمی‌شود».

## حالت‌ها
Loading (نقطه‌های فکر با Reduced-motion ایستا) · Streaming (متن تدریجی، `aria-live=polite`) · Error + Retry · Offline · Empty (Onboarding با Quick Questions).

## Mock AI
`AIService` قواعد کلیدواژه‌ای ساده روی پاسخ‌های از پیش نوشته‌شدهٔ DEMO (`design/mock/demo-data.mjs → ai`). **هیچ LLM/Provider واقعی** (D-17 هنوز باز است).
