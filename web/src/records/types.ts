// Mirrors the Phase 5 DTOs (JSON, camelCase, enums as strings). DTOs only: never database entities.
import type { LocalizedText } from "../knowledge/types";

export type Sex = "Female" | "Male" | "Other" | "Undisclosed";
export type Category = "Profile" | "Conditions" | "Allergies" | "Medications" | "Schedule" | "Intake" | "Symptoms" | "Products";

export interface PatientDto { subjectId: string; status: "Active" | "Inactive"; isDemo: boolean; notice: string | null; createdAt: string }
export interface Profile { subjectId: string; yearOfBirth: number | null; ageYears: number | null; sex: Sex | null; weightKg: number | null; heightCm: number | null; timeZone: string; updatedAt: string; version: number; isDemo: boolean; notice: string | null }
export interface Freshness { category: Category; lastUpdatedAt: string | null; recordCount: number; neverRecorded: boolean; isStale: boolean; staleAfterDays: number }
export interface Condition { id: string; name: string; onsetDate: string | null; status: "Active" | "Resolved" | "Unknown"; note: string | null; updatedAt: string; version: number }
export type Severity3 = "Mild" | "Moderate" | "Severe";
export interface Allergy { id: string; kind: "Medication" | "ActiveIngredient" | "Other"; medicationId: string | null; substance: string; severity: "Unknown" | Severity3; reaction: string | null; updatedAt: string; version: number }
export interface Symptom { id: string; text: string; severity: Severity3; onsetAt: string; resolvedAt: string | null; patientMedicationId: string | null; note: string | null; recordedAt: string }

export type Frequency = "TimesPerDay" | "EveryNHours" | "AsNeeded" | "Other";
export type PrescriberSource = "Unknown" | "Physician" | "Pharmacist" | "SelfReported";
export interface PatientMedication {
  id: string; medicationId: string | null; displayName: string; referenceName: LocalizedText | null; isRegistered: boolean; referenceIsDemo: boolean;
  doseAmount: number | null; doseUnit: string | null; doseText: string | null; frequency: Frequency; frequencyValue: number | null; route: string | null;
  startDate: string; endDate: string | null; source: PrescriberSource; prescriberNote: string | null; status: "Active" | "Paused" | "Stopped"; stopReason: string | null; updatedAt: string; version: number;
}
export interface MedicationInput {
  medicationId: string | null; unregisteredName: string | null; doseAmount: number | null; doseUnit: string | null; doseText: string | null; frequency: Frequency; frequencyValue: number | null;
  route: string | null; startDate: string; endDate: string | null; source: PrescriberSource; prescriberNote: string | null; expectedVersion?: number | null;
}
export interface ScheduleEntry { id: string; patientMedicationId: string; timeOfDay: string; days: number[]; startDate: string; endDate: string | null; active: boolean }
export interface DoseSlot { scheduleEntryId: string; patientMedicationId: string; medicationName: string; localDate: string; localTime: string; scheduledFor: string; status: "Taken" | "Skipped" | null; logId: string | null }
export interface RecordVersion { version: number; at: string; changedBy: string | null; reason: string }

export interface ProductRecord {
  id: string; subjectId: string; patientMedicationId: string | null; medicationId: string | null; productName: string; referenceName: LocalizedText | null; genericName: string | null;
  manufacturerId: string | null; manufacturerName: string | null; batchNumber: string; manufactureDate: string | null; expiryDate: string; expired: boolean; gtin: string | null; pharmacyNote: string | null;
  receivedOn: string; method: "Manual" | "BarcodeScan" | "ExternalSystem"; verification: "SelfReported" | "ProfessionalConfirmed"; consistency: "ReferenceUnknown" | "Consistent" | "Mismatch";
  findings: string[]; recordedBy: "Patient" | "Pharmacist" | "Physician"; updatedAt: string; version: number; isDemo: boolean; notice: string | null;
}

