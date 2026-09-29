# 06 — Security & Threat Model

روش: STRIDE + LINDDUN (برای حریم) + OWASP API Top 10 / OWASP LLM Top 10 به‌عنوان چک‌لیست. مقیاس ریسک: H/M/L (پیشنهادی).
چارچوب مقرراتی: **UNKNOWN** (D-01).

## 1. دارایی‌ها
PHI (نسخه، علائم، ADR، گفتگو)، اعتبارنامه‌ها، توکن‌ها، KnowledgeBase/قواعد بالینی (صحت = ایمنی بیمار)، کلیدهای رمز، Audit، مدل/پرامپت‌ها.

## 2. مهاجمان
بیمار مخرب/کنجکاو، پزشک/داروساز سوءاستفاده‌گر (دسترسی بی‌ربط)، Insider (Admin/Dev)، مهاجم خارجی، بات‌ها، Provider آلوده/نقض‌شده، سند آلوده دانش، Industry user که به دنبال re-identification است.

## 3. مرزهای اعتماد
Client ⟷ Gateway ⟷ Core ⟷ (DB/Redis/OS/S3) ؛ Core ⟷ AI Service ⟷ LLM Provider؛ Core ⟷ Providerهای خارجی؛ Admin plane.

## 4. Threat Model (جدول)
| # | حوزه | تهدید | ریسک | کنترل |
|---|---|---|---|---|
| T1 | **Authentication** | Credential stuffing، ATO | H | Argon2id (یا معادل تأییدشده)، Lockout تدریجی، Breached-password check، MFA (TOTP/WebAuthn؛ SMS فقط fallback، D-05)، Device binding، Notify on new login |
| T2 | Session/Token | سرقت توکن | H | Access کوتاه (≤15m)، Refresh rotation + reuse detection، Cookie HttpOnly/SameSite برای Web، Secure Storage در موبایل، Revoke |
| T3 | **Authorization** | IDOR/BOLA (دسترسی به بیمار دیگر) | H | Deny-by-default؛ هر endpoint: Permission (RBAC) **و** Relationship+Consent (ABAC)؛ تست ماتریسی خودکار؛ شناسه غیرقابل‌حدس |
| T4 | RBAC | Privilege escalation | H | نقش‌ها ثابت و کوچک؛ تغییر نقش = Audit + دو نفره برای Admin؛ Separation of duties |
| T5 | **ABAC** | دور زدن Consent | H | Policy Decision Point مرکزی؛ ویژگی‌ها: نقش، رابطه درمانی، AccessGrant، Purpose، Org، زمان؛ «Break-glass» فقط با دلیل + هشدار + بازبینی |
| T6 | **MFA** | Bypass/Fatigue | M | Rate-limit، Number matching یا TOTP، Recovery codes hash |
| T7 | **Secrets** | نشت در Repo/CI/Log | H | Secret manager، Secret scanning (push protection)، rotation، دسترسی حداقل، عدم Secret در Docker layer |
| T8 | **Encryption** | سرقت دیسک/Backup | H | TLS 1.2+ (ترجیحاً 1.3) همه‌جا + mTLS داخلی، رمزنگاری at-rest، ستونی برای PHI، KMS، Key rotation |
| T9 | **API Security** | Injection، Mass assignment، SSRF، BOPLA | H | Validation سخت‌گیر (schema)، Parameterized queries، DTO صریح (no bind-to-entity)، SSRF allowlist در Integrations، Security headers، CORS محدود، Size limits |
| T10 | **Rate Limiting** | Brute force، Scraping، DoS، Cost abuse AI | M-H | Redis token-bucket به‌ازای IP/کاربر/endpoint، سهمیهٔ AI، WAF، Bot detection (UNKNOWN) |
| T11 | **Audit** | حذف/دستکاری لاگ | H | Append-only، hash-chain، Role نوشتن-فقط، ارسال نسخه به Storage WORM (D-25)، هشدار بر الگوهای غیرعادی، دسترسی به Audit نیز Audit می‌شود |
| T12 | **Prompt Injection** | مستقیم (کاربر) و غیرمستقیم (سند KB/نسخه/تصویر/وب) | H | متن بازیابی = داده؛ جداسازی Roleها؛ **کاهش قدرت Tool** (فقط خواندنی)؛ Tool args اعتبارسنجی در Core؛ Output Guard؛ Sanitize سند هنگام Ingestion؛ بازبینی انسانی KB؛ Red-team |
| T13 | **Data Leakage (AI)** | نشت PHI به LLM Provider/Log/سایر کاربران | H | Pseudonymize + Minimization؛ قرارداد Provider؛ بدون آموزش روی داده؛ جداسازی Context هر کاربر (بدون حافظهٔ مشترک)؛ Cache پاسخ فقط برای سؤال عمومی بدون PHI؛ Log Redaction |
| T14 | Data Leakage (عمومی) | خطاهای verbose، Log حاوی PHI، Export | M-H | PHI-scrubber، پاسخ خطای عمومی، Export با Audit و Watermark، Presigned URL کوتاه |
| T15 | **Malicious Files** | بدافزار/PDF آلوده/Zip bomb/XXE/تصویر مخرب | H | Allowlist نوع (magic bytes)، سقف حجم، Antivirus scan (ابزار UNKNOWN)، Sandbox parse در Worker ایزوله بدون شبکه، Strip metadata/EXIF، ذخیره خارج webroot، Content-Disposition attachment، CDR (اختیاری) |
| T16 | **Tool Abuse** | AI را به فراخوانی Tool علیه دیگران بکشانند / Loop / هزینه | H | Allowlist، توکن on-behalf-of (نه Service-admin)، Consent-check در Core، سقف تعداد/عمق Tool، Timeout، Audit هر Tool call، بدون Tool نوشتاری MVP |
| T17 | Poisoning دانش | تغییر قاعده/سند KB ⇒ آسیب بیمار | H | دو نفره تأیید، امضای نسخه، Diff review، Rollback، تست‌های طلایی قواعد در CI |
| T18 | Integration | Webhook جعلی، Replay، Provider نقض‌شده | M-H | HMAC/امضا، timestamp/nonce، IP allowlist (اگر ممکن)، Idempotency، Schema validation، ایزوله Egress |
| T19 | Supply chain | وابستگی/Image آلوده | M-H | Lockfile، Dependabot، SCA، SBOM، Image scan، امضای Image، Pin به digest |
| T20 | Insider | Admin/DBA خواندن PHI | H | Least-privilege، JIT access، بدون دسترسی مستقیم prod DB، Audit، رمزنگاری ستونی با کلید جدا |
| T21 | Re-identification | Analytics صنعت | H | k-anonymity (k UNKNOWN، D-27)، Suppression، عدم ارائه ردیف‌های فردی، Query budget |
| T22 | Client-side | XSS، CSRF، Reverse engineering موبایل | M | CSP، Escape، CSRF token/SameSite، Cert pinning (اختیاری)، Root/Jailbreak detection (اختیاری)، بدون Secret در اپ |
| T23 | Browser/Share Extension | اکستنشن دسترسی بیش‌ازحد به صفحات | M-H | حداقل Permission، فقط عمل صریح کاربر، بدون خواندن پس‌زمینه، Review توسط Store (D-13) |
| T24 | Safety | پاسخ نادرست AI/قاعده نادرست | H | بخش ۵ سند ۰۵، Kill-switch، Incident response بالینی |
| T25 | Availability | DoS، وابستگی به LLM | M | Timeout/Circuit breaker، Degraded mode (بدون AI؛ Rule Engine و برنامه همچنان کار کند) |

