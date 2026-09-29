# 08 — Integration Architecture (فقط Interface)

**قاعدهٔ سخت:** هیچ API خارجی واقعی حدس زده نشده است. برای هر Provider واقعی: نام، پروتکل، احراز هویت، قالب داده، SLA، مجوز = **UNKNOWN** تا مستندات رسمی و قرارداد در دست باشد (D-07).
Interfaceها **Capability-based و Provider-neutral** هستند؛ مدل داخلی (Canonical Model) از مدل خارجی جداست.

## 1. الگو
```
Module (Prescriptions, Medications, ...) ──> Port (Interface در *.Contracts یا Integrations.Contracts)
                                              │
                          Integrations module: Registry ── Adapter(s) ── (Provider واقعی: UNKNOWN)
                                              │
              Anti-Corruption Layer (Mapper) · Resilience (Retry/CircuitBreaker/Timeout) · Idempotency
              Credentials (ارجاع به Secret manager) · Audit + IntegrationEvent · Consent check
```
- **Adapter** = تنها جایی که فرمت خارجی را می‌شناسد.
- **Fake/Sandbox Adapter** برای Dev/Test در هر Port (پیاده‌سازی در فازهای بعد).
- **Capability Descriptor**: هر Adapter اعلام می‌کند چه قابلیتی دارد (مثلاً `SupportsWebhook`, `SupportsBatch`)؛ منطق شرطی در Core نیست.
- خطاها: `Transient | Permanent | AuthFailure | RateLimited | Unavailable | ValidationFailed` (Taxonomy واحد).
- Outbound فقط از Egress ایزوله (Proxy allowlist)؛ Inbound Webhook: امضا + Replay protection.
- هر فراخوانی ⇒ Consent/Purpose check + IntegrationEvent + Audit؛ Payload خام رمزنگاری‌شده با Retention کوتاه.
- هر Integration **per-tenant** قابل فعال/غیرفعال (Feature flag + Consent برای بیمار).

## 2. Ports (پیش‌نویس Interface — امضاها مفهومی‌اند، نه کد)
### PrescriptionProvider
| عملیات | ورودی → خروجی مفهومی |
|---|---|
| `FetchPrescriptions` | PatientExternalRef, Since → `CanonicalPrescription[]` |
| `SubmitPrescription` (آینده) | CanonicalPrescription → SubmissionResult |
| `GetStatus` | ExternalRef → PrescriptionStatus |
| `SubscribeUpdates` (اختیاری) | Webhook registration |
Adapters: `<Provider>PrescriptionAdapter` — **UNKNOWN** (کدام سامانه؟ D-07). `FakePrescriptionAdapter` برای تست.

### PharmacyProvider
| `FindPharmacies` | Location/Criteria → Pharmacy[] |
| `SendPrescriptionToPharmacy` | Rx + Pharmacy → Ack |
| `GetDispenseStatus` | Ref → DispenseStatus |
| `CheckAvailability` (اختیاری) | Medication + Pharmacy → Availability |
Adapters: UNKNOWN.

### InsuranceProvider
| `CheckCoverage` | PatientRef + Medication → CoverageResult |
| `GetPriorAuthorizationStatus` | Ref → Status |
Adapters: UNKNOWN. [نیاز به تأیید اینکه اصلاً در محدوده باشد: D-29]

### DrugDatabaseProvider
| `SearchDrugs` / `GetDrug` | Query → CanonicalDrug |
| `GetInteractions` | Ingredients → `InteractionRecord[]` (با منبع، نسخه، مجوز) |
| `GetMonograph` | DrugRef → Document (برای KnowledgeBase Ingestion) |
| `GetVersion` | → SourceVersion |
Adapters: UNKNOWN — **مسیر بحرانی**: منبع دانش دارویی و مجوز استفاده (D-16). داده Provider هرگز مستقیم به بیمار نمایش داده نمی‌شود؛ ابتدا وارد KnowledgeBase → بازبینی → انتشار.

### PatientIdentityProvider
| `VerifyIdentity` | Claims → VerificationResult (LevelOfAssurance) |
| `ResolvePatient` | Identifiers → ExternalPatientRef |
| `LinkIdentity` | User + ExternalRef → Link (با Consent) |
Adapters: UNKNOWN. حداقل‌سازی: فقط Claim لازم؛ ذخیرهٔ شناسه‌ها با رمزنگاری.

### سایر Portهای پیشنهادی (آینده)
`NotificationChannelProvider` (Push/Email/SMS — Provider UNKNOWN)، `LlmProvider`/`EmbeddingProvider` (در AI Service)،
`ObjectStorageProvider`، `FileScanProvider`، `PractitionerLicenseVerifier` (D-04)، `RegulatorReportingProvider` (D-11).

## 3. مدل Canonical (خلاصه)
`CanonicalPrescription`, `CanonicalDrug`, `InteractionRecord`, `ExternalPatientRef`, `DispenseStatus`, `CoverageResult`.
نقشهٔ فیلد به‌فیلد فقط پس از دریافت مستند رسمی Provider تدوین می‌شود.

## 4. چرخه پذیرش Provider جدید (Checklist)
مستند رسمی + قرارداد → Threat model/DPIA → Adapter + Contract tests با Sandbox → Fake → Feature flag → Pilot با یک Tenant → مانیتورینگ/SLA → Runbook.

## 5. ریسک‌ها
وابستگی به Provider، تغییر بی‌اطلاع API، کیفیت/مجوز داده دارویی، دوباره‌کاری هویت بیمار، نشت PHI به Provider (Purpose/Consent/Minimization).
