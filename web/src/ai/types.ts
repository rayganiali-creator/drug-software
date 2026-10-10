// Mirrors the Phase 6 answer contract "ai-answer-1" (JSON, camelCase, enums as strings). There is deliberately no numeric confidence.
export type AnswerStatus = "Answered" | "NoEvidence" | "Refused" | "Escalated" | "Blocked" | "Unavailable";
export type EvidenceQuality = "None" | "DemoOnly" | "Unverified" | "Limited" | "Validated";
export type NextStep = "None" | "ConsultProfessional" | "ConsultPrescriber" | "EmergencyServices";
export type ProviderKind = "Disabled" | "Mock" | "External" | "Local";

export interface EvidenceSource { sourceId: string; name: string; version: string; publisher: string; receivedAt: string | null; validation: string }
export interface EvidenceItem {
  id: string; medicationId: string; medicationName: string; kind: string; text: string; qualifier: string | null; source: EvidenceSource;
  validation: string; isDemo: boolean; stale: boolean; sourceDateUnknown: boolean;
}
export interface EvidenceConflict { code: string; medicationName: string; itemIds: string[] }
export interface EvidenceSet {
  medications: { id: string; name: string; dosageForm: string; strength: string; recordVersion: number; updatedAt: string; isDemo: boolean; validation: string }[];
  items: EvidenceItem[]; conflicts: EvidenceConflict[]; missingInformation: string[]; limitations: string[]; quality: EvidenceQuality; quarantinedCount: number; excludedCount: number;
}
export interface GenerationInfo { provider: string; kind: ProviderKind; model: string | null; isMock: boolean; external: boolean }
export interface GroundedAnswer {
  text: string; provider: string; isMock: boolean; answered: boolean; notice: string; error: string;
  patientContextUsed: boolean; patientContextNote: string | null;
  status: AnswerStatus; reason: string | null; evidence: EvidenceSet | null; evidenceQuality: EvidenceQuality;
  limitations: string[] | null; missingInformation: string[] | null; nextStep: NextStep; generation: GenerationInfo | null; contractVersion: string;
}
export interface AskInput { question: string; locale: "fa" | "en"; medicationIds?: string[]; includePatientContext: boolean }

export type AssistantErrorKind = "notConnected" | "network" | "timeout" | "unauthorized" | "rateLimited" | "invalid" | "server";
export class AssistantError extends Error {
  constructor(readonly kind: AssistantErrorKind) { super(kind); }
}
