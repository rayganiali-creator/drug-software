import type { Localized } from "../i18n/I18nProvider";

// Domain types for the Phase 2 UI. Every record comes from DEMO DATA (design/mock/demo-data.mjs).
export type { Localized };
export type Risk = "low" | "medium" | "high";
export type Severity = "mild" | "moderate" | "serious";
export type Confidence = "high" | "medium" | "low";

export interface ActiveIngredient { id: string; name: Localized }
export interface Drug {
  id: string;
  name: Localized;
  activeIngredient: ActiveIngredient;
  strength: string;
  form: Localized;
  route: Localized;
  category: Localized;
  instructions: Localized;
  warnings: Localized[];
  demo: true;
}
export interface KnowledgeSource { id: string; title: Localized; type: Localized; version: string; demo: true }
export interface Interaction {
  id: string;
  drugA: Drug;
  drugB: Drug;
  severity: "minor" | "moderate" | "major";
  summary: Localized;
  management: Localized;
  source: KnowledgeSource;
}
export interface Patient {
  id: string;
  name: Localized;
  age: number;
  sex: "F" | "M";
  risk: Risk;
  adherence: number;
  lastCheckInDaysAgo: number;
  conditions: Localized[];
  allergies: Localized[];
  drugs: Drug[];
  adherenceSeries: number[];
  symptoms: Symptom[];
  demo: true;
}
export interface Symptom { id: string; term: Localized; severity: "mild" | "moderate" | "serious"; daysAgo: number }
export interface PrescriptionItem { drug: Drug; dose: Localized; frequency: Localized; times: string[]; durationDays: number }
export interface Prescription {
  id: string;
  patient: Patient;
  prescriber: Localized;
  issuedDaysAgo: number;
  status: "active" | "pending-review" | "dispensed";
  refillsLeft: number;
  items: PrescriptionItem[];
  demo: true;
}
export interface PatientMedication { drug: Drug; item: PrescriptionItem; prescriptionId: string }
export type DoseStatus = "taken" | "missed" | "upcoming";
export interface Dose { id: string; drug: Drug; time: string; status: DoseStatus; isNext: boolean; tomorrow: boolean }
export interface AdverseReport {
  id: string;
  patient: Patient;
  drug: Drug;
  event: Localized;
  severity: Severity;
  status: "new" | "under-review" | "reviewed";
  reportedDaysAgo: number;
  causality: "unassessed" | "unlikely" | "possible" | "probable";
  demo: true;
}
export interface HealthAlert { id: string; severity: "info" | "warning" | "danger"; title: Localized; body: Localized; action: string }
export interface ActivityItem { id: string; kind: "dose" | "checkin" | "ai" | "review"; minutesAgo: number; text: Localized }
export interface CheckInConfig {
  moods: { value: number; label: Localized }[];
  symptoms: { id: string; label: Localized; urgent: boolean }[];
  history: { daysAgo: number; mood: number; symptoms: string[] }[];
}
export interface CheckInSubmission { mood: number; symptomIds: string[]; note: string }
export interface CheckInResult { urgent: boolean }

export interface Evidence { source: KnowledgeSource; section: Localized; excerpt: Localized }
export interface AssistantAnswer {
  id: string;
  kind: "answer" | "refusal" | "red-flag";
  text: Localized;
  title?: Localized;
  confidence: Confidence;
  sources: KnowledgeSource[];
  evidence: Evidence[];
  followUps: Localized[];
  canEscalate: boolean;
}
export type ChatMessage =
  | { id: string; role: "user"; text: string | Localized; attachment?: string }
  | { id: string; role: "assistant"; answer: AssistantAnswer };
export interface Conversation { id: string; title: Localized; updatedMinutesAgo: number; messages: ChatMessage[] }
export interface AskInput { text: string; locale: "fa" | "en"; contextDrugId?: string }

