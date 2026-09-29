# 07 — Privacy by Design

مبنای قانونی/مقرراتی: **UNKNOWN** (D-01). مقادیر Retention زیر **پیشنهاد اولیه**اند و باید با مشاوره حقوقی نهایی شوند (D-25).
اصطلاحات: Consent = رضایت صریح بیمار؛ Grant = AccessGrant؛ Audit = ثبت در AuditLog.

## 1. اصول
1. Data minimization و Purpose limitation (Purpose در هر دسترسی الزامی).
2. Patient-centric control: مشاهده، Grant/Revoke، خروجی، حذف.
3. Default private؛ اشتراک فقط opt-in.
4. Separation: هویت (Users) جدا از داده بالینی (pseudonymous ID برای Analytics/AI).
5. De-identification قبل از هر استفادهٔ ثانویه (Analytics/آموزش/Eval).
6. Transparency: صفحهٔ «چه کسی داده مرا دید».
7. Privacy در AI: PHI حداقلی به LLM؛ بدون آموزش با دادهٔ کاربر (مگر Consent جدا، D-17).
8. حذف واقعی: Cascade به Search index، Cache، Backup (طبق چرخهٔ انقضا)، Storage.

## 2. ماتریس داده
| داده | Who can access | Why (Purpose) | Retention (پیشنهاد) | Consent | Audit |
|---|---|---|---|---|---|
| هویت/تماس (نام، ایمیل، تلفن) | خود کاربر؛ Admin محدود (support، ماسک) | حساب، اعلان | تا حذف حساب + دورهٔ قانونی UNKNOWN | شرایط استفاده | تغییر و دسترسی Admin |
| اعتبارنامه/MFA/Session | فقط Identity (خود سیستم) | احراز هویت | تا تغییر/حذف؛ Session کوتاه | — | رویداد امنیتی |
| پروفایل بالینی بیمار (آلرژی، شرایط، بارداری) | بیمار؛ درمانگر با Grant | درمان/ایمنی | تا حذف؛ طبق قانون UNKNOWN | Consent صریح + Grant | هر خواندن/نوشتن |
| نسخه/آیتم‌ها | بیمار؛ پزشک صادرکننده؛ داروساز/داروخانهٔ مرتبط با Grant | درمان/تحویل | UNKNOWN (احتمالاً بلندمدت قانونی) | Grant | هر دسترسی |
| برنامه مصرف | بیمار؛ Grant | یادآوری/پایبندی | حذف/ناشناس پس از N ماه از پایان دوره (N: D-25) | Grant | نوشتن + دسترسی حرفه‌ای |
| ثبت مصرف (Intake) | بیمار؛ Grant | پایبندی/درمان | مانند بالا | Grant؛ اشتراک با پزشک جدا | دسترسی حرفه‌ای |
| پایبندی (مشتق) | بیمار؛ Grant | پایش | مانند Intake | Grant | دسترسی |
| علائم (متن آزاد [ENC]) | بیمار؛ Grant | ایمنی/ADR | مانند بالا | Grant | هر دسترسی |
| ADR (AdverseEvent) | بیمار؛ داروساز/پزشک ارجاع‌شده | ایمنی دارو | UNKNOWN (احتمالاً بلندمدت) | Consent جدا برای **ارسال به نهاد ناظر/صنعت** | هر دسترسی + هر خروجی |
| گفتگو با AI (Message) | خود کاربر؛ *بدون* دسترسی کارکنان مگر Incident با Break-glass | پاسخ/بهبود ایمنی | کوتاه (پیشنهاد 90 روز، سپس حذف/ناشناس) | Consent برای ذخیره + Consent جدا برای بهبود مدل (پیش‌فرض خاموش) | Metadata همیشه؛ محتوا فقط Break-glass |
| AIResponse متادیتا (مدل، ارجاع، guard) | Compliance/Clinical QA | ممیزی/ایمنی | 1–2 سال (پیشنهاد؛ بدون متن PHI) | شرایط | دسترسی |
| Consent / AccessGrant | بیمار؛ Compliance | اثبات رضایت | تا مدت قانونی پس از ابطال UNKNOWN | خودش | تغییر (تغییرناپذیر) |
| AuditLog | Auditor؛ Security | ممیزی | ≥ 1–7 سال (UNKNOWN؛ پیشنهاد حداقل 2) | — | دسترسی به Audit ثبت می‌شود |
| توکن دستگاه Push | سیستم | اعلان | تا ابطال/۹۰ روز بی‌فعالی | اجازهٔ اعلان | — |
| اعلان‌ها | کاربر | اطلاع | 90 روز | ترجیحات | — |
| فایل‌ها (تصویر/PDF نسخه) | بیمار؛ Grant | ثبت نسخه | مانند نسخه | Grant | هر دسترسی/دانلود |
| Integration payloadها | سیستم؛ Admin Integration (ماسک) | همگام‌سازی/عیب‌یابی | کوتاه (30 روز) سپس Hash-only | Consent برای اتصال | هر رویداد |
| KnowledgeBase | عمومی داخلی (Editors) | پاسخ/ایمنی | نسخه‌ها حفظ (ردیابی) | — | تغییر/انتشار |
| داده تحلیلی de-identified | Industry (تجمیعی) | پایش پس از بازار/پژوهش | طبق قرارداد UNKNOWN | **Consent جدا و صریح** برای مشارکت | هر Query و Export |
| Logهای فنی | Ops | عملیات | 30–90 روز؛ بدون PHI | — | دسترسی |
| Backups | Ops (JIT) | DR | 30 روز چرخشی | — | دسترسی |