## 5. الزامات پایه (Security Baseline)
1. Deny-by-default AuthZ + تست خودکار BOLA برای همه Endpoint.
2. MFA اجباری حرفه‌ای‌ها.
3. Audit برای هر خواندن/نوشتن PHI.
4. بدون PHI در Log/Metric/URL/Push.
5. Secret خارج از Repo + Push protection.
6. SAST + SCA + Secret scan + Container scan در CI (بلوک‌کننده).
7. Threat model به‌روزرسانی در هر Phase؛ Pen-test پیش از Pilot.
8. Incident Response Plan + تمرین Breach (مهلت اعلام قانونی: **UNKNOWN**).
9. Backup رمزنگاری‌شده + تست بازیابی.
10. Least privilege برای DB roles/Service accounts/K8s (یا معادل).

## 6. Authorization Model (ABAC) — تصمیم = f(Subject, Action, Resource, Context)
- Subject: user، نقش‌ها، org، وضعیت تأیید مجوز، MFA level.
- Resource: نوع داده، patient_id، دسته داده (Rx/Symptoms/ADR/Chat)، Sensitivity.
- Context: Purpose (Treatment/Dispensing/Self/Research)، زمان، Break-glass، دستگاه.
- شرط الزامی برای حرفه‌ای‌ها: `AccessGrant` معتبر ∧ Purpose سازگار ∧ Consent فعال ⇒ Allow؛ در غیر این‌صورت Deny + Audit.
- Patient به داده خود همیشه دسترسی دارد (به‌جز محدودیت‌های قانونی: UNKNOWN).
- Industry: فقط `analytics.read_aggregate` روی Datasetهای de-identified.

## 7. موارد باز
مدل Break-glass (D-28)، سیاست رمز/هویت‌سنجی دقیق (D-05)، الزامات مقرراتی (D-01)، ابزارهای Scan (UNKNOWN)، WAF/Gateway (D-21).
