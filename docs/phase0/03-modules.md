# 03 — Bounded Modules

قالب: **Purpose / Responsibilities / Entities / Dependencies / Public APIs**.
«Public APIs» = Interfaceهای `Contracts` (in-process) + Endpointهای HTTP مهم (جزئیات در سند ۰۹).
قانون: وابستگی فقط به `Contracts`. جهت وابستگی از بالا به پایین (چرخه ممنوع).

نقشهٔ لایه‌ای:
```
L0 Platform:  Identity, Consent, Audit, Notifications, Integrations
L1 Reference: Users, Medications, ActiveIngredients, KnowledgeBase
L2 Actors:    Patients, Physicians, Pharmacists, Pharmacies
L3 Clinical:  Prescriptions, MedicationSchedule, MedicationIntake, Adherence, Symptoms, ADR, ClinicalRules
L4 Intelligence: AI (رابط Core به AI Service), Analytics
```

## 1. Identity
- **Purpose**: احراز هویت، نشست، نقش/مجوز.
- **Responsibilities**: ثبت‌نام/ورود، MFA، توکن، Refresh rotation، Device binding، RBAC، Policy ABAC evaluator، Lockout.
- **Entities**: User(credentials بخش)، Role، Permission، UserRole، RolePermission، Session/RefreshToken، MfaFactor، Device.
- **Deps**: Audit، Notifications (کد MFA).
- **Public APIs**: `IIdentityContext` (CurrentUser)، `IAuthorizationService.Can(subject, action, resource)`، `/auth/*`.

## 2. Users
- **Purpose**: هویت عمومی و پروفایل پایه‌ی هر انسان.
- **Resp.**: نام، تماس، زبان، ترجیحات، وضعیت حساب، Link به نقش‌های حرفه‌ای.
- **Entities**: User (profile)، ContactPoint.
- **Deps**: Identity.
- **APIs**: `IUserDirectory.Get(id)`, `/me`.

## 3. Patients
- **Purpose**: پروفایل بالینی-پایه‌ی بیمار.
- **Resp.**: دموگرافیک، آلرژی، شرایط، بارداری/شیردهی، ارتباط بیمار-درمانگر، Caregiver (آینده).
- **Entities**: Patient، PatientAllergy، PatientCondition، CareRelationship (UNKNOWN نهایی).
- **Deps**: Users، Consent، Audit.
- **APIs**: `IPatientReader.GetClinicalProfile(patientId, purpose)` (Consent-gated)، `/patients/{id}`.

## 4. Physicians
- **Purpose**: هویت حرفه‌ای پزشک.
- **Resp.**: تخصص، شماره مجوز (اعتبارسنجی **UNKNOWN**)، وضعیت تأیید، فهرست بیماران دارای دسترسی.
- **Entities**: Physician، ProfessionalCredential.
- **Deps**: Users، Identity.
- **APIs**: `IPhysicianDirectory`, `/physicians/*`.

## 5. Pharmacists
- **Purpose**: هویت حرفه‌ای داروساز.
- **Resp.**: مجوز، عضویت در داروخانه‌ها، وضعیت تأیید.
- **Entities**: Pharmacist، PharmacistMembership.
- **Deps**: Users، Pharmacies.
- **APIs**: `IPharmacistDirectory`, `/pharmacists/*`.

## 6. Pharmacies
- **Purpose**: سازمان داروخانه (Tenant).
- **Resp.**: اطلاعات سازمانی، اعضا، پیکربندی، مرز دسترسی سازمانی.
- **Entities**: Pharmacy، PharmacyMember.
- **Deps**: Users، Identity.
- **APIs**: `IPharmacyDirectory`, `/pharmacies/*`.

## 7. Medications
- **Purpose**: کاتالوگ محصول دارویی.
- **Resp.**: نام تجاری/ژنریک، فرم، قدرت، بسته‌بندی، سازنده، نگاشت به ماده مؤثره، وضعیت، کدهای شناسایی (**UNKNOWN** سیستم کدگذاری، D-16).
- **Entities**: Medication، Manufacturer، MedicationIngredient (join)، MedicationCode.
- **Deps**: ActiveIngredients، KnowledgeBase (ارجاع).
- **APIs**: `IMedicationCatalog.Search/Get`, `/medications`.

