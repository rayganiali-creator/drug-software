import { RecordsError, type AiContext, type Allergy, type CareRow, type Condition, type ConsentEvent, type ConsentRow, type DoseSlot, type Freshness, type GuidanceMessage, type GuidanceSamples, type GuidanceStatus,
  type MedicationInput, type PatientDto, type PatientMedication, type ProcessResult, type ProductRecord, type Profile, type Provider, type Queue, type RecordVersion, type Report, type ScheduleEntry, type Symptom } from "./types";

/** An authorised fetch relative to the API base URL (supplied by the auth backend, which owns the token). */
export type ApiFetch = (path: string, init?: RequestInit) => Promise<Response>;

/**
 * Client of the patient-layer endpoints. It holds no secret and no business rule: the server decides who may see or change what, and every
 * refusal arrives as an HTTP status that is turned into a `RecordsError` here. Without an authorised fetch (the in-browser demo accounts)
 * every call fails with `notConnected` instead of pretending to work.
 */
export class RecordsApi {
  constructor(private readonly apiFetch: ApiFetch | undefined) {}

  private async call<T>(method: string, path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
    if (!this.apiFetch) throw new RecordsError("notConnected");
    let res: Response;
    try {
      res = await this.apiFetch(path, {
        method, signal,
        ...(body === undefined ? {} : { body: JSON.stringify(body), headers: { "Content-Type": "application/json" } }),
      });
    } catch (e) {
      if (e instanceof DOMException && e.name === "AbortError") throw e;
      throw new RecordsError("network");
    }
    if (res.ok) return res.status === 204 ? (undefined as T) : ((await res.json()) as T);
    const problem = (await res.json().catch(() => ({}))) as { code?: string; errors?: string[] };
    if (res.status === 401 || res.status === 403) throw new RecordsError("unauthorized");
    if (res.status === 404) throw new RecordsError("notFound", problem.code);
    if (res.status === 409) throw new RecordsError("conflict", problem.code, problem.errors);
    if (res.status === 400) throw new RecordsError("invalid", problem.code, problem.errors);
    if (res.status === 429) throw new RecordsError("rateLimited");
    if (res.status === 501) throw new RecordsError("notAvailable", problem.code);
    throw new RecordsError("server");
  }

  private p = (id: string, rest: string) => `/patients/${encodeURIComponent(id)}/${rest}`;

  // ---- patient, profile, lists
  ensureOwn = () => this.call<PatientDto>("POST", "/patients/me");
  profile = (id: string, s?: AbortSignal) => this.call<Profile>("GET", this.p(id, "profile"), undefined, s);
  saveProfile = (id: string, body: { yearOfBirth: number | null; sex: string | null; weightKg: number | null; heightCm: number | null; timeZone: string | null; expectedVersion: number }) => this.call<Profile>("PUT", this.p(id, "profile"), body);
  freshness = (id: string, s?: AbortSignal) => this.call<Freshness[]>("GET", this.p(id, "freshness"), undefined, s);
  conditions = (id: string, s?: AbortSignal) => this.call<Condition[]>("GET", this.p(id, "conditions"), undefined, s);
  addCondition = (id: string, body: { name: string; onsetDate: string | null; status: string; note: string | null }) => this.call<Condition>("POST", this.p(id, "conditions"), body);
  removeCondition = (id: string, cid: string) => this.call<void>("DELETE", this.p(id, `conditions/${cid}`));
  allergies = (id: string, s?: AbortSignal) => this.call<Allergy[]>("GET", this.p(id, "allergies"), undefined, s);
  addAllergy = (id: string, body: { kind: string; medicationId: string | null; substance: string | null; severity: string; reaction: string | null }) => this.call<Allergy>("POST", this.p(id, "allergies"), body);
  removeAllergy = (id: string, aid: string) => this.call<void>("DELETE", this.p(id, `allergies/${aid}`));
  symptoms = (id: string, s?: AbortSignal) => this.call<Symptom[]>("GET", this.p(id, "symptoms?take=50"), undefined, s);
  addSymptom = (id: string, body: { text: string; severity: string; onsetAt: string; resolvedAt: string | null; patientMedicationId: string | null; note: string | null }) => this.call<Symptom>("POST", this.p(id, "symptoms"), body);
  removeSymptom = (id: string, sid: string) => this.call<void>("DELETE", this.p(id, `symptoms/${sid}`));
  aiContext = (id: string, s?: AbortSignal) => this.call<AiContext>("GET", this.p(id, "ai-context"), undefined, s);

