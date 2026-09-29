import raw from "../../shared/demo-data.json";
import type {
  ADRService, AIService, AdverseReport, AnalyticsService, AssistantAnswer, ChatMessage, CheckInConfig, Confidence, Conversation, DispensingItem, Dose,
  Drug, Evidence, FollowUp, InventoryItem, Interaction, KnowledgeSource, Localized, MedicationService, NotificationService, Patient, PatientMedication,
  PatientQuestion, PatientRequest, PatientService, PharmacistService, PharmacyAlert, PharmacyService, Prescription, PrescriptionService, ReviewItem, Services,
} from "../types";

/* eslint-disable @typescript-eslint/no-explicit-any */
const data = raw as any;

export interface MockOptions {
  /** Simulated network latency (ms). Tests pass 0. */
  latencyMs?: number;
  /** Injectable clock so "next dose" is deterministic in tests. */
  now?: () => Date;
}

const STAGES = ["received", "preparing", "ready", "handed"] as const;
const pad = (n: number) => String(n).padStart(2, "0");
const hhmmToMinutes = (t: string) => Number(t.slice(0, 2)) * 60 + Number(t.slice(3, 5));
const dayKey = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const sleep = (ms: number) => (ms > 0 ? new Promise<void>((r) => setTimeout(r, ms)) : Promise.resolve());

