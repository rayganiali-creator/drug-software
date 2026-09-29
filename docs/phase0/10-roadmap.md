# 10 — Roadmap (Phaseهای کوچک)

قواعد: هر Phase مستقل تحویل‌پذیر، ≤ ~۲–۳ هفته (تخمین زمان: **UNKNOWN**، وابسته به اندازه تیم – D-30)، با Definition of Done مشترک:
تست‌های واحد+یکپارچگی سبز، Architecture tests سبز، SAST/SCA/Secret scan بدون Finding بحرانی، Threat model و جدول Privacy به‌روز، OpenAPI به‌روز، Audit برای هر مسیر PHI، مستند Runbook مختصر.
«Files» = مسیرهای هدف (پیش‌بینی، قابل تغییر).

**Gate ها:** G1 = D-01,D-16,D-17 پاسخ داده شده قبل از Phase 5/11؛ G2 = Pen-test و DPIA قبل از Pilot.

---
## P1 — Foundation & Engineering Platform
- **Goal**: اسکلت قابل‌اجرا و خط CI امن.
- **Features**: Solution ماژولار خالی، Host، BuildingBlocks (Result, Outbox abstraction, Clock)، Docker Compose (Postgres, Redis, OpenSearch, Kafka اختیاری, MinIO)، Health checks، Logging/OTel، Config، Error format.
- **Files**: `src/Host`, `src/BuildingBlocks`, `src/Modules/*/(Contracts…)` خالی، `docker-compose.yml`, `.github/workflows/ci.yml`, `docs/adr/*`.
- **DB**: schema خالی + ابزار Migration.
- **API**: `/health`, `/version`.
- **Tests**: Architecture tests (مرز ماژول)، Smoke.
- **Security**: Secret scanning، Dependabot، Container scan، عدم Secret در Repo.
- **Acceptance**: `docker compose up` → health سبز؛ CI روی PR اجباری؛ نقض مرز ماژول CI را قرمز می‌کند.

## P2 — Identity, RBAC, MFA
- **Goal**: احراز هویت و مجوز پایه.
- **Features**: Register/Login، Password hashing، MFA، Refresh rotation، نقش‌ها/مجوزها، Policy handler، Lockout، Rate-limit.
- **Files**: `Modules/Identity/*`, `Host/Auth`.
- **DB**: User, Role, Permission, UserRole, RolePermission, Device, MfaFactor, RefreshToken.
- **API**: `/auth/*`, `/me`, `/admin/roles`.
- **Tests**: Auth flow، رمز/MFA، BOLA-skeleton، Rate-limit، توکن منقضی/revoked.
- **Security**: Argon2id، MFA اجباری حرفه‌ای‌ها، Session hardening.
- **Acceptance**: نقش‌ها اعمال؛ MFA کار می‌کند؛ Refresh reuse ⇒ ابطال زنجیره.

## P3 — Audit & Consent Core
- **Goal**: ردپا و رضایت قبل از هر داده بالینی.
- **Features**: AuditWriter (hash-chain)، Consent/AccessGrant، ConsentEvaluator (ABAC PDP)، «چه کسی داده مرا دید».
- **Files**: `Modules/Audit`, `Modules/Consent`.
- **DB**: AuditLog, Consent, AccessGrant.
- **API**: `/consents`, `/access-grants`, `/audit-logs`, `/me/access-log`.
- **Tests**: Tamper-detection، ماتریس Consent، ابطال فوری.
- **Security**: Append-only privileges، دسترسی Audit ثبت.
- **Acceptance**: هر Endpoint نمونه بدون Grant ⇒ 403 + Audit.

## P4 — Users & Actors (Patients, Physicians, Pharmacists, Pharmacies)
- **Goal**: پروفایل‌ها و تأیید حرفه‌ای.
- **Features**: پروفایل‌ها، عضویت داروساز-داروخانه، وضعیت تأیید مجوز (فرایند دستی Admin تا روشن‌شدن D-04)، Patient allergies/conditions (پس از D-06).
- **Files**: `Modules/{Users,Patients,Physicians,Pharmacists,Pharmacies}`.
- **DB**: Patient(+Allergy/Condition), Physician, Pharmacist, Pharmacy, PharmacistMembership.
- **API**: `/patients`, `/physicians`, `/pharmacists`, `/pharmacies`.
- **Tests**: مالکیت/Tenant isolation، RLS.
- **Security**: Column encryption PHI، ماسک.
- **Acceptance**: بیمار A هرگز B را نمی‌بیند؛ حرفه‌ای بدون تأیید نمی‌تواند نسخه بنویسد.

