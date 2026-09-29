import type { DataScope } from "../auth/access.g";
import type { Services } from "./types";

/** Thrown when the signed-in user may not open a patient's record (no care relationship or no consent). */
export class AccessDeniedError extends Error {
  constructor() {
    super("access denied");
    this.name = "AccessDeniedError";
  }
}

export type CanView = (subjectKey: string, scope?: DataScope) => Promise<boolean>;

/**
 * Decorates the (mock) data services so that patient-level results respect the same rules as the server:
 * a professional only sees patients they have a care relationship with AND an active consent from.
 * UX layer only. In API mode `canView` asks the real server, so the decision is the server's.
 */
export function withAccessControl(base: Services, canView: CanView): Services {
  const keepPatients = async <T>(items: T[], patientId: (i: T) => string, scope: DataScope): Promise<T[]> => {
    const cache = new Map<string, Promise<boolean>>();
    const ok = (id: string) => { let p = cache.get(id); if (!p) { p = canView(id, scope); cache.set(id, p); } return p; };
    const flags = await Promise.all(items.map((i) => ok(patientId(i))));
    return items.filter((_, idx) => flags[idx]);
  };
  return {
    ...base,
    patients: {
      ...base.patients,
      list: async () => keepPatients(await base.patients.list(), (p) => p.id, "profile"),
      get: async (id) => {
        if (!(await canView(id, "profile"))) throw new AccessDeniedError();
        return base.patients.get(id);
      },
    },
    prescriptions: {
      ...base.prescriptions,
      list: async (f) => keepPatients(await base.prescriptions.list(f), (r) => r.patient.id, "prescriptions"),
    },
    adr: { ...base.adr, list: async (f) => keepPatients(await base.adr.list(f), (a) => a.patient.id, "adr") },
    pharmacist: {
      ...base.pharmacist,
      reviews: async () => keepPatients(await base.pharmacist.reviews(), (r) => r.patient.id, "prescriptions"),
      questions: async () => keepPatients(await base.pharmacist.questions(), (q) => q.patient.id, "profile"),
      followUps: async () => keepPatients(await base.pharmacist.followUps(), (f) => f.patient.id, "profile"),
    },
    pharmacy: {
      ...base.pharmacy,
      dispensing: async () => keepPatients(await base.pharmacy.dispensing(), (d) => d.patient.id, "prescriptions"),
      requests: async () => keepPatients(await base.pharmacy.requests(), (r) => r.patient.id, "prescriptions"),
    },
  };
}