export function createMockServices(options: MockOptions = {}): Services {
  const latency = options.latencyMs ?? 220;
  const now = options.now ?? (() => new Date());
  const call = async <T>(fn: () => T): Promise<T> => {
    await sleep(latency);
    return structuredClone(fn());
  };

  // ---- reference data ----
  const ingredients = new Map<string, any>(data.activeIngredients.map((a: any) => [a.id, a]));
  const drugs = new Map<string, Drug>(
    data.drugs.map((d: any) => [d.id, { id: d.id, name: d.name, activeIngredient: ingredients.get(d.activeIngredientId), strength: d.strength, form: d.form, route: d.route, category: d.category, instructions: d.instructions, warnings: d.warnings, demo: true } as Drug]),
  );
  const sources = new Map<string, KnowledgeSource>(data.knowledgeSources.map((s: any) => [s.id, { ...s, demo: true }]));
  const drug = (id: string): Drug => drugs.get(id) ?? (() => { throw new Error(`Unknown drug ${id}`); })();
  const source = (id: string): KnowledgeSource => sources.get(id) ?? (() => { throw new Error(`Unknown source ${id}`); })();

  const patients = new Map<string, Patient>(
    data.patients.map((p: any) => [p.id, {
      id: p.id, name: p.name, age: p.age, sex: p.sex, risk: p.risk, adherence: p.adherence, lastCheckInDaysAgo: p.lastCheckInDaysAgo,
      conditions: p.conditions, allergies: p.allergies, drugs: p.drugIds.map(drug), adherenceSeries: p.adherenceSeries, symptoms: p.symptoms, demo: true,
    } as Patient]),
  );
  const patient = (id: string): Patient => patients.get(id) ?? (() => { throw new Error(`Unknown patient ${id}`); })();

  const prescriptions: Prescription[] = data.prescriptions.map((r: any) => ({
    id: r.id, patient: patient(r.patientId), prescriber: r.prescriber, issuedDaysAgo: r.issuedDaysAgo, status: r.status, refillsLeft: r.refillsLeft, demo: true,
    items: r.items.map((i: any) => ({ drug: drug(i.drugId), dose: i.dose, frequency: i.frequency, times: i.times, durationDays: i.durationDays })),
  }));
  const rx = (id: string): Prescription => prescriptions.find((p) => p.id === id) ?? (() => { throw new Error(`Unknown prescription ${id}`); })();

  const interactions: Interaction[] = data.interactions.map((x: any) => ({
    id: x.id, drugA: drug(x.drugAId), drugB: drug(x.drugBId), severity: x.severity, summary: x.summary, management: x.management, source: source(x.sourceId),
  }));

  // ---- mutable demo state (in memory only) ----
  const takenDoses = new Set<string>();
  const seededMissed = `${data.patientHome.missedSeedDose.prescriptionItemDrugId}:${data.patientHome.missedSeedDose.time}`;
  let checkedInToday = false;
  const completedReviews = new Set<string>();
  const stageOverride = new Map<string, DispensingItem["stage"]>();

  const patientsSvc: PatientService = {
    currentPatient: () => call(() => patient(data.patientHome.patientId)),
    list: () => call(() => [...patients.values()]),
    get: (id) => call(() => patient(id)),
    checkInConfig: () => call(() => data.checkIn as CheckInConfig),
    submitCheckIn: (input) =>
      call(() => {
        checkedInToday = true;
        const urgent = input.symptomIds.some((id) => data.checkIn.symptoms.find((s: any) => s.id === id)?.urgent);
        return { urgent };
      }),
    hasCheckedInToday: () => call(() => checkedInToday),
  };

  function medsFor(patientId: string): PatientMedication[] {
    return prescriptions
      .filter((p) => p.patient.id === patientId && p.status !== "pending-review")
      .flatMap((p) => p.items.map((item) => ({ drug: item.drug, item, prescriptionId: p.id })));
  }

  function dosesFor(patientId: string): Dose[] {
    const t = now();
    const nowMin = t.getHours() * 60 + t.getMinutes();
    const key = dayKey(t);
    const slots = medsFor(patientId).flatMap((m) => m.item.times.map((time) => ({ drug: m.drug, time })));
    slots.sort((a, b) => hhmmToMinutes(a.time) - hhmmToMinutes(b.time));
    const doses: Dose[] = slots.map((s) => {
      const id = `${key}:${s.drug.id}:${s.time}`;
      const past = hhmmToMinutes(s.time) <= nowMin;
      let status: Dose["status"] = "upcoming";
      if (takenDoses.has(id)) status = "taken";
      else if (past) status = `${s.drug.id}:${s.time}` === seededMissed && patientId === data.patientHome.patientId ? "missed" : "taken";
      return { id, drug: s.drug, time: s.time, status, isNext: false, tomorrow: false };
    });
    const next = doses.find((d) => d.status === "upcoming");
    if (next) next.isNext = true;
    else if (doses[0]) doses.push({ ...doses[0], id: `${dayKey(new Date(t.getTime() + 86400000))}:${doses[0].drug.id}:${doses[0].time}`, status: "upcoming", isNext: true, tomorrow: true });
    return doses;
  }

  const medicationsSvc: MedicationService = {
    drug: (id) => call(() => drug(id)),
    forPatient: (id) => call(() => medsFor(id)),
    todaysDoses: (id) => call(() => dosesFor(id)),
    setDose: (doseId, status) =>
      call(() => {
        if (status === "taken") takenDoses.add(doseId);
        else takenDoses.delete(doseId);
        return dosesFor(data.patientHome.patientId);
      }),
  };

  const prescriptionsSvc: PrescriptionService = {
    list: (filter) => call(() => prescriptions.filter((p) => (!filter?.patientId || p.patient.id === filter.patientId) && (!filter?.status || p.status === filter.status))),
    get: (id) => call(() => rx(id)),
    interactionsFor: (id) =>
      call(() => {
        const ids = new Set(rx(id).items.map((i) => i.drug.id));
        return interactions.filter((x) => ids.has(x.drugA.id) && ids.has(x.drugB.id));
      }),
  };

  // ---- AI (keyword rules over canned DEMO answers; no LLM) ----
  const answerById = new Map<string, any>(data.ai.answers.map((a: any) => [a.id, a]));
  const toAnswer = (a: any, kind: AssistantAnswer["kind"] = "answer"): AssistantAnswer => ({
    id: `${a.id ?? "fallback"}-${Math.random().toString(36).slice(2, 8)}`,
    kind,
    text: a.text,
    confidence: a.confidence as Confidence,
    sources: (a.sourceIds as string[]).map(source),
    evidence: (a.evidence as any[]).map((e): Evidence => ({ source: source(e.sourceId), section: e.section, excerpt: e.excerpt })),
    followUps: a.followUps as Localized[],
    canEscalate: Boolean(a.escalate),
  });
  const matches = (text: string, words: string[]) => words.some((w) => text.includes(w.toLowerCase()));
  const aiSvc: AIService = {
    conversations: () =>
      call(() =>
        data.ai.conversations.map((c: any): Conversation => ({
          id: c.id, title: c.title, updatedMinutesAgo: c.updatedMinutesAgo,
          messages: c.messages.map((m: any, i: number): ChatMessage =>
            m.role === "user" ? { id: `${c.id}-${i}`, role: "user", text: m.text as Localized } : { id: `${c.id}-${i}`, role: "assistant", answer: toAnswer(answerById.get(m.answerId)) }),
        })),
      ),
    quickQuestions: () => call(() => data.ai.quickQuestions.map((q: any) => q.text as Localized)),
    ask: ({ text, contextDrugId }) =>
      call(() => {
        const ctx = contextDrugId ? ` ${drug(contextDrugId).name.en} ${drug(contextDrugId).name.fa}` : "";
        const lower = `${text}${ctx}`.toLowerCase();
        const rf = data.ai.redFlag;
        if (matches(lower, [...rf.keywords.en, ...rf.keywords.fa])) {
          return { id: `redflag-${Date.now()}`, kind: "red-flag", title: rf.title, text: rf.body, confidence: "high", sources: [], evidence: [], followUps: [], canEscalate: false } as AssistantAnswer;
        }
        // Best keyword-hit count wins; ties go to the first rule. No LLM is involved.
        const score = (a: any) => [...a.keywords.en, ...a.keywords.fa].filter((w: string) => lower.includes(w.toLowerCase())).length;
        const best = (data.ai.answers as any[]).reduce((acc, a) => (score(a) > acc.s ? { a, s: score(a) } : acc), { a: null as any, s: 0 });
        return best.a ? toAnswer(best.a) : toAnswer(data.ai.fallback, "refusal");
      }),
    escalate: () => call(() => ({ status: "sent" as const })),
    physicianSummary: (patientId) =>
      call(() => {
        const s = data.physician.aiSummary[patientId] ?? data.physician.defaultAiSummary;
        return { text: s.text, sources: (s.sourceIds as string[]).map(source), confidence: s.confidence as Confidence };
      }),
  };

  const adrList: AdverseReport[] = data.adrReports.map((a: any) => ({
    id: a.id, patient: patient(a.patientId), drug: drug(a.drugId), event: a.event, severity: a.severity, status: a.status, reportedDaysAgo: a.reportedDaysAgo, causality: a.causality, demo: true,
  }));
  const adrSvc: ADRService = {
    list: (f) => call(() => adrList.filter((a) => (!f?.patientId || a.patient.id === f.patientId) && (!f?.status || a.status === f.status))),
  };

  const notificationsSvc: NotificationService = {
    alerts: () => call(() => data.patientHome.alerts),
    activity: () => call(() => data.patientHome.activity),
  };

  const analyticsSvc: AnalyticsService = {
    k: () => data.industry.k,
    adrTrend: () => call(() => data.industry.adrTrend.map((s: any) => ({ drug: drug(s.drugId), counts: s.counts }))),
    experience: () => call(() => data.industry.experience),
    signals: () => call(() => data.industry.signals.map((s: any) => ({ id: s.id, drug: drug(s.drugId), term: s.term, strength: s.strength, reports: s.reports, status: s.status, firstSeenMonthsAgo: s.firstSeenMonthsAgo }))),
    reports: () => call(() => data.industry.reports),
    physicianReports: () => call(() => data.physician.reports),
    adherenceSeries: (id) => call(() => (id === data.patientHome.patientId ? data.patientHome.adherenceSeries30 : patient(id).adherenceSeries)),
  };

  const pharmacistSvc: PharmacistService = {
    reviews: () => call(() => data.pharmacist.reviews.map((r: any): ReviewItem => ({
      id: r.id, patient: patient(r.patientId), prescription: rx(r.prescriptionId), priority: r.priority, waitingMinutes: r.waitingMinutes,
      interactions: (r.interactionIds as string[]).map((id) => interactions.find((x) => x.id === id)!).filter(Boolean), status: completedReviews.has(r.id) ? "done" : "open",
    }))),
    questions: () => call(() => data.pharmacist.questions.map((q: any): PatientQuestion => ({ id: q.id, patient: patient(q.patientId), askedMinutesAgo: q.askedMinutesAgo, text: q.text, aiDraft: q.aiDraft, sources: (q.sourceIds as string[]).map(source), status: q.status }))),
    followUps: () => call(() => data.pharmacist.followUps.map((f: any): FollowUp => ({ id: f.id, patient: patient(f.patientId), dueInDays: f.dueInDays, reason: f.reason, done: f.done }))),
    completeReview: (id) => { completedReviews.add(id); return pharmacistSvc.reviews(); },
  };

  const stockStatus = (i: any): InventoryItem["status"] => (i.stock < i.reorderLevel ? "low" : i.expiresInMonths <= 3 ? "expiring" : "ok");
  const dispensingList = (): DispensingItem[] => data.pharmacy.dispensing.map((d: any) => ({ id: d.id, prescription: rx(d.prescriptionId), patient: patient(d.patientId), stage: stageOverride.get(d.id) ?? d.stage }));
  const pharmacySvc: PharmacyService = {
    inventory: () => call(() => data.pharmacy.inventory.map((i: any): InventoryItem => ({ drug: drug(i.drugId), stock: i.stock, reorderLevel: i.reorderLevel, batch: i.batch, expiresInMonths: i.expiresInMonths, status: stockStatus(i) }))),
    dispensing: () => call(dispensingList),
    advance: (id) => call(() => {
      const cur = dispensingList().find((d) => d.id === id);
      if (cur) stageOverride.set(id, STAGES[Math.min(STAGES.indexOf(cur.stage) + 1, STAGES.length - 1)]!);
      return dispensingList();
    }),
    requests: () => call(() => data.pharmacy.requests.map((r: any): PatientRequest => ({ id: r.id, patient: patient(r.patientId), kind: r.kind, drug: drug(r.drugId), note: r.note, agoMinutes: r.agoMinutes }))),
    alerts: () => call(() => data.pharmacy.alerts.map((a: any): PharmacyAlert => ({ id: a.id, kind: a.kind, drug: drug(a.drugId), severity: a.severity, text: a.text }))),
  };

  return {
    patients: patientsSvc,
    medications: medicationsSvc,
    prescriptions: prescriptionsSvc,
    ai: aiSvc,
    adr: adrSvc,
    notifications: notificationsSvc,
    analytics: analyticsSvc,
    pharmacist: pharmacistSvc,
    pharmacy: pharmacySvc,
  };
}

export const demoMeta = (raw as any).meta as { label: Localized; note: Localized };
