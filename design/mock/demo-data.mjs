// Shared DEMO DATA for web and Flutter. EVERYTHING here is fictional.
// No real drug, dose, interaction, warning, person or statistic. NOT FOR CLINICAL USE.
// Times are "HH:mm" local; ages/dates are relative offsets so the demo never looks stale.
const L = (en, fa) => ({ en, fa });

const activeIngredients = [
  { id: "ai-demoprilate", name: L("demoprilate (fictional)", "دموپریلات (فرضی)") },
  { id: "ai-glycanorin", name: L("glycanorin (fictional)", "گلیکانورین (فرضی)") },
  { id: "ai-respirexol", name: L("respirexol (fictional)", "رسپیرکسول (فرضی)") },
  { id: "ai-nocturamide", name: L("nocturamide (fictional)", "نوکتورامید (فرضی)") },
  { id: "ai-solvitol", name: L("solvitol (fictional)", "سولویتول (فرضی)") },
];

const drugs = [
  {
    id: "drug-demopril",
    name: L("Demopril", "دموپریل"),
    activeIngredientId: "ai-demoprilate",
    strength: "10 mg",
    form: L("Tablet", "قرص"),
    route: L("Oral", "خوراکی"),
    category: L("Demo heart-health class", "گروه نمایشی سلامت قلب"),
    instructions: L("Take one tablet each morning with a glass of water.", "هر صبح یک قرص همراه یک لیوان آب مصرف شود."),
    warnings: [
      L("Fictional warning: may cause demo dizziness when standing up quickly.", "هشدار فرضی: ممکن است هنگام برخاستن ناگهانی سرگیجهٔ نمایشی ایجاد کند."),
    ],
  },
  {
    id: "drug-glycanor",
    name: L("Glycanor", "گلیکانور"),
    activeIngredientId: "ai-glycanorin",
    strength: "500 mg",
    form: L("Tablet", "قرص"),
    route: L("Oral", "خوراکی"),
    category: L("Demo metabolic class", "گروه نمایشی متابولیک"),
    instructions: L("Take one tablet with breakfast and one with dinner.", "یک قرص همراه صبحانه و یک قرص همراه شام مصرف شود."),
    warnings: [
      L("Fictional warning: take with food to reduce demo stomach upset.", "هشدار فرضی: همراه غذا مصرف شود تا ناراحتی نمایشی معده کمتر شود."),
    ],
  },
  {
    id: "drug-respirex",
    name: L("Respirex", "رسپیرکس"),
    activeIngredientId: "ai-respirexol",
    strength: "100 mcg / puff",
    form: L("Inhaler", "اسپری استنشاقی"),
    route: L("Inhalation", "استنشاقی"),
    category: L("Demo breathing-support class", "گروه نمایشی حمایت تنفسی"),
    instructions: L("Two puffs in the morning. Rinse your mouth afterwards.", "صبح دو پاف مصرف شود. پس از مصرف دهان شسته شود."),
    warnings: [L("Fictional warning: do not exceed the demo daily puffs.", "هشدار فرضی: از تعداد پاف نمایشی روزانه بیشتر نشود.")],
  },
  {
    id: "drug-nocturin",
    name: L("Nocturin", "نوکتورین"),
    activeIngredientId: "ai-nocturamide",
    strength: "5 mg",
    form: L("Tablet", "قرص"),
    route: L("Oral", "خوراکی"),
    category: L("Demo sleep-support class", "گروه نمایشی حمایت خواب"),
    instructions: L("Take one tablet at bedtime.", "یک قرص هنگام خواب مصرف شود."),
    warnings: [
      L("Fictional warning: may cause demo drowsiness. Avoid driving after taking.", "هشدار فرضی: ممکن است خواب‌آلودگی نمایشی ایجاد کند. پس از مصرف رانندگی نکنید."),
    ],
  },
  {
    id: "drug-solvita",
    name: L("Solvita", "سولویتا"),
    activeIngredientId: "ai-solvitol",
    strength: "1000 IU",
    form: L("Capsule", "کپسول"),
    route: L("Oral", "خوراکی"),
    category: L("Demo supplement class", "گروه نمایشی مکمل"),
    instructions: L("Take one capsule with lunch.", "یک کپسول همراه ناهار مصرف شود."),
    warnings: [L("Fictional warning: demo supplement, keep out of reach of children.", "هشدار فرضی: مکمل نمایشی؛ دور از دسترس کودکان نگه دارید.")],
  },
];