## 8. ActiveIngredients
- **Purpose**: مرجع مواد مؤثره (پایهٔ تداخل‌ها).
- **Resp.**: نام‌ها/مترادف‌ها، کلاس دارویی، کدها (UNKNOWN).
- **Entities**: ActiveIngredient، IngredientSynonym، DrugClass.
- **Deps**: —
- **APIs**: `IIngredientCatalog`, `/active-ingredients`.

## 9. Prescriptions
- **Purpose**: نسخه و آیتم‌ها؛ حقیقت واحد «چه چیزی تجویز شد».
- **Resp.**: چرخهٔ عمر، امضا/تأیید، ایمنی-چک (فراخوانی ClinicalRules)، انتشار رویداد، منبع نسخه (پزشک/خودگزارشی/Provider).
- **Entities**: Prescription، PrescriptionItem، PrescriptionStatusHistory، Attachment.
- **Deps**: Patients، Physicians، Pharmacists/Pharmacies، Medications، ClinicalRules، Consent، Audit.
- **APIs**: `IPrescriptionReader`, `/prescriptions`.

## 10. MedicationSchedule
- **Purpose**: تبدیل آیتم نسخه به برنامهٔ زمانی.
- **Resp.**: تولید occurrenceها، منطقه زمانی، ویرایش، تعلیق، اتمام.
- **Entities**: MedicationSchedule، ScheduleRule، ScheduledDose.
- **Deps**: Prescriptions، Patients.
- **APIs**: `IScheduleReader.GetDoses(patientId, range)`, `/schedules`.

## 11. MedicationIntake
- **Purpose**: ثبت مصرف واقعی.
- **Resp.**: Taken/Skipped/Late/Missed، منبع ثبت (کاربر/خودکار)، Undo، آفلاین-sync idempotent.
- **Entities**: MedicationIntake.
- **Deps**: MedicationSchedule.
- **APIs**: `/intakes`, رویداد `IntakeRecorded`.

## 12. Adherence
- **Purpose**: محاسبهٔ پایبندی.
- **Resp.**: شاخص‌ها، روند، آستانه‌ها، تولید هشدار (با Consent).
- **Entities**: AdherenceSnapshot (مشتق‌شده).
- **Deps**: MedicationIntake، MedicationSchedule.
- **APIs**: `IAdherenceReader`, `/patients/{id}/adherence`.

## 13. Symptoms
- **Purpose**: ثبت علائم بیمار.
- **Resp.**: علامت، شدت، زمان، ارتباط زمانی با دارو، Red-flag hook.
- **Entities**: Symptom، SymptomEntry.
- **Deps**: Patients، ClinicalRules (Red-flag).
- **APIs**: `/symptoms`, رویداد `SymptomRecorded`.

## 14. ADR
- **Purpose**: عارضهٔ ناخواستهٔ دارو.
- **Resp.**: ثبت، ارزیابی علیت (روش **UNKNOWN**، D-11)، گردش‌کار بازبینی داروساز/پزشک، خروجی گزارش.
- **Entities**: AdverseEvent، AdverseEventReview، AdverseEventDrug.
- **Deps**: Symptoms، Medications، Prescriptions، Consent.
- **APIs**: `/adverse-events`.

## 15. AI
- **Purpose**: مرز Core با AI Service؛ حاکمیت بر گفتگو و Toolها.
- **Resp.**: Conversation/Message، Gatekeeping (AuthZ/Consent/Rate/Guard)، Tool Gateway (Toolهای مجاز)، ثبت AIResponse+منابع، بازخورد، Kill-switch.
- **Entities**: Conversation، Message، AIResponse، AIToolCall، AIFeedback.
- **Deps**: Consent، Audit، KnowledgeBase، ClinicalRules، Patients (Tool)، Identity.
- **APIs**: `/ai/conversations`, `IAiToolGateway` (داخلی برای AI Service).

