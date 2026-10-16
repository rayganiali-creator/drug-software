// Wire types of the internal pharmacy safety endpoints (Phase 10). Enums arrive as strings. These are internal pharmacy records: not regulatory reports, no seriousness
// grade, no causality, nothing sent anywhere.

export type SafetyStatus = "Submitted" | "InReview" | "FollowUpRequired" | "Closed";
export type SafetyEventKind = "DispensingError" | "NearMiss";
export type ReachedPatient = "Yes" | "No" | "Unknown";
export type PharmacyRole = "Pharmacist" | "PharmacyAdmin";
export type ConcernType = "AppearanceOrPackaging" | "SuspectedQualityProblem" | "LabellingOrNaming" | "ApparentLackOfEffect" | "Other";
export type EntryKind = "ReviewNote" | "FollowUp" | "ManufacturerReportProposal" | "StatusChange";
export type DispensingErrorExample = "IncorrectMedicineFormOrStrength" | "LabelIncorrectOrInconsistentWithPrescription" | "IncorrectQuantity" | "ExpiredMedicine" | "IncorrectlyCompounded"
  | "InteractionNotDetectedOrAllergyNotDocumented" | "SomeoneElsesPrescription" | "DifferentBrandLeadingToDuplication" | "OtherNotListed";

export const examples: DispensingErrorExample[] = ["IncorrectMedicineFormOrStrength", "LabelIncorrectOrInconsistentWithPrescription", "IncorrectQuantity", "ExpiredMedicine", "IncorrectlyCompounded",
  "InteractionNotDetectedOrAllergyNotDocumented", "SomeoneElsesPrescription", "DifferentBrandLeadingToDuplication", "OtherNotListed"];
export const concernTypes: ConcernType[] = ["AppearanceOrPackaging", "SuspectedQualityProblem", "LabellingOrNaming", "ApparentLackOfEffect", "Other"];
export const allStatuses: SafetyStatus[] = ["Submitted", "InReview", "FollowUpRequired", "Closed"];

export interface Identifiers { productName: string | null; batchNumber: string | null; expiryDate: string | null; manufacturerName: string | null; gtin: string | null }
export interface SafetyEvent {
  id: string; organizationId: string; kind: SafetyEventKind; reachedPatient: ReachedPatient; errorExamples: DispensingErrorExample[]; description: string; occurredOn: string | null;
  medicineAsReported: string | null; howDetectedOrIntercepted: string | null; immediateActionTaken: string | null; reporterRole: PharmacyRole; reportedAt: string; status: SafetyStatus;
  missingInformation: string[]; linkedAdverseEventId: string | null; linkedProductConcernId: string | null; lastActivityAt: string; version: number; notice: string; externalStatus: string; persianWordingStatus: string;
}
export interface Concern {
  id: string; organizationId: string; type: ConcernType; typeBasis: string; description: string; observedOn: string | null; linkState: "LinkedToProductRecord" | "Unmatched";
  linked: Identifiers | null; linkedRecordVersion: number | null; linkedVerificationAtLinking: string | null; asReported: Identifiers | null; flags: string[]; reporterRole: PharmacyRole;
  reportedAt: string; status: SafetyStatus; linkedAdverseEventId: string | null; linkedSafetyEventId: string | null; manufacturerReportProposed: boolean; manufacturerReportProposedAt: string | null;
  lastActivityAt: string; version: number; notice: string; externalStatus: string; persianWordingStatus: string;
}
export interface Entry { id: string; kind: EntryKind; authorRole: PharmacyRole; authorUserId: string; at: string; text: string | null; fromStatus: SafetyStatus | null; toStatus: SafetyStatus | null }
export interface EventDetail { event: SafetyEvent; entries: Entry[] }
export interface ConcernDetail { concern: Concern; entries: Entry[] }
export interface Summary { id: string; recordKind: string; label: string; status: SafetyStatus; reportedAt: string; lastActivityAt: string; excerpt: string }
export interface CreateEvent {
  kind: SafetyEventKind; reachedPatient: ReachedPatient; errorExamples: DispensingErrorExample[]; description: string; occurredOn: string | null; medicineAsReported: string | null;
  howDetectedOrIntercepted: string | null; immediateActionTaken: string | null; clientRequestId: string;
}
export interface CreateConcern {
  type: ConcernType; description: string; observedOn: string | null; productRecord: { subjectId: string; productRecordId: string } | null; asReported: Identifiers | null; clientRequestId: string;
}
