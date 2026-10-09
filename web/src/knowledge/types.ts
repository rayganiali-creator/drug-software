// Mirrors MedSmarter.Modules.Medications.Contracts (JSON, camelCase, enums as strings). DTOs only: never database entities.

export type ValidationStatus = "Demo" | "Unverified" | "NeedsValidation" | "Validated" | "Rejected";
export type Lifecycle = "Draft" | "Active" | "Inactive";
export type StatementKind = "Indication" | "Contraindication" | "Warning" | "Precaution" | "AdverseReaction" | "Administration" | "Storage";
export type InteractionSeverity = "Unknown" | "Minor" | "Moderate" | "Major" | "Contraindicated";

export interface LocalizedText { en: string | null; fa: string | null }

export interface MedicationSummary {
  id: string;
  name: LocalizedText;
  brandName: LocalizedText | null;
  dosageForm: LocalizedText;
  strengthSummary: string;
  ingredients: LocalizedText[];
  lifecycle: Lifecycle;
  validation: ValidationStatus;
  isDemo: boolean;
  matchedOn: string | null;
  score: number;
}

export interface Paged<T> { items: T[]; total: number; limit: number; offset: number }

export interface MedicationIngredient { ingredientId: string; name: LocalizedText; strengthValue: number | null; strengthUnit: string | null; perUnit: string | null; order: number }
export interface Statement { id: string; kind: StatementKind; text: LocalizedText; severity: string | null; frequency: string | null; population: string | null; revisionId: string; sourceId: string; validation: ValidationStatus }
export interface Interaction { id: string; otherIngredientId: string; otherIngredientName: LocalizedText; severity: InteractionSeverity; mechanism: LocalizedText; management: LocalizedText; sourceId: string; validation: ValidationStatus }
export interface KnowledgeSource { id: string; name: string; publisher: string; type: string; url: string | null; version: string; licenseName: string; redistributionAllowed: boolean | null; usageRestrictions: string | null }

export interface MedicationDetail {
  id: string;
  version: number;
  name: LocalizedText;
  brand: { id: string; name: LocalizedText } | null;
  manufacturer: { id: string; name: LocalizedText; country: string | null } | null;
  dosageForm: LocalizedText;
  routes: LocalizedText[];
  strengthSummary: string;
  ingredients: MedicationIngredient[];
  classifications: { id: string; code: string; name: LocalizedText }[];
  synonyms: string[];
  identifiers: { scheme: string; value: string }[];
  statements: Statement[];
  missingKinds: StatementKind[];
  interactions: Interaction[];
  sources: KnowledgeSource[];
  lifecycle: Lifecycle;
  validation: ValidationStatus;
  isDemo: boolean;
  updatedAt: string;
  notice: string;
}

export type KnowledgeErrorKind = "notConnected" | "unauthorized" | "notFound" | "invalid" | "rateLimited" | "server" | "network";

/** Safe, UI-facing failure: a kind plus (for validation) the server's machine-readable code. Never contains server internals. */
export class KnowledgeError extends Error {
  constructor(readonly kind: KnowledgeErrorKind, readonly code?: string) {
    super(kind);
    this.name = "KnowledgeError";
  }
}
