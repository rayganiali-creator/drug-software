# گزارش فاز ۴ — AI MedSmarter (برای بازبینی)

**مخزن:** `rayganiali-creator/drug-software` · **شاخه:** `claude/amazing-darwin-kfj6sv` · **آخرین کامیت:** `365ece4`
**وضعیت کلی:** همه‌چیز محلی / DEV / MOCK است. هیچ سرور، VPS، دامنه، هاست یا API پولی خریداری یا فعال نشده است. تمام داده‌های دارویی **خیالی** و برچسب‌دار (DEMO DATA — NOT FOR CLINICAL USE) هستند. فاز ۵ شروع نشده است.

## ۱. هدف فاز ۴
هستهٔ دانش دارویی (Medication Knowledge Core)، معماری اقتصادی AI، آمادگی بیمه (فقط معماری)، و آمادگی چندسکویی (Web، Android، iOS، Desktop) بدون هیچ هزینه و بدون وابستگی تست‌ها به اینترنت.

## ۲. آنچه ساخته شد
**بک‌اند (.NET 10، مونولیت ماژولار)**
- ماژول‌های جدید: `Medications` (+Contracts)، `AI` (+Contracts)، `Integrations` (+Contracts)؛ endpointها در `src/Host/MedSmarter.Api/Knowledge/KnowledgeEndpoints.cs`.
- مدل دامنه با ۲۱ مفهوم (Medication، ActiveIngredient، Brand، Manufacturer، DosageForm، Strength، Route، Indication، Contraindication، Warning، DrugInteraction، MedicationVersion، KnowledgeSource، KnowledgeRevision و …)، شناسهٔ داخلی پایدار، فیلدهای اختیاری شناسهٔ رسمی (بدون مقدار جعلی)، نسخه‌بندی، provenance و اعتبارسنجی.
- قواعد: هر گزاره/شناسه/تداخل باید KnowledgeRevision داشته باشد؛ رکورد DEMO نمی‌تواند شناسهٔ رسمی داشته باشد و نمی‌تواند «Validated» شود (در دیتابیس هم check constraint دارد)؛ ویرایش، اعتبارسنجی را باطل می‌کند؛ قفل خوش‌بینانه با `If-Match`؛ حذف نرم (deactivate)؛ پیش‌نویس/غیرفعال فقط برای دارندگان `knowledge.manage` دیده می‌شود.
- جستجوی واقعی (بدون موتور خارجی): فارسی/انگلیسی، نام ژنریک/برند/ماده مؤثره، مترادف، جستجوی جزئی، نرمال‌سازی ي/ی و ك/ک و فاصله، رتبه‌بندی (exact 1000 / prefix 800 / word 600 / substring 300)، صفحه‌بندی، اعتبارسنجی ورودی (q بین ۲ تا ۶۴ نویسه، حداکثر ۸ توکن، limit ≤ 50، offset ≤ 1000).
- لایهٔ سرویس/DTO/خطای یکنواخت (ProblemDetails)/audit زنجیره‌هش‌دار، سیاست‌های `perm:` فاز ۳، rate limiting قابل‌پیکربندی (`search`، `ai`، `knowledge-write`)، CORS.
- سه مجوز جدید به کاتالوگ افزوده شد: `medication.read`، `insurance.import`، `insurance.read` (جمعاً ۴۹ مجوز، ۹ نقش).
- `MedicationKnowledgeDocument` برای RAG آینده؛ LLM هرگز مستقیم به DB دسترسی ندارد و دستیار فقط از `IMedicationService` بازیابی می‌کند.

**پایگاه‌داده**
- طرح EF Core/PostgreSQL، schema به نام `medications` با ۱۸ جدول snake_case، قیود check/FK/unique، ایندکس pg_trgm، توکن همروندی `version`، snapshot نسخه به‌صورت jsonb، و مایگریشن `InitialMedicationsSchema`.
- مایگریشن روی PostgreSQL 16 محلیِ موقت اعمال و راستی‌آزمایی شد.
- **نکتهٔ مهم:** API هنوز از repository درون‌حافظه‌ای استفاده می‌کند؛ repository مبتنی بر PostgreSQL ساخته **نشده** است.