  // ---- medicines taken, schedule, doses
  medications = (id: string, includeStopped = false, s?: AbortSignal) => this.call<PatientMedication[]>("GET", this.p(id, `medications?includeStopped=${includeStopped}`), undefined, s);
  addMedication = (id: string, body: MedicationInput) => this.call<PatientMedication>("POST", this.p(id, "medications"), body);
  stopMedication = (id: string, mid: string, body: { reason: string | null; endDate: string | null; expectedVersion: number }) => this.call<PatientMedication>("POST", this.p(id, `medications/${mid}/stop`), body);
  resumeMedication = (id: string, mid: string, expectedVersion: number) => this.call<PatientMedication>("POST", this.p(id, `medications/${mid}/resume`), { expectedVersion });
  removeMedication = (id: string, mid: string) => this.call<void>("DELETE", this.p(id, `medications/${mid}`));
  medicationVersions = (id: string, mid: string) => this.call<RecordVersion[]>("GET", this.p(id, `medications/${mid}/versions`));
  schedule = (id: string, mid: string) => this.call<ScheduleEntry[]>("GET", this.p(id, `medications/${mid}/schedule`));
  addSchedule = (id: string, mid: string, body: { timeOfDay: string; days: number[] | null }) => this.call<ScheduleEntry>("POST", this.p(id, `medications/${mid}/schedule`), body);
  removeSchedule = (id: string, eid: string) => this.call<void>("DELETE", this.p(id, `schedule/${eid}`));
  doses = (id: string, date: string, s?: AbortSignal) => this.call<DoseSlot[]>("GET", this.p(id, `doses?date=${date}`), undefined, s);
  logIntake = (id: string, body: { patientMedicationId: string; scheduleEntryId: string | null; scheduledFor: string | null; status: "Taken" | "Skipped"; takenAt: string | null; note: string | null }) => this.call<unknown>("POST", this.p(id, "intake"), body);

  // ---- batch / lot records
  products = (id: string, s?: AbortSignal) => this.call<ProductRecord[]>("GET", this.p(id, "products"), undefined, s);
  addProduct = (id: string, body: Record<string, unknown>) => this.call<ProductRecord>("POST", this.p(id, "products"), body);
  removeProduct = (id: string, pid: string) => this.call<void>("DELETE", this.p(id, `products/${pid}`));
  confirmProduct = (id: string, pid: string, expectedVersion: number) => this.call<ProductRecord>("POST", this.p(id, `products/${pid}/confirm`), { expectedVersion });
  scan = (id: string, code: string) => this.call<unknown>("POST", this.p(id, "products/scan"), { code });

  // ---- manufacturer reports
  reports = (id: string, s?: AbortSignal) => this.call<Report[]>("GET", this.p(id, "manufacturer-reports"), undefined, s);
  createReport = (id: string, body: Record<string, unknown>) => this.call<Report>("POST", this.p(id, "manufacturer-reports"), body);
  submitReport = (id: string, rid: string, expectedVersion: number) => this.call<Report>("POST", this.p(id, `manufacturer-reports/${rid}/submit`), { expectedVersion });
  cancelReport = (id: string, rid: string) => this.call<Report>("POST", this.p(id, `manufacturer-reports/${rid}/cancel`));
  reviewReport = (id: string, rid: string, body: { decision: "Approve" | "Reject"; note: string | null; expectedVersion: number }) => this.call<Report>("POST", this.p(id, `manufacturer-reports/${rid}/review`), body);
  pendingReviews = (s?: AbortSignal) => this.call<Report[]>("GET", "/manufacturer-reports/pending-review", undefined, s);
  queue = (s?: AbortSignal) => this.call<Queue>("GET", "/manufacturer-reports/queue", undefined, s);
  processQueue = (max = 20) => this.call<ProcessResult>("POST", "/manufacturer-reports/queue/process", { max });
  retryReport = (rid: string) => this.call<boolean>("POST", `/manufacturer-reports/${rid}/retry`);

  // ---- care relationships, consents
  care = (s?: AbortSignal) => this.call<CareRow[]>("GET", "/care-relationships", undefined, s);
  requestCare = (counterpartUserId: string, kind: string) => this.call<unknown>("POST", "/care-relationships", { counterpartUserId, kind });
  careAction = (id: string, action: "accept" | "decline" | "end", reason?: string) => this.call<unknown>("POST", `/care-relationships/${id}/${action}`, action === "end" ? { reason: reason ?? null } : undefined);
  providers = (s?: AbortSignal) => this.call<Provider[]>("GET", "/directory/providers", undefined, s);
  consents = (s?: AbortSignal) => this.call<ConsentRow[]>("GET", "/consents", undefined, s);
  grantConsent = (body: { granteeUserId: string | null; purpose: string; scope: string[]; expiresAt: string; version: string }) => this.call<unknown>("POST", "/consents", body);
  revokeConsent = (id: string) => this.call<unknown>("DELETE", `/consents/${id}`);
  consentHistory = (s?: AbortSignal) => this.call<ConsentEvent[]>("GET", "/consents/history", undefined, s);

  // ---- messages
  messages = (s?: AbortSignal) => this.call<GuidanceMessage[]>("GET", "/guidance/messages", undefined, s);
  setMessageStatus = (id: string, status: GuidanceStatus) => this.call<GuidanceMessage>("POST", `/guidance/messages/${id}/status`, { status });
  samples = (locale: string, s?: AbortSignal) => this.call<GuidanceSamples>("GET", `/guidance/samples?locale=${locale}`, undefined, s);
}