## P5 — Medications & ActiveIngredients (Reference data)
- **Goal**: کاتالوگ دارو.
- **Features**: CRUD مرجع (Admin)، Import از منبع مجاز (پس از G1)، جستجوی فازی fa/en در OpenSearch، Outbox→Indexer.
- **Files**: `Modules/{Medications,ActiveIngredients}`, `workers/indexer`.
- **DB**: Medication, ActiveIngredient, Manufacturer, MedicationIngredient.
- **API**: `/medications`, `/active-ingredients`.
- **Tests**: Search relevance (فارسی)، Reindex idempotent.
- **Security**: Import validation، مجوز داده.
- **Acceptance**: جستجوی نمونه‌ها p95 < 300ms؛ داده دارای Provenance.

## P6 — KnowledgeBase (Ingestion & Versioning)
- **Goal**: دانش منبع‌دار نسخه‌دار.
- **Features**: Source/Document/Version، Upload امن، Scan، Approve دو نفره، انتشار/Rollback (هنوز بدون Embedding).
- **Files**: `Modules/KnowledgeBase`, `workers/ingestion (Python, skeleton)`.
- **DB**: KnowledgeSource, KnowledgeDocument, KnowledgeVersion.
- **API**: `/knowledge/*`.
- **Tests**: گردش‌کار تأیید، فایل مخرب (EICAR/zip bomb)، Immutability.
- **Security**: Malicious file pipeline، Sandbox parse.
- **Acceptance**: بدون دو تأیید انتشار ممکن نیست؛ Rollback کار می‌کند.

## P7 — Clinical Rules Engine (v1: تداخل/آلرژی/تکرار)
- **Goal**: موتور ایمنی قطعی.
- **Features**: DrugInteraction (از KB تأییدشده)، Evaluate(context)، SafetyResult قابل توضیح، Override با دلیل، Golden tests.
- **Files**: `Modules/ClinicalRules`, `tests/GoldenSafety`.
- **DB**: DrugInteraction, ClinicalRule, RuleVersion, SafetyAlert, AlertOverride.
- **API**: `/safety/check`, `/clinical-rules`, `/drug-interactions`.
- **Tests**: Golden (منبع بالینی)، Property-based، Regression؛ تأیید بالینی دو نفره.
- **Security**: تغییر قاعده = دو نفره + Audit.
- **Acceptance**: مجموعهٔ طلایی 100%؛ هر هشدار منبع/نسخه دارد.

## P8 — Prescriptions
- **Goal**: نسخه و آیتم‌ها با ایمنی.
- **Features**: Draft→Issued→…، نسخه پزشک و خودگزارشی، Attachments، Safety check در Issue، Events.
- **Files**: `Modules/Prescriptions`.
- **DB**: Prescription, PrescriptionItem, StatusHistory, Attachment.
- **API**: `/prescriptions*`.
- **Tests**: State machine، Grant/Consent، Idempotency.
- **Security**: BOLA، Malware scan.
- **Acceptance**: نسخه بدون Safety check نهایی نمی‌شود (یا Override مستند).

## P9 — Schedule, Intake, Notifications
- **Goal**: برنامه، ثبت مصرف و یادآوری.
- **Features**: تولید برنامه (Timezone/DST)، Intake idempotent + آفلاین sync، اعلان Push/Email، ترجیحات، Quiet hours.
- **Files**: `Modules/{MedicationSchedule,MedicationIntake,Notifications}`.
- **DB**: MedicationSchedule, MedicationIntake, Notification, Device.
- **API**: `/schedules`, `/intakes*`, `/devices`, `/notifications`.
- **Tests**: DST/Timezone، Sync conflict، Notification retry.
- **Security**: Push بدون PHI.
- **Acceptance**: مصرف آفلاین بدون تکرار همگام می‌شود.

## P10 — Adherence, Symptoms, ADR
- **Goal**: پایش و عوارض.
- **Features**: Adherence (فرمول تأییدشده D-09)، Symptoms + Red-flag rules، AdverseEvent + بازبینی.
- **Files**: `Modules/{Adherence,Symptoms,ADR}`.
- **DB**: AdherenceSnapshot, Symptom, AdverseEvent, AdverseEventReview.
- **API**: `/adherence`, `/symptoms`, `/adverse-events*`.
- **Tests**: فرمول‌ها، Red-flag golden.
- **Security**: Consent برای اشتراک ADR.
- **Acceptance**: Red-flag ⇒ پیام ارجاع قطعی؛ ADR گردش‌کار کامل.