export type ReportStatus = "Draft" | "PendingConsentOrReview" | "ReadyToSend" | "Sent" | "Acknowledged" | "Failed" | "Cancelled";
export type IssueType = "AdverseEvent" | "AbnormalAppearanceOrPackaging" | "ApparentLackOfEffect" | "QualityProblem" | "Other";
export type ReportSeverity = "Unknown" | "Mild" | "Moderate" | "Severe";
export interface ReportPayload {
  reportReference: string; productName: string; genericName: string | null; manufacturerName: string | null; batchNumber: string; manufactureDate: string | null; expiryDate: string; gtin: string | null;
  issueType: string; severity: string; occurredOn: string; durationOfUseDays: number | null; ageGroup: string; sexGroup: string; concomitantMedications: string[]; description: string | null; isDemo: boolean; schemaVersion: string;
}
export interface Report {
  id: string; subjectId: string; productRecordId: string; productName: string; batchNumber: string; status: ReportStatus; issueType: IssueType; severity: ReportSeverity; occurredOn: string; durationOfUseDays: number | null;
  description: string | null; includeConcomitantMedications: boolean; reviewRequired: boolean; consentActive: boolean; reviewerNote: string | null; reviewDecision: "Approve" | "Reject" | null; failureCode: string | null; attempts: number;
  createdAt: string; updatedAt: string; submittedAt: string | null; reviewedAt: string | null; sentAt: string | null; acknowledgedAt: string | null; isMockDelivery: boolean; version: number; isDemo: boolean; notice: string | null; payloadPreview: ReportPayload | null;
}
export interface Queue {
  reportsByStatus: Record<string, number>; outboxByState: Record<string, number>;
  entries: { id: string; reportId: string; state: string; attempts: number; nextAttemptAt: string; lastErrorCode: string | null; createdAt: string; sentAt: string | null }[];
  provider: string; providerIsMock: boolean; providerConfigured: boolean; notice: string;
}
export interface ProcessResult { considered: number; sent: number; retrying: number; failed: number; blocked: number; skipped: string | null }

export interface CareRelationship {
  id: string; patientSubjectId: string; providerUserId: string | null; kind: "Treating" | "Dispensing"; status: "PendingProvider" | "PendingPatient" | "Active" | "Declined" | "Ended"; initiatedBy: "Patient" | "Provider";
  requestedAt: string; patientConsentedAt: string | null; providerConsentedAt: string | null; endedAt: string | null; endedBy: string | null; endReason: string | null;
}
export interface CareRow { relationship: CareRelationship; patientName: string; providerName: string | null }
export interface Consent { id: string; purpose: string; scope: string[]; grantedAt: string; expiresAt: string | null; revokedAt: string | null; status: "Active" | "Expired" | "Revoked"; granteeUserId: string | null }
export interface ConsentRow { consent: Consent; granteeName: string | null }
export interface ConsentEvent { consentId: string; kind: "Granted" | "Revoked"; at: string; actorUserId: string; purpose: string }
export interface Provider { id: string; displayName: string; roles: string[] }

export interface GuidancePatient { observed: string; whyItMatters: string; suggestedAction: string; whenToConsult: string; urgentSigns: string | null; basisAndConfidence: string }
export interface GuidanceProfessional { summary: string; technicalDetail: string; severityLabel: string; basis: string; confidence: string }
export type GuidanceLevel = "Information" | "FollowUp" | "ReviewSoon" | "Urgent";
export type GuidanceStatus = "Sent" | "Seen" | "Reviewed" | "Referred" | "Resolved";
export interface GuidanceMessage { id: string; templateKey: string; level: GuidanceLevel; status: GuidanceStatus; locale: string; patient: GuidancePatient; professional: GuidanceProfessional | null; createdAt: string; isDemo: boolean; notice: string | null; origin?: { kind: string; assessmentId: string; ruleId: string; ruleVersion: number; findingKey: string; dataAsOf: string } | null }
export interface GuidanceSamples { demo: boolean; notice: string; samples: { level: GuidanceLevel; patient: GuidancePatient; professional: GuidanceProfessional | null }[] }
export interface AiContext {
  ageGroup: string; sexGroup: string; medications: { name: string; dose: string | null; frequency: string; status: string; isRegistered: boolean }[]; allergies: { substance: string; severity: string; reaction: string | null }[];
  conditions: { name: string; status: string }[]; recentSymptoms: { text: string; severity: string; onsetDate: string }[]; consentedPurposes: string[]; excluded: { category: string; reason: string }[]; externalProcessingConsented: boolean; notice: string;
}

export type ErrorKind = "notConnected" | "network" | "unauthorized" | "notFound" | "conflict" | "invalid" | "rateLimited" | "notAvailable" | "server";
export class RecordsError extends Error {
  constructor(readonly kind: ErrorKind, readonly code?: string, readonly errors: string[] = []) { super(kind); }
}