const knowledgeSources = [
  { id: "ks-1", title: L("Demo Formulary Monograph: Demopril", "تک‌نگار نمایشی فرمولری: دموپریل"), type: L("Monograph", "تک‌نگار"), version: "demo-0.1" },
  { id: "ks-2", title: L("Demo Patient Leaflet: Glycanor", "برگهٔ نمایشی بیمار: گلیکانور"), type: L("Patient leaflet", "برگهٔ بیمار"), version: "demo-0.1" },
  { id: "ks-3", title: L("Demo Guide: Missed Doses", "راهنمای نمایشی: دوز فراموش‌شده"), type: L("Guideline", "راهنما"), version: "demo-0.2" },
  { id: "ks-4", title: L("Demo Interaction Table (fictional)", "جدول نمایشی تداخل (فرضی)"), type: L("Interaction table", "جدول تداخل"), version: "demo-0.1" },
];

const interactions = [
  {
    id: "ix-1",
    drugAId: "drug-demopril",
    drugBId: "drug-nocturin",
    severity: "moderate",
    summary: L("Fictional: taken together, dizziness may feel stronger.", "فرضی: مصرف هم‌زمان ممکن است سرگیجه را بیشتر کند."),
    management: L("Pharmacist review suggested. Do not change doses on your own.", "بازبینی توسط داروساز توصیه می‌شود. دوزها را خودسرانه تغییر ندهید."),
    sourceId: "ks-4",
  },
  {
    id: "ix-2",
    drugAId: "drug-glycanor",
    drugBId: "drug-solvita",
    severity: "minor",
    summary: L("Fictional: minor timing note, no action needed.", "فرضی: نکتهٔ جزئی دربارهٔ زمان مصرف؛ اقدامی لازم نیست."),
    management: L("Informational only.", "صرفاً اطلاعاتی."),
    sourceId: "ks-4",
  },
];

const patients = [
  {
    id: "pt-sara", name: L("Sara Ahmadi", "سارا احمدی"), age: 42, sex: "F", risk: "medium", adherence: 86, lastCheckInDaysAgo: 0,
    conditions: [L("Demo condition A", "بیماری نمایشی الف"), L("Demo condition B", "بیماری نمایشی ب")], allergies: [L("Fictional allergen X", "حساسیت فرضی X")],
    drugIds: ["drug-demopril", "drug-glycanor", "drug-nocturin", "drug-solvita"],
    adherenceSeries: [92, 88, 90, 85, 80, 84, 86, 91, 89, 83, 78, 82, 88, 86],
    symptoms: [
      { id: "sy-1", term: L("Mild headache", "سردرد خفیف"), severity: "mild", daysAgo: 1 },
      { id: "sy-2", term: L("Dizziness in the morning", "سرگیجه صبحگاهی"), severity: "moderate", daysAgo: 3 },
    ],
  },
  { id: "pt-1", name: L("Ali Rezaei", "علی رضایی"), age: 67, sex: "M", risk: "high", adherence: 61, lastCheckInDaysAgo: 4, conditions: [L("Demo condition A", "بیماری نمایشی الف")], allergies: [], drugIds: ["drug-demopril", "drug-nocturin", "drug-glycanor"], adherenceSeries: [70, 66, 64, 60, 58, 62, 61, 57, 60, 63, 59, 55, 60, 61], symptoms: [{ id: "sy-3", term: L("Fatigue", "خستگی"), severity: "moderate", daysAgo: 2 }] },
  { id: "pt-2", name: L("Maryam Hosseini", "مریم حسینی"), age: 55, sex: "F", risk: "medium", adherence: 78, lastCheckInDaysAgo: 1, conditions: [L("Demo condition B", "بیماری نمایشی ب")], allergies: [L("Fictional allergen Y", "حساسیت فرضی Y")], drugIds: ["drug-glycanor", "drug-solvita"], adherenceSeries: [80, 78, 82, 76, 74, 79, 78, 81, 77, 75, 80, 78, 76, 78], symptoms: [] },
  { id: "pt-3", name: L("Hamid Nouri", "حمید نوری"), age: 71, sex: "M", risk: "high", adherence: 54, lastCheckInDaysAgo: 6, conditions: [L("Demo condition A", "بیماری نمایشی الف"), L("Demo condition C", "بیماری نمایشی ج")], allergies: [], drugIds: ["drug-demopril", "drug-respirex", "drug-nocturin"], adherenceSeries: [60, 58, 55, 52, 50, 56, 54, 51, 55, 53, 50, 52, 56, 54], symptoms: [{ id: "sy-4", term: L("Shortness of breath (demo)", "تنگی نفس (نمایشی)"), severity: "moderate", daysAgo: 1 }] },
  { id: "pt-4", name: L("Neda Karimi", "ندا کریمی"), age: 34, sex: "F", risk: "low", adherence: 95, lastCheckInDaysAgo: 0, conditions: [L("Demo condition B", "بیماری نمایشی ب")], allergies: [], drugIds: ["drug-solvita"], adherenceSeries: [96, 94, 97, 95, 93, 96, 95, 94, 97, 96, 95, 94, 96, 95], symptoms: [] },
  { id: "pt-5", name: L("Reza Moradi", "رضا مرادی"), age: 49, sex: "M", risk: "low", adherence: 91, lastCheckInDaysAgo: 2, conditions: [L("Demo condition A", "بیماری نمایشی الف")], allergies: [], drugIds: ["drug-demopril"], adherenceSeries: [90, 92, 91, 89, 93, 90, 91, 92, 90, 91, 93, 89, 92, 91], symptoms: [] },
  { id: "pt-6", name: L("Zahra Ebrahimi", "زهرا ابراهیمی"), age: 62, sex: "F", risk: "medium", adherence: 72, lastCheckInDaysAgo: 3, conditions: [L("Demo condition C", "بیماری نمایشی ج")], allergies: [L("Fictional allergen X", "حساسیت فرضی X")], drugIds: ["drug-respirex", "drug-glycanor"], adherenceSeries: [75, 72, 70, 74, 71, 73, 72, 70, 69, 73, 74, 71, 72, 72], symptoms: [{ id: "sy-5", term: L("Dry cough", "سرفهٔ خشک"), severity: "mild", daysAgo: 4 }] },
  { id: "pt-7", name: L("Kian Tehrani", "کیان تهرانی"), age: 29, sex: "M", risk: "low", adherence: 88, lastCheckInDaysAgo: 1, conditions: [], allergies: [], drugIds: ["drug-solvita"], adherenceSeries: [88, 90, 87, 89, 86, 88, 90, 87, 88, 89, 86, 88, 90, 88], symptoms: [] },
];