**AI**
- `IAIProvider` با پیاده‌سازی‌ها: Disabled (پیش‌فرض)، `MockAIProvider`، `ExternalAIProvider` (قرارداد JSON خنثی `POST {BaseUrl}/complete`)، `LocalAIProvider` (فقط port؛ نیازمند `ILocalModelRuntime` که پیاده نشده).
- تنظیمات: Provider، BaseUrl، ApiKey، Model، Timeout، MaxTokens؛ هیچ secretی در مخزن؛ `.env.example` خالی/جعلی.
- `AiGuard`: Mock در Production رد می‌شود مگر `Ai:AllowMockInProduction`؛ انتقال داده به بیرون فقط با `Ai:AllowExternalDataTransfer=true`؛ اگر منبعی پیدا نشود provider صدا زده نمی‌شود؛ audit بدون متن سؤال.
- بدون API key برنامه با Mock کار می‌کند و Mock هرگز واقعی نمایش داده نمی‌شود.

**بیمه (فقط معماری)**
- `IInsuranceProvider`، `MockInsuranceProvider`، `InsuranceIntegrationService`، InsuranceMember، InsuranceCoverage، ExternalPatientIdentifier، ردگیری import، dedupe، تشخیص تضاد، retry امن، audit؛ فقط شناسه‌های خیالی؛ **بدون endpoint و بدون اتصال به بیمهٔ واقعی**؛ مجوزهای `insurance.*` به هیچ نقشی داده نشده.

**Web (React/TS)**
- صفحات جستجو و جزئیات دارو روی API واقعی؛ حالت‌های Loading / Empty / Error / not-found؛ برچسب DEMO؛ master-detail در عرض ≥۱۰۲۴px؛ RTL/فارسی/جلالی حفظ شد؛ `apiFetch` در backend احراز هویت.

**Flutter**
- کلاینت API، صفحات جستجو و جزئیات دارو، آدرس Base URL قابل‌پیکربندی بدون secret، پوشه‌های Windows/macOS/Linux ساخته شد (Android/iOS از قبل بود).

## ۳. وضعیت واقعی هر پلتفرم (بدون ادعای اغراق‌آمیز)
| پلتفرم | وضعیت |
|---|---|
| Web | تست‌شده روی Chromium (نه Firefox/Safari) |
| Linux Desktop | `flutter build linux --debug` موفق و اجرا زیر Xvfb روی API واقعی (نیاز به `libgtk-3-dev`) |
| Windows / macOS | پوشه‌ها آماده، **ساخته و تست نشده** |
| Android / iOS | **ساخته و تست نشده** در این محیط |

## ۴. نتایج تست‌ها (همین ماشین، ۲۰۲۶-۱۰-۰۹)
| مورد | نتیجه |
|---|---|
| `dotnet build -c Release -warnaserror` | ۰ هشدار، ۰ خطا |
| BuildingBlocks / Architecture / Security / Api | ۴ / ۵ / ۱۳۳ / ۸ — همه پاس |
| Knowledge.Tests | ۱۶۲ پاس، ۹ skip (PostgreSQL اختیاری) |
| Knowledge.Tests با `MEDSMARTER_PG_TEST` روی PostgreSQL محلی | **۱۷۱ از ۱۷۱ پاس** |
| IntegrationTests | ۳ skip (نیاز به stack داکر؛ اجرا نشد) |
| Web: eslint، tsc، vitest | تمیز؛ ۱۷۸ از ۱۷۸ |
| QA مرورگر (`qa.mjs`، `flow.mjs`، `knowledge-qa.mjs`) | ۰ خطا؛ axe: ۰ تخلف |
| Flutter analyze / test | بدون مشکل / ۹۱ پاس |
| `design/build.mjs --check` و `security/build.mjs --check` | به‌روز |

تست نشده: Firefox/Safari، Windows، macOS، Android، iOS، هر سرویس واقعی AI یا بیمه.

