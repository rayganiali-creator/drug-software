// GENERATED from security/access-catalog.json by security/build.mjs - do not edit.
export const permissions = {
  patientProfileRead: "patient.profile.read",
  patientProfileUpdate: "patient.profile.update",
  patientMedicationsRead: "patient.medications.read",
  patientMedicationsUpdate: "patient.medications.update",
  patientPrescriptionsRead: "patient.prescriptions.read",
  patientAdherenceRead: "patient.adherence.read",
  patientAdrRead: "patient.adr.read",
  patientAdrCreate: "patient.adr.create",
  patientCheckinsRead: "patient.checkins.read",
  patientCheckinsCreate: "patient.checkins.create",
  patientSymptomsRead: "patient.symptoms.read",
  patientAiSummaryRead: "patient.ai-summary.read",
  prescriptionCreate: "prescription.create",
  prescriptionUpdate: "prescription.update",
  prescriptionReview: "prescription.review",
  adrReview: "adr.review",
  pharmacyInventoryRead: "pharmacy.inventory.read",
  pharmacyInventoryUpdate: "pharmacy.inventory.update",
  pharmacyPrescriptionsRead: "pharmacy.prescriptions.read",
  pharmacyDispensingManage: "pharmacy.dispensing.manage",
  pharmacyRequestsManage: "pharmacy.requests.manage",
  pharmacyAlertsRead: "pharmacy.alerts.read",
  orgRead: "org.read",
  orgManage: "org.manage",
  analyticsRead: "analytics.read",
  signalsRead: "signals.read",
  researchDatasetRequest: "research.dataset.request",
  consentRead: "consent.read",
  consentGrant: "consent.grant",
  consentRevoke: "consent.revoke",
  sessionRead: "session.read",
  sessionRevoke: "session.revoke",
  auditReadOwn: "audit.read.own",
  interactionRead: "interaction.read",
  aiUse: "ai.use",
  aiReview: "ai.review",
  aiManage: "ai.manage",
  aiEvaluationRead: "ai.evaluation.read",
  knowledgeRead: "knowledge.read",
  knowledgeManage: "knowledge.manage",
  knowledgePublish: "knowledge.publish",
  userRead: "user.read",
  userManage: "user.manage",
  roleManage: "role.manage",
  auditRead: "audit.read",
  sessionRevokeAny: "session.revoke.any",
  medicationRead: "medication.read",
  insuranceImport: "insurance.import",
  insuranceRead: "insurance.read",
} as const;
export type Permission = (typeof permissions)[keyof typeof permissions];

export const roleNames = ["Patient", "Physician", "Pharmacist", "PharmacyAdmin", "PharmaceuticalCompany", "Researcher", "ContentManager", "AIManager", "SystemAdmin"] as const;
export type RoleName = (typeof roleNames)[number];

export const consentPurposes = ["Treatment", "MedicationReview", "Dispensing", "Research"] as const;
export type ConsentPurpose = (typeof consentPurposes)[number];

export const dataScopes = ["profile", "medications", "prescriptions", "adherence", "adr", "checkins", "symptoms", "ai_summary"] as const;
export type DataScope = (typeof dataScopes)[number];