const prescriptions = [
  {
    id: "rx-1001", patientId: "pt-sara", prescriber: L("Dr. Reza Karimi (demo)", "دکتر رضا کریمی (نمایشی)"), issuedDaysAgo: 18, status: "active", refillsLeft: 2,
    items: [
      { drugId: "drug-demopril", dose: L("1 tablet", "۱ قرص"), frequency: L("Once daily", "روزی یک بار"), times: ["08:00"], durationDays: 90 },
      { drugId: "drug-glycanor", dose: L("1 tablet", "۱ قرص"), frequency: L("Twice daily with meals", "روزی دو بار همراه غذا"), times: ["08:30", "20:00"], durationDays: 90 },
      { drugId: "drug-nocturin", dose: L("1 tablet", "۱ قرص"), frequency: L("At bedtime", "هنگام خواب"), times: ["22:30"], durationDays: 30 },
    ],
  },
  {
    id: "rx-1002", patientId: "pt-sara", prescriber: L("Dr. Reza Karimi (demo)", "دکتر رضا کریمی (نمایشی)"), issuedDaysAgo: 40, status: "active", refillsLeft: 1,
    items: [{ drugId: "drug-solvita", dose: L("1 capsule", "۱ کپسول"), frequency: L("Once daily with lunch", "روزی یک بار همراه ناهار"), times: ["13:00"], durationDays: 120 }],
  },
  { id: "rx-1003", patientId: "pt-1", prescriber: L("Dr. Reza Karimi (demo)", "دکتر رضا کریمی (نمایشی)"), issuedDaysAgo: 5, status: "active", refillsLeft: 3, items: [{ drugId: "drug-demopril", dose: L("1 tablet", "۱ قرص"), frequency: L("Once daily", "روزی یک بار"), times: ["08:00"], durationDays: 60 }, { drugId: "drug-nocturin", dose: L("1 tablet", "۱ قرص"), frequency: L("At bedtime", "هنگام خواب"), times: ["22:30"], durationDays: 30 }] },
  { id: "rx-1004", patientId: "pt-3", prescriber: L("Dr. Leila Sadeghi (demo)", "دکتر لیلا صادقی (نمایشی)"), issuedDaysAgo: 2, status: "pending-review", refillsLeft: 0, items: [{ drugId: "drug-respirex", dose: L("2 puffs", "۲ پاف"), frequency: L("Morning", "صبح"), times: ["09:00"], durationDays: 30 }, { drugId: "drug-nocturin", dose: L("1 tablet", "۱ قرص"), frequency: L("At bedtime", "هنگام خواب"), times: ["22:30"], durationDays: 30 }] },
  { id: "rx-1005", patientId: "pt-2", prescriber: L("Dr. Leila Sadeghi (demo)", "دکتر لیلا صادقی (نمایشی)"), issuedDaysAgo: 1, status: "pending-review", refillsLeft: 1, items: [{ drugId: "drug-glycanor", dose: L("1 tablet", "۱ قرص"), frequency: L("Twice daily with meals", "روزی دو بار همراه غذا"), times: ["08:30", "20:00"], durationDays: 60 }, { drugId: "drug-solvita", dose: L("1 capsule", "۱ کپسول"), frequency: L("Once daily", "روزی یک بار"), times: ["13:00"], durationDays: 60 }] },
  { id: "rx-1006", patientId: "pt-6", prescriber: L("Dr. Leila Sadeghi (demo)", "دکتر لیلا صادقی (نمایشی)"), issuedDaysAgo: 9, status: "dispensed", refillsLeft: 2, items: [{ drugId: "drug-respirex", dose: L("2 puffs", "۲ پاف"), frequency: L("Morning", "صبح"), times: ["09:00"], durationDays: 30 }] },
];