export interface ReviewItem { id: string; patient: Patient; prescription: Prescription; priority: "high" | "normal"; interactions: Interaction[]; status: "open" | "done"; waitingMinutes: number }
export interface PatientQuestion { id: string; patient: Patient; askedMinutesAgo: number; text: Localized; aiDraft: Localized; sources: KnowledgeSource[]; status: "open" | "answered" }
export interface FollowUp { id: string; patient: Patient; dueInDays: number; reason: Localized; done: boolean }
export interface InventoryItem { drug: Drug; stock: number; reorderLevel: number; batch: string; expiresInMonths: number; status: "ok" | "low" | "expiring" }
export type DispenseStage = "received" | "preparing" | "ready" | "handed";
export interface DispensingItem { id: string; prescription: Prescription; patient: Patient; stage: DispenseStage }
export interface PatientRequest { id: string; patient: Patient; kind: "refill" | "question" | "delivery"; drug: Drug; note: Localized; agoMinutes: number }
export interface PharmacyAlert { id: string; kind: "low-stock" | "expiry" | "recall-demo"; drug: Drug; severity: "info" | "warning"; text: Localized }

export interface TrendSeries { drug: Drug; counts: number[] }
export interface ExperienceScore { dimension: Localized; score: number; n: number }
export interface Signal { id: string; drug: Drug; term: Localized; strength: "weak" | "moderate" | "strong"; reports: number; status: "new" | "monitoring" | "closed"; firstSeenMonthsAgo: number }
export interface ReportItem { id: string; title: Localized; period?: Localized; kind: Localized; updatedDaysAgo: number }

// ---- service contracts (Mock now; real backend later) ----
export interface PatientService {
  currentPatient(): Promise<Patient>;
  list(): Promise<Patient[]>;
  get(id: string): Promise<Patient>;
  checkInConfig(): Promise<CheckInConfig>;
  submitCheckIn(input: CheckInSubmission): Promise<CheckInResult>;
  hasCheckedInToday(): Promise<boolean>;
}
export interface MedicationService {
  drug(id: string): Promise<Drug>;
  forPatient(patientId: string): Promise<PatientMedication[]>;
  todaysDoses(patientId: string): Promise<Dose[]>;
  setDose(doseId: string, status: "taken" | "upcoming"): Promise<Dose[]>;
}
export interface PrescriptionService {
  list(filter?: { patientId?: string; status?: Prescription["status"] }): Promise<Prescription[]>;
  get(id: string): Promise<Prescription>;
  interactionsFor(prescriptionId: string): Promise<Interaction[]>;
}
export interface AIService {
  conversations(): Promise<Conversation[]>;
  quickQuestions(): Promise<Localized[]>;
  ask(input: AskInput): Promise<AssistantAnswer>;
  escalate(answerId: string): Promise<{ status: "sent" }>;
  physicianSummary(patientId: string): Promise<{ text: Localized; sources: KnowledgeSource[]; confidence: Confidence }>;
}
export interface ADRService {
  list(filter?: { patientId?: string; status?: AdverseReport["status"] }): Promise<AdverseReport[]>;
}
export interface NotificationService {
  alerts(patientId: string): Promise<HealthAlert[]>;
  activity(patientId: string): Promise<ActivityItem[]>;
}
export interface AnalyticsService {
  k(): number;
  adrTrend(): Promise<TrendSeries[]>;
  experience(): Promise<ExperienceScore[]>;
  signals(): Promise<Signal[]>;
  reports(): Promise<ReportItem[]>;
  physicianReports(): Promise<ReportItem[]>;
  adherenceSeries(patientId: string): Promise<number[]>;
}
export interface PharmacistService {
  reviews(): Promise<ReviewItem[]>;
  questions(): Promise<PatientQuestion[]>;
  followUps(): Promise<FollowUp[]>;
  completeReview(id: string): Promise<ReviewItem[]>;
}
export interface PharmacyService {
  inventory(): Promise<InventoryItem[]>;
  dispensing(): Promise<DispensingItem[]>;
  advance(id: string): Promise<DispensingItem[]>;
  requests(): Promise<PatientRequest[]>;
  alerts(): Promise<PharmacyAlert[]>;
}

export interface Services {
  patients: PatientService;
  medications: MedicationService;
  prescriptions: PrescriptionService;
  ai: AIService;
  adr: ADRService;
  notifications: NotificationService;
  analytics: AnalyticsService;
  pharmacist: PharmacistService;
  pharmacy: PharmacyService;
}
