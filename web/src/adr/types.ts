// Wire types of the internal adverse-event record (Phase 9). Enums arrive as strings. The record is for human review inside this system: it is not a regulatory
// report, there is no seriousness grade and no causality score, and nothing here is computed in the browser.

export type ReporterRole = "Patient" | "Physician" | "Pharmacist";
export type ReportOrigin = "PatientReported" | "ProfessionalEntered";
export type EventCategory = "Unclassified" | "AdverseDrugReaction" | "AdverseEvent" | "AdverseDrugEvent" | "MedicationError" | "NearMiss";
export type ReviewStatus = "Submitted" | "InReview" | "InformationRequested" | "Assessed" | "Closed";
export type EntryKind = "FollowUp" | "ReviewNote" | "InformationRequest" | "ProfessionalAssessment" | "Supplement" | "Classification" | "StatusChange";
export type MedicationReferenceState = "Linked" | "LinkedUnavailable" | "ReportedNameOnly";

export const categories: EventCategory[] = ["Unclassified", "AdverseDrugReaction", "AdverseEvent", "AdverseDrugEvent", "MedicationError", "NearMiss"];
export const statuses: ReviewStatus[] = ["Submitted", "InReview", "InformationRequested", "Assessed", "Closed"];
export const reviewKinds: EntryKind[] = ["ReviewNote", "InformationRequest", "ProfessionalAssessment"];

export interface SuspectedMedication { patientMedicationId: string | null; nameAsReported: string | null; displayName: string | null; state: MedicationReferenceState }
export interface SuspectedMedicationInput { patientMedicationId: string | null; nameAsReported: string | null }
export interface FieldValue<T> { value: T | null; provenance: string; entryId: string | null }
export interface AdverseEvent {
  id: string; subjectId: string; reporterRole: ReporterRole; origin: ReportOrigin; reportedAt: string;
  original: { eventDescription: string; onsetAt: string | null; resolvedAt: string | null; outcomeAsStated: string | null; additionalDetails: string | null };
  effective: { onsetAt: FieldValue<string>; resolvedAt: FieldValue<string>; outcomeAsStated: FieldValue<string> };
  suspectedMedications: SuspectedMedication[];
  /** Null when the viewer is the patient. */
  category: EventCategory | null; status: ReviewStatus; missingInformation: string[]; lastActivityAt: string; version: number;
  signalNotice: string; internalRecordNotice: string; externalStatus: string; persianWordingStatus: string;
}
export interface Entry {
  id: string; kind: EntryKind; authorRole: ReporterRole; authorUserId: string | null; at: string; text: string | null; visibility: "PatientVisible" | "ProfessionalOnly"; provenance: string;
  fromStatus: ReviewStatus | null; toStatus: ReviewStatus | null; fromCategory: EventCategory | null; toCategory: EventCategory | null;
  supplementOnsetAt: string | null; supplementResolvedAt: string | null; supplementOutcomeAsStated: string | null;
}
export interface Detail { event: AdverseEvent; entries: Entry[] }
export interface Summary { id: string; subjectId: string; reporterRole: ReporterRole; origin: ReportOrigin; reportedAt: string; category: EventCategory | null; status: ReviewStatus; lastActivityAt: string; excerpt: string }
export interface Assistance {
  origin: string; label: string; generatedAt: string; summary: { text: string; fromSection: string }[]; missingInformation: string[]; uncertainty: string[]; notice: string; persianWordingStatus: string;
}
export interface Definition { category: EventCategory; term: string; definition: string; source: { document: string; section: string; page: string; kind: string; isLectureSlide: boolean }; note: string; persianWordingStatus: string }
export interface Definitions { definitions: Definition[]; signalNotice: string; internalRecordNotice: string; persianWordingStatus: string }
export interface CreateInput { eventDescription: string; onsetAt: string | null; resolvedAt: string | null; suspectedMedications: SuspectedMedicationInput[]; outcomeAsStated: string | null; additionalDetails: string | null; clientRequestId: string }