const adrReports = [
  { id: "adr-1", patientId: "pt-sara", drugId: "drug-demopril", event: L("Dizziness on standing (fictional)", "سرگیجه هنگام ایستادن (فرضی)"), severity: "moderate", status: "new", reportedDaysAgo: 1, causality: "unassessed" },
  { id: "adr-2", patientId: "pt-3", drugId: "drug-nocturin", event: L("Next-day drowsiness (fictional)", "خواب‌آلودگی روز بعد (فرضی)"), severity: "moderate", status: "under-review", reportedDaysAgo: 3, causality: "possible" },
  { id: "adr-3", patientId: "pt-2", drugId: "drug-glycanor", event: L("Mild stomach upset (fictional)", "ناراحتی خفیف معده (فرضی)"), severity: "mild", status: "reviewed", reportedDaysAgo: 12, causality: "possible" },
  { id: "adr-4", patientId: "pt-1", drugId: "drug-demopril", event: L("Persistent dry cough (fictional)", "سرفهٔ خشک مداوم (فرضی)"), severity: "moderate", status: "new", reportedDaysAgo: 0, causality: "unassessed" },
];

const patientHome = {
  patientId: "pt-sara",
  alerts: [
    { id: "al-1", severity: "warning", title: L("Pharmacist is reviewing two of your medicines", "داروساز در حال بررسی دو داروی شماست"), body: L("Demopril and Nocturin were flagged for a routine check. Keep taking them as prescribed.", "دموپریل و نوکتورین برای بررسی روتین علامت‌گذاری شده‌اند. طبق نسخه مصرف را ادامه دهید."), action: "medications" },
    { id: "al-2", severity: "info", title: L("Refill due in 3 days", "تمدید دارو تا ۳ روز دیگر"), body: L("Glycanor supply is running low (demo).", "موجودی گلیکانور رو به اتمام است (نمایشی)."), action: "medications" },
  ],
  activity: [
    { id: "ac-1", kind: "dose", minutesAgo: 95, text: L("You took Demopril 10 mg", "دموپریل ۱۰ میلی‌گرم را مصرف کردید") },
    { id: "ac-2", kind: "checkin", minutesAgo: 1560, text: L("Daily check-in completed", "چک‌این روزانه ثبت شد") },
    { id: "ac-3", kind: "ai", minutesAgo: 2900, text: L("You asked the assistant about missed doses", "از دستیار دربارهٔ دوز فراموش‌شده پرسیدید") },
    { id: "ac-4", kind: "review", minutesAgo: 4300, text: L("Pharmacist reviewed your prescription", "داروساز نسخهٔ شما را بازبینی کرد") },
  ],
  adherenceSeries30: [90, 92, 88, 94, 96, 90, 84, 80, 86, 90, 92, 94, 88, 84, 82, 86, 90, 92, 94, 96, 90, 88, 84, 80, 78, 86, 90, 92, 88, 86],
  missedSeedDose: { prescriptionItemDrugId: "drug-glycanor", time: "08:30" },
};

const checkIn = {
  moods: [
    { value: 1, label: L("Very low", "خیلی بد") },
    { value: 2, label: L("Low", "بد") },
    { value: 3, label: L("Okay", "معمولی") },
    { value: 4, label: L("Good", "خوب") },
    { value: 5, label: L("Great", "عالی") },
  ],
  symptoms: [
    { id: "headache", label: L("Headache", "سردرد"), urgent: false },
    { id: "nausea", label: L("Nausea", "تهوع"), urgent: false },
    { id: "dizziness", label: L("Dizziness", "سرگیجه"), urgent: false },
    { id: "fatigue", label: L("Fatigue", "خستگی"), urgent: false },
    { id: "cough", label: L("Cough", "سرفه"), urgent: false },
    { id: "rash", label: L("Skin rash", "بثورات پوستی"), urgent: false },
    { id: "chest-pain", label: L("Chest pain", "درد قفسه سینه"), urgent: true },
    { id: "breathing", label: L("Difficulty breathing", "مشکل در تنفس"), urgent: true },
  ],
  history: [
    { daysAgo: 1, mood: 4, symptoms: ["headache"] },
    { daysAgo: 2, mood: 3, symptoms: ["dizziness", "fatigue"] },
    { daysAgo: 3, mood: 4, symptoms: [] },
    { daysAgo: 4, mood: 5, symptoms: [] },
  ],
};

