# 09 — API Architecture

فقط تعریف؛ بدون پیاده‌سازی. جزئیات Schema (OpenAPI) در Phase مربوط تولید می‌شود.

## 1. اصول
- REST/JSON، پایه `/api/v1`، OpenAPI 3.x به‌عنوان قرارداد (Contract-first برای Endpointهای خارجی/Mobile).
- Auth: `Authorization: Bearer <JWT کوتاه‌مدت>`؛ Web می‌تواند Cookie HttpOnly (تصمیم در Phase Identity).
- خطا: `application/problem+json` (RFC 9457) با `correlationId`؛ بدون افشای جزئیات داخلی.
- Pagination: cursor-based؛ Filtering/Sorting محدود allowlist.
- Idempotency: `Idempotency-Key` برای POST حساس (نسخه، مصرف، AI message).
- Concurrency: `ETag/If-Match` برای PUT/PATCH.
- Rate-limit: هدرهای `RateLimit-*`.
- نسخه‌بندی: Path؛ تغییر Breaking = نسخهٔ جدید؛ Deprecation 6 ماه (پیشنهاد).
- هر Endpoint: Permission + ABAC + Audit tag در تعریف (لازم برای Test خودکار).
- Streaming AI: SSE (`text/event-stream`) — (جایگزین WebSocket، تصمیم فاز AI).
- Webhook ورودی: امضا HMAC + timestamp.
- Mobile: پشتیبانی Offline-sync با `client_op_id` و `since` cursor.
- Internal AI↔Core: مسیر جدا `/internal/ai/*`، mTLS + توکن on-behalf-of؛ **در دسترس عموم نیست**.
- Gateway: Reverse proxy/WAF (D-21).

## 2. Endpointهای مهم
علامت: 🔒 نیازمند Consent/Grant · 🩺 نقش حرفه‌ای · 👤 بیمار · 🛡 Admin · (M)=MVP

### Identity
| Method | Path | توضیح |
|---|---|---|
| POST | `/auth/register` (M) | ثبت‌نام |
| POST | `/auth/login` (M) | ورود → challenge MFA |
| POST | `/auth/mfa/verify` (M) | |
| POST | `/auth/token/refresh` (M) | rotation |
| POST | `/auth/logout` (M) | revoke |
| POST/DELETE | `/auth/mfa/factors` (M) | |
| GET/DELETE | `/me/sessions`, `/me/devices` | |
| GET | `/me` (M) | |
| POST/GET/PUT | `/admin/roles`, `/admin/users/{id}/roles` 🛡 | |

### Profiles
| GET/PUT | `/patients/{id}` 🔒(M) | |
| GET/POST/DELETE | `/patients/{id}/allergies` 🔒(M) | |
| GET/PUT | `/physicians/{id}`, `/pharmacists/{id}` (M) | |
| POST | `/physicians/{id}/verification` 🛡 | فرایند UNKNOWN |
| GET/POST | `/pharmacies`, `/pharmacies/{id}/members` (M) | |

### Medications / Knowledge
| GET | `/medications?q=` (M) | جستجوی OpenSearch |
| GET | `/medications/{id}` (M) | |
| GET | `/active-ingredients?q=` (M) | |
| GET | `/medications/{id}/info` (M) | از KB با ارجاع |
| POST | `/knowledge/sources` 🛡 | |
| POST | `/knowledge/documents` (upload) 🛡 | |
| POST | `/knowledge/versions/{id}/submit`, `/approve`, `/retire` 🛡 | دو نفره |

### Prescriptions
| POST | `/prescriptions` 🩺(M) | شامل Safety check |
| GET | `/prescriptions/{id}` 🔒(M) | |
| GET | `/patients/{id}/prescriptions` 🔒(M) | |
| POST | `/prescriptions/{id}/issue` 🩺(M) | |
| POST | `/prescriptions/{id}/cancel` (M) | |
| POST | `/patients/{id}/prescriptions/self-reported` 👤(M) | |
| POST | `/prescriptions/{id}/dispense` 🩺(pharmacist) | |
| POST | `/prescriptions/{id}/attachments` | Malware scan |

### Schedule / Intake / Adherence
| GET | `/patients/{id}/schedule?from&to` 🔒(M) | |
| POST | `/prescription-items/{id}/schedule` (M) | |
| PATCH | `/schedules/{id}` (M) | |
| POST | `/intakes` 👤(M) | idempotent |
| POST | `/intakes/sync` 👤(M) | batch آفلاین |
| DELETE | `/intakes/{id}` (undo) | |
| GET | `/patients/{id}/adherence` 🔒(M) | |

### Symptoms / ADR
| POST/GET | `/patients/{id}/symptoms` (M) | Red-flag hook |
| POST/GET | `/adverse-events` (M) | |
| POST | `/adverse-events/{id}/review` 🩺 | |
| POST | `/adverse-events/{id}/report` 🩺 | خروجی نهاد ناظر: UNKNOWN |

### Clinical Safety
| POST | `/safety/check` 🩺/AI-internal (M) | ورودی: بیمار، آیتم‌ها → SafetyResult (شدت، دلیل، منبع) |
| POST | `/safety/alerts/{id}/override` 🩺 | دلیل اجباری |
| GET/POST | `/clinical-rules`, `/clinical-rules/{id}/versions` 🛡 | دو نفره |
| GET | `/drug-interactions?ingredientIds=` (M) | |

### AI
| POST | `/ai/conversations` (M) | purpose، subject |
| POST | `/ai/conversations/{id}/messages` (M) | SSE |
| GET | `/ai/conversations/{id}` | |
| DELETE | `/ai/conversations/{id}` | حذف |
| POST | `/ai/responses/{id}/feedback` | |
| POST | `/internal/ai/tools/{toolName}` | فقط AI Service |
| POST | `/admin/ai/kill-switch` 🛡 | |

### Consent / Access
| GET/POST | `/consents` (M) | |
| POST | `/consents/{id}/revoke` (M) | |
| GET/POST | `/access-grants` (M) | |
| POST | `/access-grants/{id}/revoke` (M) | |
| GET | `/me/access-log` (M) | «چه کسی داده مرا دید» |
| POST | `/break-glass` 🩺 | D-28 |

### Notifications / Devices
| POST/DELETE | `/devices` (M) | |
| GET/PUT | `/notification-preferences` (M) | |
| GET | `/notifications` (M) | |

### Audit / Analytics / Integrations
| GET | `/audit-logs` (Auditor) | |
| GET | `/analytics/adherence/summary` 🛡/Industry | فقط تجمیعی |
| POST | `/analytics/exports` | Consent + k-anon |
| GET/POST | `/integrations` 🛡 | |
| POST | `/webhooks/{providerKey}` | امضا |

### Data rights
| POST | `/me/data-export` | |
| POST | `/me/delete-request` | |

## 3. Client Matrix
| Client | استفاده اصلی |
|---|---|
| Flutter (Patient) | Auth, Schedule, Intake, Symptoms, AI, Consent, Notifications |
| React (Physician/Pharmacist) | Patients(با Grant)، Prescriptions، Safety، ADR |
| React (Admin/Editor) | Knowledge، Rules، Users، Integrations، Audit |
| React (Industry) | Analytics تجمیعی |
| Extension | فقط `POST /shares` (ارسال متن/تصویر/لینک به صندوق کاربر) — تعریف در Phase مربوط (D-13) |

## 4. UNKNOWN
قالب دقیق کدهای دارو/علامت، ساختار Dose ساخت‌یافته، قالب گزارش رسمی ADR، Auth Web (Cookie vs Bearer)، مدیریت زبان محتوا (fa/en).
