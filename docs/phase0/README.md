# AI MedSmarter — Phase 0: System Analysis & Architecture

> وضعیت: **پیش‌نویس برای بازبینی** — هیچ کد Production در این فاز تولید نشده است.
> تاریخ: 2026-09-29

## فهرست اسناد
| # | سند | محتوا |
|---|-----|-------|
| 01 | [PRD](01-prd.md) | چشم‌انداز، کاربران، نیازمندی‌ها، MVP |
| 02 | [System Architecture](02-architecture.md) | معماری، Stack، Modular Monolith، مرز میکروسرویس |
| 03 | [Modules](03-modules.md) | ۲۲ Bounded Module |
| 04 | [Database](04-database.md) | معماری دیتابیس + ERD (بدون SQL) |
| 05 | [AI Architecture](05-ai-architecture.md) | LLM / RAG / Rule Engine / Safety |
| 06 | [Security & Threat Model](06-security-threat-model.md) | STRIDE-محور |
| 07 | [Privacy by Design](07-privacy.md) | ماتریس داده |
| 08 | [Integration](08-integration.md) | فقط Interface + Adapter |
| 09 | [API](09-api.md) | Endpointهای مهم |
| 10 | [Roadmap](10-roadmap.md) | Phaseهای کوچک |
| 11 | [Decisions](11-decisions.md) | **DECISIONS I MUST MAKE** + UNKNOWNها |

## قراردادهای علامت‌گذاری
- `UNKNOWN` — اطلاعات کافی نداریم؛ حدس زده نشده است.
- `[DECISION REQUIRED]` — نیاز به تأیید صریح مالک محصول دارد. شمارهٔ `D-xx` به سند ۱۱ ارجاع می‌دهد.
- `(پیشنهاد)` — توصیهٔ معماری که هنوز تأیید نشده است.

## یادداشت دربارهٔ مخزن
مخزن `drug-software` تنها یک README خالی دارد؛ مخزن `planner11` یک اپ Capacitor نامرتبط (روتین پلنر) است و
در این طراحی هیچ وابستگی‌ای به آن فرض نشده است (**UNKNOWN**: آیا قرار است بخشی از آن دوباره استفاده شود؟ → D-24).

## هشدار
این سند مشاورهٔ حقوقی یا پزشکی نیست. همهٔ ادعاهای مقرراتی (مجوز نرم‌افزار پزشکی، حریم خصوصی داده سلامت)
به دلیل ناشناخته‌بودن حوزهٔ قضایی، `UNKNOWN` هستند (D-01).