const pharmacist = {
  reviews: [
    { id: "rv-1", patientId: "pt-3", prescriptionId: "rx-1004", priority: "high", interactionIds: ["ix-1"], status: "open", waitingMinutes: 42 },
    { id: "rv-2", patientId: "pt-sara", prescriptionId: "rx-1001", priority: "high", interactionIds: ["ix-1"], status: "open", waitingMinutes: 130 },
    { id: "rv-3", patientId: "pt-2", prescriptionId: "rx-1005", priority: "normal", interactionIds: ["ix-2"], status: "open", waitingMinutes: 260 },
  ],
  questions: [
    { id: "q-1", patientId: "pt-sara", askedMinutesAgo: 25, text: L("Can I take Demopril and Nocturin close together?", "آیا می‌توانم دموپریل و نوکتورین را نزدیک هم مصرف کنم؟"), aiDraft: L("Based on the demo interaction table, a pharmacist review is suggested before changing timing. (AI draft: needs pharmacist approval)", "بر اساس جدول نمایشی تداخل، پیش از تغییر زمان مصرف بازبینی داروساز توصیه می‌شود. (پیش‌نویس هوش مصنوعی: نیازمند تأیید داروساز)"), sourceIds: ["ks-4"], status: "open" },
    { id: "q-2", patientId: "pt-1", askedMinutesAgo: 180, text: L("I forgot yesterday's dose. What should I do?", "دوز دیروز را فراموش کردم. چه کنم؟"), aiDraft: L("The demo missed-dose guide says not to double the next dose. (AI draft: needs pharmacist approval)", "راهنمای نمایشی دوز فراموش‌شده می‌گوید دوز بعدی را دوبرابر نکنید. (پیش‌نویس هوش مصنوعی: نیازمند تأیید داروساز)"), sourceIds: ["ks-3"], status: "open" },
    { id: "q-3", patientId: "pt-6", askedMinutesAgo: 600, text: L("Is it normal to have a dry cough?", "آیا سرفهٔ خشک طبیعی است؟"), aiDraft: L("There is not enough information in the demo sources. Please ask the patient to describe duration and severity.", "اطلاعات کافی در منابع نمایشی وجود ندارد. لطفاً از بیمار مدت و شدت را بپرسید."), sourceIds: [], status: "open" },
  ],
  followUps: [
    { id: "fu-1", patientId: "pt-1", dueInDays: 0, reason: L("Check adherence after ADR report", "بررسی پایبندی پس از گزارش عارضه"), done: false },
    { id: "fu-2", patientId: "pt-3", dueInDays: 1, reason: L("Call about drowsiness", "تماس دربارهٔ خواب‌آلودگی"), done: false },
    { id: "fu-3", patientId: "pt-6", dueInDays: 3, reason: L("Inhaler technique check", "بررسی روش مصرف اسپری"), done: false },
    { id: "fu-4", patientId: "pt-2", dueInDays: -1, reason: L("Confirm stomach upset resolved", "تأیید رفع ناراحتی معده"), done: true },
  ],
};

