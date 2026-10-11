// Wire types of the clinical safety endpoints (Phase 7). Enums arrive as strings. Nothing here is computed in the browser: every status, severity and
// "actionable" flag is decided by the server's deterministic engine.

export type RuleDomain = "Interaction" | "AllergyConflict" | "DuplicateIngredient" | "DataQuality" | "ReportedSymptom";
export type AssessmentStatus = "CompletedWithFindings" | "CompletedNoMatches" | "Incomplete" | "NoApprovedCoverage" | "Failed";
export type FindingSeverity = "Informational" | "Minor" | "Moderate" | "Major" | "Critical";
export type FindingUrgency = "None" | "Routine" | "Soon" | "Prompt";
export type RuleOutcome = "Matched" | "NoMatch" | "MissingData" | "StaleData" | "InsufficientEvidence" | "NotApplicable" | "Unavailable" | "Error";
export type ActivationState = "Active" | "DemonstrationOnly" | "Inactive";
export type RuleStatus = "Draft" | "UnderReview" | "Approved" | "Retired" | "Rejected";
export type InputAvailability = "Available" | "NeverRecorded" | "NotAuthorized" | "Unavailable";
export type GuidanceLinkState = "NotApplicable" | "Created" | "Partial" | "Failed";

export interface EvidenceRef { sourceId: string; sourceName: string; version: string; publicationDate: string | null; validation: "Demo" | "Unverified" | "NeedsValidation" | "Validated"; reviewStatus: string; locator: string | null }
export interface InputState { category: string; availability: InputAvailability; lastUpdatedAt: string | null; recordCount: number; isStale: boolean; staleAfterDays: number; source: string }
export interface FindingSubject { kind: "medication" | "allergy" | "symptom"; recordId: string; label: string }
export interface Finding {
  key: string; ruleId: string; ruleVersion: number; domain: RuleDomain; severity: FindingSeverity; urgency: FindingUrgency; actionable: boolean; isDemo: boolean; guidanceTemplateKey: string;
  subjects: FindingSubject[]; evidence: EvidenceRef[]; referenceSeverity: string | null; evidenceConflict: boolean; inputsStale: boolean; limitations: string[]; emergencySigns: string[] | null;
}
export interface RuleEvaluation { ruleId: string; version: number; domain: RuleDomain; outcome: RuleOutcome; reasons: string[]; activation: ActivationState; isDemo: boolean; partial: boolean; findingCount: number }
export interface Coverage { activeRules: number; demonstrationRules: number; inactiveRules: number; notEvaluable: number; domainsCovered: RuleDomain[]; unsupportedDomains: string[] }
export interface EngineResult {
  status: AssessmentStatus; complete: boolean; inputs: InputState[]; evaluations: RuleEvaluation[]; findings: Finding[]; coverage: Coverage; limitations: string[];
  engineVersion: string; ruleSetVersion: string; evaluatedAt: string; containsDemonstration: boolean; safetyClaimAllowed: boolean;
}
export interface GuidanceLink { findingKey: string; messageId: string | null; state: string }
export interface Assessment {
  id: string; subjectId: string; result: EngineResult; trigger: string; requestedByPatient: boolean; guidanceState: GuidanceLinkState; guidanceLinks: GuidanceLink[];
  openGuidanceNoLongerMatching: string[]; outdated: boolean; outdatedReasons: string[]; notice: string;
  inputFingerprint?: string; inputVersions?: InputVersionRef[] | null; change?: InputChange | null;
}
export interface AssessmentSummary { id: string; evaluatedAt: string; status: AssessmentStatus; complete: boolean; findingCount: number; actionableFindingCount: number; ruleSetVersion: string; trigger: string; containsDemonstration: boolean }
export interface RuleSummary { ruleId: string; version: number; title: string; domain: RuleDomain; status: RuleStatus; isDemo: boolean; activation: ActivationState; activationReasons: string[]; authoredAt: string }
export interface RuleCoverage {
  activeRules: number; demonstrationRules: number; inactiveRules: number; domainsWithActiveCoverage: RuleDomain[]; domainsWithoutActiveCoverage: RuleDomain[]; unsupportedDomains: string[]; ruleSetVersion: string; demonstrationAllowed: boolean;
}

export type SafetyErrorKind = "notConnected" | "network" | "unauthorized" | "notFound" | "conflict" | "unavailable" | "invalid" | "rateLimited" | "server";
export class SafetyError extends Error {
  constructor(readonly kind: SafetyErrorKind, readonly code?: string, readonly errors: string[] = []) { super(kind); }
}

// ---- Phase 8: change-aware assessments and scheduled checks
export interface InputVersionRef { kind: string; id: string; version: string }
export interface InputChange { firstAssessment: boolean; added: number; removed: number; changed: number; ruleSetChanged: boolean }
export type MonitoringState = "Idle" | "Running" | "Exhausted" | "Withdrawn";
export interface MonitoringStatus {
  enrolled: boolean; state: MonitoringState; enrolledAt: string | null; lastAttemptAt: string | null; lastSuccessAt: string | null; lastOutcome: string | null; lastErrorCode: string | null;
  consecutiveFailures: number; nextCheckAt: string | null; lastAssessmentId: string | null; workerEnabled: boolean; notice: string;
}

// ---- Phase 8: rule authoring and review
export interface RuleDefinition {
  ruleId: string; version: number; title: string; purpose: string; domain: RuleDomain; population: { minAgeYears: number | null; maxAgeYears: number | null; unsupported: string[] };
  requiredInputs: string[]; criteria: { kind: string; minReferenceSeverity: string | null; withinDays: number | null }; severity: FindingSeverity; urgency: FindingUrgency; guidanceTemplateKey: string;
  emergencySigns: string[] | null; evidence: EvidenceRef[]; limitations: string[]; testCases: unknown[];
}
export interface RuleLifecycle { status: RuleStatus; isDemo: boolean; author: string; authoredAt: string; reviewer: string | null; reviewedAt: string | null; reviewNote: string | null; effectiveFrom: string | null; retiredAt: string | null }
export interface RuleDetail { record: { definition: RuleDefinition; lifecycle: RuleLifecycle }; activation: { state: ActivationState; reasons: string[] } }
export interface RuleHistory { authorName: string | null; reviewerName: string | null; authorIsYou: boolean; approvalBlockers: string[]; events: { action: string; from: RuleStatus; to: RuleStatus; actor: string | null; at: string }[] }
export interface RuleOptions { domains: { domain: RuleDomain; criteria: string; requiredInputs: string[] }[]; templateKeys: string[]; severities: string[]; urgencies: string[]; validations: string[]; minReferenceSeverities: string[]; outcomes: string[] }