## 3. حقوق کاربر (Data Subject Rights)
مشاهده، خروجی (فرمت ماشین‌خوان)، اصلاح، حذف/ناشناس، ابطال Consent، اعتراض به تحلیل. SLA: UNKNOWN (D-01).
استثنا: داده‌ای که قانوناً باید نگه‌داری شود (Prescription/Audit) ⇒ محدودسازی پردازش به‌جای حذف (UNKNOWN).

## 4. Consent Model
- دسته‌ها (Data Categories): `Rx, Schedule, Intake, Adherence, Symptoms, ADR, Chat, Profile`.
- Purposeها: `Treatment, Dispensing, SelfCare, SafetyReporting, Research/Analytics, AI-Improvement`.
- هر Consent: نسخهٔ متن، زمان، مدرک (evidence)، قابل ابطال، ابطال ⇒ دسترسی فوری قطع (کش کوتاه‌مدت + رویداد).
- Consent جدا برای: اشتراک با هر درمانگر، ارسال ADR، Analytics، بهبود AI. **Bundling ممنوع.**
- کودکان/قیم/ناتوان: **UNKNOWN** (D-03, D-01).
- اضطراری (Break-glass): فقط پزشک تأییدشده؛ دلیل اجباری؛ اعلان به بیمار؛ بازبینی بعدی.

## 5. کنترل‌های فنی
Pseudonymous IDs برای Analytics/AI؛ Tokenization قبل از LLM؛ Field-level encryption؛ RLS؛ ماسک در UI؛ Export کنترل‌شده؛ عدم PHI در Push/URL/Log؛
Retention Jobs با گزارش؛ حذف از OpenSearch/Redis/Storage؛ تست حذف؛ Privacy Impact Assessment (DPIA) قبل از هر Phase حاوی داده جدید.

## 6. Cross-border / Residency
محل ذخیره و LLM Provider ⇒ **UNKNOWN**، [DECISION REQUIRED] D-01/D-17. تا تصمیم، فرض: همهٔ PHI در Region مجاز واحد؛ LLM بیرونی فقط با داده pseudonymized.

## 7. Privacy Review Gates
هر Phase جدید که فیلد/فرایند PHI اضافه می‌کند: جدول این سند به‌روز شود + DPIA + تأیید Consent UX.