## 16. KnowledgeBase
- **Purpose**: مدیریت دانش دارویی نسخه‌دار و منبع‌دار.
- **Resp.**: منبع، سند، نسخه، Ingestion، بازبینی/انتشار (دو نفره)، فهرست‌سازی (OpenSearch)، Rollback.
- **Entities**: KnowledgeSource، KnowledgeDocument، KnowledgeVersion، KnowledgeChunk (در ایندکس).
- **Deps**: Storage، Medications، Integrations (DrugDatabaseProvider).
- **APIs**: `IKnowledgeReader.Retrieve`, `/knowledge/*`.

## 17. ClinicalRules
- **Purpose**: موتور ایمنی **قطعی**.
- **Resp.**: تداخل، آلرژی، تکرار درمان، منع مصرف، Red-flag، (دوز: پس از تأیید منبع)، نسخه‌بندی قواعد، توضیح‌پذیری، تست‌های طلایی.
- **Entities**: ClinicalRule، RuleVersion، DrugInteraction، SafetyAlert، AlertOverride.
- **Deps**: ActiveIngredients، Medications، Patients (Consent/purpose)، KnowledgeBase (منبع).
- **APIs**: `IClinicalSafetyService.Evaluate(context) → SafetyResult`, `/safety/check`.

## 18. Notifications
- **Purpose**: تحویل پیام.
- **Resp.**: کانال‌ها (Push/Email/SMS؟)، قالب، ترجیحات، Quiet hours، Retry، محتوای بدون PHI در Push.
- **Entities**: Notification، NotificationTemplate، NotificationPreference، Device.
- **Deps**: Identity، Users.
- **APIs**: `INotificationSender.Send`, `/notifications`, `/devices`.

## 19. Consent
- **Purpose**: مدیریت رضایت و مجوز دسترسی.
- **Resp.**: تعریف Purpose، ثبت/ابطال، AccessGrant (کی/به کی/چه دادهٔ/چه مدت)، ارزیابی.
- **Entities**: Consent، ConsentPurpose، ConsentVersion، AccessGrant.
- **Deps**: Identity، Audit.
- **APIs**: `IConsentEvaluator.IsAllowed(subject, patient, dataCategory, purpose)`, `/consents`, `/access-grants`.

## 20. Audit
- **Purpose**: ردپای غیرقابل‌انکار.
- **Resp.**: ثبت append-only، hash-chain، جستجوی محدود، Export برای بازرسی، دسترسی‌ها به خود Audit نیز ثبت.
- **Entities**: AuditLog.
- **Deps**: —(همه به آن وابسته‌اند؛ فقط `IAuditWriter`).
- **APIs**: `IAuditWriter.Write`, `/audit-logs` (Auditor).

## 21. Analytics
- **Purpose**: گزارش تجمیعی/de-identified.
- **Resp.**: Pipeline از رویدادها، k-anonymity، Differential-privacy (اختیاری، UNKNOWN)، داشبورد صنعت.
- **Entities**: AnalyticsDataset (Read model)، AggregationJob، ExportRequest.
- **Deps**: Consent، Events.
- **APIs**: `/analytics/*` (فقط تجمیعی).

## 22. Integrations
- **Purpose**: پل به سامانه‌های خارجی.
- **Resp.**: Interface/Adapter، Credential vault، Retry/DLQ، Mapping، Rate-limit خروجی، Webhook ورودی امضاشده.
- **Entities**: Integration، IntegrationEvent، IntegrationCredentialRef، ExternalIdentifier.
- **Deps**: Audit، Consent.
- **APIs**: `/integrations/*` (ادمین)، `/webhooks/{provider}`.

## ماتریس وابستگی (خلاصه)
Prescriptions → {Patients, Physicians, Medications, ClinicalRules, Consent, Audit}؛
ClinicalRules → {ActiveIngredients, Medications, KnowledgeBase}؛
AI → {KnowledgeBase, ClinicalRules, Consent, Audit, Patients(tool)}؛
Analytics → رویدادها (نه API مستقیم PHI). Audit و Consent و Identity به هیچ ماژول دامنه‌ای وابسته نیستند.