const pharmacy = {
  inventory: [
    { drugId: "drug-demopril", stock: 240, reorderLevel: 100, batch: "DEMO-A17", expiresInMonths: 14 },
    { drugId: "drug-glycanor", stock: 64, reorderLevel: 120, batch: "DEMO-B02", expiresInMonths: 9 },
    { drugId: "drug-respirex", stock: 38, reorderLevel: 30, batch: "DEMO-C11", expiresInMonths: 5 },
    { drugId: "drug-nocturin", stock: 150, reorderLevel: 80, batch: "DEMO-D05", expiresInMonths: 2 },
    { drugId: "drug-solvita", stock: 320, reorderLevel: 100, batch: "DEMO-E09", expiresInMonths: 20 },
  ],
  dispensing: [
    { id: "dp-1", prescriptionId: "rx-1005", patientId: "pt-2", stage: "received" },
    { id: "dp-2", prescriptionId: "rx-1004", patientId: "pt-3", stage: "received" },
    { id: "dp-3", prescriptionId: "rx-1003", patientId: "pt-1", stage: "preparing" },
    { id: "dp-4", prescriptionId: "rx-1006", patientId: "pt-6", stage: "ready" },
    { id: "dp-5", prescriptionId: "rx-1002", patientId: "pt-sara", stage: "handed" },
  ],
  requests: [
    { id: "rq-1", patientId: "pt-sara", kind: "refill", drugId: "drug-glycanor", note: L("Refill request", "درخواست تمدید"), agoMinutes: 35 },
    { id: "rq-2", patientId: "pt-4", kind: "question", drugId: "drug-solvita", note: L("Question about timing", "پرسش دربارهٔ زمان مصرف"), agoMinutes: 120 },
    { id: "rq-3", patientId: "pt-5", kind: "delivery", drugId: "drug-demopril", note: L("Home delivery request", "درخواست ارسال به منزل"), agoMinutes: 300 },
  ],
  alerts: [
    { id: "pa-1", kind: "low-stock", drugId: "drug-glycanor", severity: "warning", text: L("Stock below reorder level", "موجودی کمتر از حد سفارش") },
    { id: "pa-2", kind: "expiry", drugId: "drug-nocturin", severity: "warning", text: L("Batch DEMO-D05 expires in 2 months", "بچ DEMO-D05 تا ۲ ماه دیگر منقضی می‌شود") },
    { id: "pa-3", kind: "recall-demo", drugId: "drug-respirex", severity: "info", text: L("Demo notice: sample recall drill, no action", "اعلان نمایشی: تمرین فراخوان، بدون اقدام") },
  ],
};

const industry = {
  k: 11,
  months: 12,
  adrTrend: [
    { drugId: "drug-demopril", counts: [14, 15, 13, 16, 18, 17, 21, 24, 22, 26, 29, 31] },
    { drugId: "drug-nocturin", counts: [9, 8, 12, 10, 13, 15, 14, 17, 19, 18, 21, 23] },
    { drugId: "drug-glycanor", counts: [22, 21, 23, 20, 22, 21, 19, 20, 18, 19, 17, 18] },
    { drugId: "drug-respirex", counts: [4, 6, 5, 7, 6, 8, 9, 7, 8, 10, 9, 12] },
  ],
  experience: [
    { dimension: L("Ease of taking doses on time", "سهولت مصرف به‌موقع"), score: 78, n: 1240 },
    { dimension: L("Understanding of instructions", "درک دستورهای مصرف"), score: 84, n: 1240 },
    { dimension: L("Confidence in side-effect information", "اطمینان به اطلاعات عوارض"), score: 69, n: 1180 },
    { dimension: L("Access to pharmacist help", "دسترسی به کمک داروساز"), score: 73, n: 1102 },
  ],
  signals: [
    { id: "sg-1", drugId: "drug-demopril", term: L("Dizziness on standing (fictional)", "سرگیجه هنگام ایستادن (فرضی)"), strength: "moderate", reports: 31, status: "monitoring", firstSeenMonthsAgo: 5 },
    { id: "sg-2", drugId: "drug-nocturin", term: L("Next-day drowsiness (fictional)", "خواب‌آلودگی روز بعد (فرضی)"), strength: "strong", reports: 23, status: "new", firstSeenMonthsAgo: 2 },
    { id: "sg-3", drugId: "drug-respirex", term: L("Throat irritation (fictional)", "تحریک گلو (فرضی)"), strength: "weak", reports: 8, status: "monitoring", firstSeenMonthsAgo: 7 },
    { id: "sg-4", drugId: "drug-glycanor", term: L("Stomach upset (fictional)", "ناراحتی معده (فرضی)"), strength: "weak", reports: 18, status: "closed", firstSeenMonthsAgo: 11 },
  ],
  reports: [
    { id: "rp-1", title: L("Quarterly ADR summary (demo)", "خلاصهٔ فصلی عوارض (نمایشی)"), period: L("Last quarter", "فصل گذشته"), kind: L("Aggregate PDF", "PDF تجمیعی"), updatedDaysAgo: 4 },
    { id: "rp-2", title: L("Patient experience snapshot (demo)", "تصویر لحظه‌ای تجربهٔ بیمار (نمایشی)"), period: L("Last 30 days", "۳۰ روز گذشته"), kind: L("Dashboard export", "خروجی داشبورد"), updatedDaysAgo: 9 },
    { id: "rp-3", title: L("Signal review log (demo)", "گزارش بازبینی سیگنال (نمایشی)"), period: L("Year to date", "از ابتدای سال"), kind: L("Aggregate CSV", "CSV تجمیعی"), updatedDaysAgo: 15 },
  ],
};

