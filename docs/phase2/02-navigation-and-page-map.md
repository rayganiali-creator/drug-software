# 02 — Navigation & Page Map

## 1. تحلیل Journey قبل از نهایی‌کردن Navigation
کارهای بیمار (فرضیه‌های محصول؛ با تحقیق کاربر تأیید نشده‌اند — D-02 در Phase 0):

| # | Job-to-be-done | تکرار | زمان‌حساس؟ | کجا پاسخ داده می‌شود |
|---|---|---|---|---|
| 1 | «دوز بعدی من کِی است؟ مصرف کردم» | چند بار/روز | بله | **Home** (کارت دوز بعدی + اقدام یک‌لمسی) |
| 2 | «داروهایم چیست؟ چطور مصرف کنم؟» | روزانه/هفتگی | خیر | **Medications** |
| 3 | «این علامت/تداخل نگران‌کننده است؟» | گاه‌به‌گاه، اما ارزش بالا | بله (اضطراب) | **AI Assistant** (نقطهٔ تمایز محصول) |
| 4 | «حال امروزم را ثبت کنم» | روزانه | نیمه | **Check-in** |
| 5 | «تنظیمات، حریم خصوصی، زبان، تم» | نادر | خیر | **Profile** |
| 6 | هشدار مهم (اتمام دارو، بررسی داروساز) | رویدادی | بله | Banner روی Home + Badge (نه تب جدا) |

نتیجه: پنج مقصد سطح‌بالا، مطابق مثال‌ها اما با دو تصمیم آگاهانه:
1. **AI Assistant در مرکز** و بصری‌شده به‌عنوان اقدام اصلی (رنگ Iris) — چون تمایز محصول است و باید با یک لمس در دسترس باشد.
2. **Alerts تب جدا نیست**: هشدار بالای Home و Badge روی آیکون Home؛ فهرست کامل داخل Home → «همهٔ هشدارها» (Bottom sheet/صفحه). دلیل: کمتر از ۵ هشدار فعال در هر لحظه؛ تب جدا Navigation را شلوغ می‌کند.

## 2. Mobile (Flutter): Patient App
```
NavigationBar (5)  [RTL: ترتیب بصری از راست؛ ترتیب منطقی ثابت]
 ├─ Home            /home
 ├─ Medications     /medications  → /medications/:id → (Schedule, Info, Warnings)
 ├─ AI Assistant    /assistant    → /assistant/history · Evidence sheet · Escalation sheet
 ├─ Check-in        /checkin      → /checkin/done
 └─ Profile         /profile      → /profile/settings · /profile/design-system (dev)
Push routes (بدون Bottom bar): medication detail, chat history, all alerts, design gallery
```
Compact ⇒ Bottom NavigationBar · Medium/Expanded ⇒ NavigationRail. Back gesture بومی (iOS swipe / Android predictive back). Deep-link آماده (go_router).

## 3. Web
```
Public (بدون Shell)     /            Landing
                        /design-system
App Shell               Sidebar (Collapsible) + Topbar + Main
Expanded ≥1024   Sidebar 264px، قابل جمع‌شدن به Rail 76px (وضعیت ذخیره)
Medium 600–1023  Rail ثابت (آیکون + Tooltip)؛ باز شدن موقت به Drawer روی Overlay
Compact <600     Patient: Bottom Navigation (همان ۵ مقصد موبایل) · سایر نقش‌ها: Drawer با Topbar (Hamburger)
Topbar           Breadcrumb/عنوان · جستجوی سراسری (Mock) · Role switcher (Demo) · Theme · Language · Notifications
Global           DemoBanner ثابت: «DEMO DATA — NOT FOR CLINICAL USE»
```
Role switcher فقط برای Demo است (Auth در فاز بعد)؛ انتخاب نقش ⇒ Navigation و Information Architecture کاملاً متفاوت.

## 4. نقشهٔ صفحات
| Role | مسیر | صفحه | Web | Mobile |
|---|---|---|---|---|
| Public | `/` | Landing (Hero, Problem, Solution, How, Patient, Pharmacist, Physician, Pharmacy, AI, Safety, Privacy, Future integrations, CTA) | ✓ | – |
| Public | `/design-system` | Gallery Tokens/Components | ✓ | ✓ (Profile) |
| Patient | `/app/patient` | Home | ✓ | ✓ |
| | `/app/patient/medications` | Medications list | ✓ | ✓ |
| | `/app/patient/medications/:id` | Medication detail | ✓ | ✓ |
| | `/app/patient/assistant` | AI Assistant (+ History) | ✓ | ✓ |
| | `/app/patient/checkin` | Daily Check-in | ✓ | ✓ |
| | `/app/patient/profile` | Profile & Settings | ✓ | ✓ |
| Physician | `/app/physician` | Dashboard (اولویت‌ها، ریسک، ADR جدید) | ✓ | – |
| | `/app/physician/patients` | Patients | ✓ | – |
| | `/app/physician/patients/:id` | Patient Overview: Overview · Medications · Prescriptions · Adherence · Symptoms · ADR · AI Summary | ✓ | – |
| | `/app/physician/reports` | Reports | ✓ | – |
| Pharmacist | `/app/pharmacist` | Dashboard / Work queue | ✓ | – |
| | `/app/pharmacist/reviews` | Medication Review (+ Prescription, Interactions) | ✓ | – |
| | `/app/pharmacist/adr` | ADR | ✓ | – |
| | `/app/pharmacist/adherence` | Adherence | ✓ | – |
| | `/app/pharmacist/questions` | Patient Questions | ✓ | – |
| | `/app/pharmacist/followups` | Follow-up | ✓ | – |
| Pharmacy | `/app/pharmacy` | Overview | ✓ | – |
| | `/inventory` `/prescriptions` `/dispensing` `/requests` `/alerts` | Inventory · Prescriptions · Dispensing · Patient Requests · Alerts | ✓ | – |
| Industry | `/app/industry` | Analytics overview | ✓ | – |
| | `/adr-trends` `/experience` `/signals` `/reports` | ADR Trends · Patient Experience · Signals · Reports (فقط تجمیعی) | ✓ | – |

نقش‌های حرفه‌ای در Mobile: **خارج از Phase 2** (پیشنهاد Phase 0 = Web-only، D-12). Web آن‌ها روی Tablet/Phone هم قابل استفاده است (Responsive) اما «Mobile-optimized» نیست.