## ۵. واقعی در برابر Mock
- **واقعی (پیاده و تست‌شده):** قواعد دامنه، جستجو، endpointها/مجوزها/audit، اتصال UI، طرح PostgreSQL.
- **Mock:** تمام محتوای دارویی (۷ دارو + ۱ غیرفعال + ۲ تداخل خیالی)، MockAIProvider، MockInsuranceProvider.
- **ساخته نشده:** repository روی PostgreSQL، runtime مدل محلی، endpointهای بیمه، اتصال بیمهٔ واقعی، build سکوهای تست‌نشده. ExternalAIProvider فقط با handler جعلی تست شده، نه با هیچ vendor واقعی.

## ۶. محدودیت‌ها و ریسک‌ها (به ترتیب اولویت رفع)
1. repository مبتنی بر PostgreSQL (فعلاً داده با ری‌استارت از بین می‌رود).
2. حاکمیت داده بالینی: منابع دارای مجوز، فرایند اعتبارسنجی دارویی، بررسی حقوقی — پیش از هر داده واقعی.
3. build و تست روی Windows/macOS/Android/iOS؛ بسته‌بندی/امضا برای Linux/Windows/macOS انجام نشده.
4. ارزیابی AI خارجی/محلی: حفاظت داده، prompt-injection، ایمنی خروجی؛ مدل محلی فقط port است.
5. rate limiting درون‌پردازه‌ای است؛ پشت چند instance/پروکسی باید بازنگری شود.
6. جستجو حداکثر ۲۰۰۰ کاندید را در حافظه بررسی می‌کند؛ برای کاتالوگ واقعی باید از pg_trgm استفاده شود.
7. اعمال خودکار مایگریشن هنگام استارت فقط برای توسعهٔ محلی است.
8. دسترس‌پذیری: axe خودکار و صفحه‌کلید روی وب؛ تست screen reader انجام نشده. Audit store درون‌حافظه‌ای است (مثل فاز ۳).

## ۷. هزینه‌های احتمالی آینده (هیچ‌کدام فعال/خریداری نشده)
هاست API و دیتابیس مدیریت‌شده، API مدل زبانی (هزینه به‌ازای توکن) یا سخت‌افزار GPU برای مدل محلی، مجوز داده‌های دارویی، حساب‌های توسعه‌دهندهٔ Apple/Google، گواهی امضای کد، دامنه و TLS.

## ۸. اجرای محلی
- بک‌اند + وب روی API محلی بدون داکر: `make web-run-api` — جزئیات: `docs/phase4/04-local-setup.md`.
- Flutter: `cd mobile && flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5080`.
- تست PostgreSQL اختیاری: متغیر `MEDSMARTER_PG_TEST` را به یک دیتابیس محلیِ موقت بدهید (`docs/phase4/07-postgresql-and-migrations.md`).
- بدون API key: `docs/phase4/06-no-api-key.md`.

## ۹. مستندات در مخزن (`docs/phase4/`)
README، ۰۱ معماری، ۰۲ مدل داده، ۰۳ endpointها با نمونه، ۰۴ راه‌اندازی محلی، ۰۵ اجرای تست‌ها، ۰۶ بدون API key، ۰۷ PostgreSQL و مایگریشن، ۰۸ معماری AI provider، ۰۹ آمادگی بیمه، ۱۰ پشتیبانی پلتفرم، ۱۱ واقعی در برابر Mock، ۱۲ محدودیت‌ها، REPORT.md (به انگلیسی).

## ۱۰. درخواست از بازبین
لطفاً به‌ویژه این موارد را بررسی کنید: (الف) درستی قواعد provenance/validation و جلوگیری از ظاهر رسمی‌شدن داده بی‌منبع، (ب) طراحی AiGuard و جلوگیری از فعال‌ماندن Mock در Production، (ج) تصمیم نگه‌داشتن repository درون‌حافظه‌ای تا فاز بعد، (د) اولویت‌بندی بخش ۶. فاز ۵ تا تأیید شما شروع نمی‌شود.