const physician = {
  physicianName: L("Dr. Reza Karimi (demo)", "دکتر رضا کریمی (نمایشی)"),
  aiSummary: {
    "pt-sara": {
      text: L(
        "Adherence is 86% over 14 days, with a dip 4 to 6 days ago that overlaps with a reported morning dizziness. A pharmacist review of Demopril + Nocturin is open. (AI draft from demo data; requires physician confirmation.)",
        "پایبندی در ۱۴ روز گذشته ۸۶٪ است و ۴ تا ۶ روز پیش افتی دیده می‌شود که با سرگیجهٔ صبحگاهی گزارش‌شده هم‌زمان است. بازبینی داروساز برای دموپریل + نوکتورین باز است. (پیش‌نویس هوش مصنوعی از داده نمایشی؛ نیازمند تأیید پزشک.)",
      ),
      sourceIds: ["ks-4", "ks-3"],
      confidence: "medium",
    },
  },
  defaultAiSummary: {
    text: L("Summary is generated from demo records only. Review adherence and symptoms before acting. (AI draft; requires physician confirmation.)", "خلاصه فقط از سوابق نمایشی ساخته شده است. پیش از اقدام، پایبندی و علائم را بررسی کنید. (پیش‌نویس هوش مصنوعی؛ نیازمند تأیید پزشک.)"),
    sourceIds: ["ks-3"],
    confidence: "low",
  },
  reports: [
    { id: "pr-1", title: L("Panel adherence overview", "نمای کلی پایبندی بیماران"), kind: L("Table + chart", "جدول و نمودار"), updatedDaysAgo: 1 },
    { id: "pr-2", title: L("ADR summary for my patients", "خلاصهٔ عوارض بیماران من"), kind: L("Summary", "خلاصه"), updatedDaysAgo: 3 },
    { id: "pr-3", title: L("Prescription changes this month", "تغییرات نسخه در این ماه"), kind: L("List", "فهرست"), updatedDaysAgo: 6 },
  ],
};

