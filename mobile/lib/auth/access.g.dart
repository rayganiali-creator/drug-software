// GENERATED from security/access-catalog.json by security/build.mjs - do not edit.
// ignore_for_file: public_member_api_docs

abstract final class Permissions {
  static const patientProfileRead = 'patient.profile.read';
  static const patientProfileUpdate = 'patient.profile.update';
  static const patientMedicationsRead = 'patient.medications.read';
  static const patientMedicationsUpdate = 'patient.medications.update';
  static const patientPrescriptionsRead = 'patient.prescriptions.read';
  static const patientAdherenceRead = 'patient.adherence.read';
  static const patientAdrRead = 'patient.adr.read';
  static const patientAdrCreate = 'patient.adr.create';
  static const patientCheckinsRead = 'patient.checkins.read';
  static const patientCheckinsCreate = 'patient.checkins.create';
  static const patientSymptomsRead = 'patient.symptoms.read';
  static const patientAiSummaryRead = 'patient.ai-summary.read';
  static const prescriptionCreate = 'prescription.create';
  static const prescriptionUpdate = 'prescription.update';
  static const prescriptionReview = 'prescription.review';
  static const adrReview = 'adr.review';
  static const pharmacyInventoryRead = 'pharmacy.inventory.read';
  static const pharmacyInventoryUpdate = 'pharmacy.inventory.update';
  static const pharmacyPrescriptionsRead = 'pharmacy.prescriptions.read';
  static const pharmacyDispensingManage = 'pharmacy.dispensing.manage';
  static const pharmacyRequestsManage = 'pharmacy.requests.manage';
  static const pharmacyAlertsRead = 'pharmacy.alerts.read';
  static const orgRead = 'org.read';
  static const orgManage = 'org.manage';
  static const analyticsRead = 'analytics.read';
  static const signalsRead = 'signals.read';
  static const researchDatasetRequest = 'research.dataset.request';
  static const consentRead = 'consent.read';
  static const consentGrant = 'consent.grant';
  static const consentRevoke = 'consent.revoke';
  static const sessionRead = 'session.read';
  static const sessionRevoke = 'session.revoke';
  static const auditReadOwn = 'audit.read.own';
  static const interactionRead = 'interaction.read';
  static const aiUse = 'ai.use';
  static const aiReview = 'ai.review';
  static const aiManage = 'ai.manage';
  static const aiEvaluationRead = 'ai.evaluation.read';
  static const knowledgeRead = 'knowledge.read';
  static const knowledgeManage = 'knowledge.manage';
  static const knowledgePublish = 'knowledge.publish';
  static const userRead = 'user.read';
  static const userManage = 'user.manage';
  static const roleManage = 'role.manage';
  static const auditRead = 'audit.read';
  static const sessionRevokeAny = 'session.revoke.any';
}

abstract final class RoleNames {
  static const patient = 'Patient';
  static const physician = 'Physician';
  static const pharmacist = 'Pharmacist';
  static const pharmacyAdmin = 'PharmacyAdmin';
  static const pharmaceuticalCompany = 'PharmaceuticalCompany';
  static const researcher = 'Researcher';
  static const contentManager = 'ContentManager';
  static const aIManager = 'AIManager';
  static const systemAdmin = 'SystemAdmin';
}

abstract final class DataScopes {
  static const profile = 'profile';
  static const medications = 'medications';
  static const prescriptions = 'prescriptions';
  static const adherence = 'adherence';
  static const adr = 'adr';
  static const checkins = 'checkins';
  static const symptoms = 'symptoms';
  static const aiSummary = 'ai_summary';
}
