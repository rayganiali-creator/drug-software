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
  static const medicationRead = 'medication.read';
  static const insuranceImport = 'insurance.import';
  static const insuranceRead = 'insurance.read';
  static const patientConditionsRead = 'patient.conditions.read';
  static const patientConditionsUpdate = 'patient.conditions.update';
  static const patientAllergiesRead = 'patient.allergies.read';
  static const patientAllergiesUpdate = 'patient.allergies.update';
  static const patientSymptomsCreate = 'patient.symptoms.create';
  static const patientAdherenceLog = 'patient.adherence.log';
  static const patientProductsRead = 'patient.products.read';
  static const patientProductsRecord = 'patient.products.record';
  static const manufacturerReportCreate = 'manufacturer-report.create';
  static const manufacturerReportRead = 'manufacturer-report.read';
  static const manufacturerReportReview = 'manufacturer-report.review';
  static const manufacturerReportQueueRead = 'manufacturer-report.queue.read';
  static const manufacturerReportQueueManage = 'manufacturer-report.queue.manage';
  static const careRelationshipRead = 'care.relationship.read';
  static const careRelationshipManage = 'care.relationship.manage';
  static const guidanceRead = 'guidance.read';
  static const guidanceUpdate = 'guidance.update';
  static const guidanceProfessionalRead = 'guidance.professional.read';
  static const safetyAssessRun = 'safety.assess.run';
  static const safetyAssessRead = 'safety.assess.read';
  static const clinicalrulesRead = 'clinicalrules.read';
  static const clinicalrulesAuthor = 'clinicalrules.author';
  static const clinicalrulesReview = 'clinicalrules.review';
  static const clinicalrulesRetire = 'clinicalrules.retire';
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

abstract final class ConsentPurposes {
  static const treatment = 'Treatment';
  static const medicationReview = 'MedicationReview';
  static const dispensing = 'Dispensing';
  static const research = 'Research';
  static const insuranceSharing = 'InsuranceSharing';
  static const manufacturerReport = 'ManufacturerReport';
  static const monitoring = 'Monitoring';
  static const aiProcessing = 'AiProcessing';
  static const aiExternalProcessing = 'AiExternalProcessing';
  static const granteeFree = ['InsuranceSharing', 'ManufacturerReport', 'Monitoring', 'AiProcessing', 'AiExternalProcessing'];
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
  static const conditions = 'conditions';
  static const allergies = 'allergies';
  static const products = 'products';
}