## P11 — AI Service Foundation (RAG بدون Tool بیمار)
- **Goal**: پاسخ اطلاعاتی منبع‌دار.
- **Features**: FastAPI، LLM Adapter، Embedding، Indexing (OpenSearch)، Retriever، Guards، Refusal، Streaming، Eval harness v1، Conversation/Message/AIResponse در Core.
- **Files**: `ai/` (app, guards, rag, eval)، `Modules/AI`.
- **DB**: Conversation, Message, AIResponse.
- **API**: `/ai/conversations*`, `/internal/ai/*`.
- **Tests**: Golden Q&A، Refusal، Injection red-team، Latency.
- **Security**: mTLS، on-behalf-of token، Egress allowlist، Redaction، Kill-switch.
- **Acceptance**: Gate G1 پاس؛ Eval thresholds (D-26) پاس؛ پاسخ بدون ارجاع ممنوع.

## P12 — AI Tools (خواندنی) & Safety Integration
- **Goal**: پاسخ شخصی‌سازی‌شده امن.
- **Features**: Tools (`check_interactions`, `get_my_schedule`, …)، Tool Gateway با Consent، Output consistency با Rule Engine، escalate_to_human.
- **Tests**: Tool abuse، Cross-user leakage، Consistency 100%.
- **Security**: T12/T13/T16 کنترل‌ها.
- **Acceptance**: هیچ نشت بین‌کاربری در Red-team؛ AI نتیجهٔ Rule Engine را تغییر نمی‌دهد.

## P13 — Web Dashboard (React)
- **Goal**: رابط پزشک/داروساز/Admin.
- **Features**: Auth+MFA، بیماران با Grant، نسخه، هشدار ایمنی، ADR، Knowledge/Rules admin، Audit viewer، RTL/i18n.
- **Files**: `web/`.
- **API**: مصرف‌کنندهٔ API موجود.
- **Tests**: Component، E2E (Playwright)، a11y.
- **Security**: CSP، XSS، CSRF، بدون PHI در storage مرورگر.
- **Acceptance**: سناریوهای J4/J5/J7 E2E سبز.

## P14 — Mobile App (Flutter)
- **Goal**: اپ بیمار.
- **Features**: Auth، Schedule، Intake آفلاین، Symptoms، AI Chat، Consent UI، Notifications.
- **Files**: `mobile/`.
- **Tests**: Widget/Integration، Offline sync.
- **Security**: Secure storage، Screen-capture guard (اختیاری)، بدون Secret.
- **Acceptance**: سناریوهای J1/J2/J3.

## P15 — Integrations Framework
- **Goal**: Port/Adapter/Resilience بدون Provider واقعی.
- **Features**: Ports سند ۰۸، Registry، Fake adapters، Webhook امضا، Retry/DLQ، IntegrationEvent.
- **Files**: `Modules/Integrations`.
- **DB**: Integration, IntegrationEvent, ExternalIdentifier.
- **API**: `/integrations`, `/webhooks/*`.
- **Tests**: Contract tests با Fake، Replay/Signature.
- **Security**: SSRF، Credential ref.
- **Acceptance**: تعویض Fake↔Real بدون تغییر Core (اثبات با دو Fake). Adapter واقعی فقط پس از مستند (D-07).

## P16 — Analytics (de-identified)
- **Goal**: داشبورد تجمیعی.
- **Features**: Pipeline رویداد، k-anonymity، Industry role، Export کنترل‌شده.
- **DB**: schema analytics.
- **Tests**: Re-identification tests.
- **Security**: T21؛ Consent جدا.
- **Acceptance**: هیچ گروه < k؛ هیچ ردیف فردی خارج نمی‌شود.

## P17 — Browser/Share Extension
- **Goal**: اشتراک محتوای دارویی به اپ.
- **Features**: Share target موبایل + افزونه (فقط اقدام صریح کاربر).
- **API**: `/shares`.
- **Security**: T23، حداقل Permission، اسکن فایل.
- **Acceptance**: Store review-ready (D-13).

## P18 — Hardening & Pilot Readiness
- **Goal**: آمادگی Pilot.
- **Features**: Pen-test، DPIA نهایی، Load test، DR drill، Runbooks، Monitoring/Alerting، Clinical safety review، آموزش کاربر.
- **Acceptance**: Gate G2؛ صفر Finding بحرانی؛ RPO/RTO اثبات‌شده.

## P19+ — Post-MVP
OCR/استخراج نسخه (Human-in-loop)، Providerهای واقعی، Caregiver، ADR reporting رسمی، Tools نوشتاری AI با تأیید UI، دوز (پس از منبع مجاز و تأیید بالینی).

## وابستگی‌ها
P1→P2→P3→P4→(P5,P6)→P7→P8→P9→P10; P6→P11→P12; P8/P10→P13,P14; P15 مستقل پس از P3؛ P16 پس از P10؛ P17 پس از P14؛ P18 آخر.