const ai = {
  assistantName: L("MedSmarter Assistant", "دستیار مدسمارتر"),
  quickQuestions: [
    { id: "qq-1", text: L("What should I do if I miss a dose?", "اگر یک دوز را فراموش کنم چه کنم؟") },
    { id: "qq-2", text: L("Should I take Glycanor with food?", "آیا گلیکانور را با غذا مصرف کنم؟") },
    { id: "qq-3", text: L("Can Demopril and Nocturin be taken together?", "آیا دموپریل و نوکتورین را می‌توان با هم مصرف کرد؟") },
    { id: "qq-4", text: L("What are the warnings for Nocturin?", "هشدارهای نوکتورین چیست؟") },
  ],
  answers: [
    {
      id: "ans-missed",
      keywords: { en: ["miss", "forgot", "forget", "skipped"], fa: ["فراموش", "جا انداخ", "جا افتاد"] },
      text: L(
        "In the demo guide, a missed dose should be taken as soon as you remember, unless the next dose is close. Do not take a double dose. If you are unsure, ask your pharmacist.",
        "طبق راهنمای نمایشی، دوز فراموش‌شده را به محض یادآوری مصرف کنید، مگر اینکه زمان دوز بعدی نزدیک باشد. هرگز دوز را دوبرابر نکنید. اگر مطمئن نیستید از داروساز بپرسید.",
      ),
      confidence: "high",
      sourceIds: ["ks-3"],
      evidence: [{ sourceId: "ks-3", section: L("Section 2: Missed doses", "بخش ۲: دوز فراموش‌شده"), excerpt: L("Demo text: take the missed dose when remembered unless the next dose is near; never double.", "متن نمایشی: دوز فراموش‌شده هنگام یادآوری مصرف شود مگر دوز بعدی نزدیک باشد؛ هرگز دوبرابر نشود.") }],
      followUps: [L("What if it is almost time for the next dose?", "اگر تقریباً زمان دوز بعدی باشد چه؟")],
      escalate: false,
    },
    {
      id: "ans-food",
      keywords: { en: ["food", "meal", "breakfast", "with meals"], fa: ["غذا", "صبحانه", "وعده"] },
      text: L("The demo leaflet for Glycanor says to take it with meals to reduce stomach upset.", "برگهٔ نمایشی گلیکانور می‌گوید آن را همراه غذا مصرف کنید تا ناراحتی معده کمتر شود."),
      confidence: "high",
      sourceIds: ["ks-2"],
      evidence: [{ sourceId: "ks-2", section: L("How to take", "نحوه مصرف"), excerpt: L("Demo text: take with breakfast and dinner.", "متن نمایشی: همراه صبحانه و شام مصرف شود.") }],
      followUps: [],
      escalate: false,
    },
    {
      id: "ans-interaction",
      keywords: { en: ["together", "interaction", "combine", "same time"], fa: ["با هم", "تداخل", "نزدیک هم", "هم‌زمان"] },
      text: L(
        "The demo interaction table flags Demopril + Nocturin as a moderate (fictional) interaction: dizziness may feel stronger. Please do not change timing or doses yourself; a pharmacist review is recommended.",
        "جدول نمایشی تداخل، دموپریل + نوکتورین را یک تداخل متوسط (فرضی) علامت زده است: ممکن است سرگیجه بیشتر شود. لطفاً زمان یا دوز را خودسرانه تغییر ندهید؛ بازبینی داروساز توصیه می‌شود.",
      ),
      confidence: "medium",
      sourceIds: ["ks-4", "ks-1"],
      evidence: [
        { sourceId: "ks-4", section: L("Row 1", "ردیف ۱"), excerpt: L("Demo text: Demopril + Nocturin, moderate, pharmacist review suggested.", "متن نمایشی: دموپریل + نوکتورین، متوسط، بازبینی داروساز پیشنهاد می‌شود.") },
        { sourceId: "ks-1", section: L("Warnings", "هشدارها"), excerpt: L("Demo text: may cause dizziness when standing quickly.", "متن نمایشی: ممکن است هنگام برخاستن سریع سرگیجه ایجاد کند.") },
      ],
      followUps: [],
      escalate: true,
    },
    {
      id: "ans-warnings",
      keywords: { en: ["warning", "side effect", "drowsy", "sleep", "drive"], fa: ["هشدار", "عارضه", "خواب‌آلود", "رانندگی"] },
      text: L("The demo monograph for Nocturin warns about drowsiness. Avoid driving after taking it. This information is fictional.", "تک‌نگار نمایشی نوکتورین دربارهٔ خواب‌آلودگی هشدار می‌دهد. پس از مصرف رانندگی نکنید. این اطلاعات فرضی است."),
      confidence: "medium",
      sourceIds: ["ks-1"],
      evidence: [{ sourceId: "ks-1", section: L("Warnings", "هشدارها"), excerpt: L("Demo text: drowsiness; avoid driving.", "متن نمایشی: خواب‌آلودگی؛ از رانندگی پرهیز شود.") }],
      followUps: [],
      escalate: false,
    },
  ],
  fallback: {
    text: L(
      "I could not find enough information about this in the approved demo sources, so I will not guess. Would you like to send this question to a pharmacist?",
      "درباره این موضوع در منابع نمایشی تأییدشده اطلاعات کافی پیدا نکردم و حدس نمی‌زنم. می‌خواهید این پرسش را برای داروساز بفرستم؟",
    ),
    confidence: "low",
    sourceIds: [],
    evidence: [],
    followUps: [],
    escalate: true,
  },
  redFlag: {
    keywords: { en: ["chest pain", "can't breathe", "cannot breathe", "overdose", "unconscious", "suicid"], fa: ["درد قفسه", "نمی‌توانم نفس", "نمیتونم نفس", "مصرف بیش از حد", "بی‌هوش", "خودکشی"] },
    title: L("This may be urgent", "ممکن است اورژانسی باشد"),
    body: L(
      "I cannot assess emergencies. If you or someone else may be in danger, contact your local emergency service now. (Demo screen: no real emergency number is configured.)",
      "من نمی‌توانم شرایط اورژانسی را ارزیابی کنم. اگر خودتان یا فرد دیگری در خطر است همین حالا با اورژانس محلی تماس بگیرید. (صفحه نمایشی: هیچ شماره اورژانس واقعی تنظیم نشده است.)",
    ),
  },
  conversations: [
    {
      id: "conv-1", title: L("Missed doses", "دوز فراموش‌شده"), updatedMinutesAgo: 2900,
      messages: [
        { role: "user", text: L("What should I do if I miss a dose?", "اگر یک دوز را فراموش کنم چه کنم؟") },
        { role: "assistant", answerId: "ans-missed" },
      ],
    },
    {
      id: "conv-2", title: L("Glycanor and food", "گلیکانور و غذا"), updatedMinutesAgo: 8700,
      messages: [
        { role: "user", text: L("Should I take Glycanor with food?", "آیا گلیکانور را با غذا مصرف کنم؟") },
        { role: "assistant", answerId: "ans-food" },
      ],
    },
  ],
};

export const demoData = {
  meta: {
    version: 1,
    label: L("DEMO DATA — NOT FOR CLINICAL USE", "داده نمایشی — غیرقابل استفاده بالینی"),
    note: L("All names, drugs, doses, interactions and statistics are fictional.", "همهٔ نام‌ها، داروها، دوزها، تداخل‌ها و آمارها ساختگی هستند."),
  },
  activeIngredients, drugs, knowledgeSources, interactions, patients, prescriptions, adrReports,
  patientHome, checkIn, pharmacist, pharmacy, industry, physician, ai,
};
