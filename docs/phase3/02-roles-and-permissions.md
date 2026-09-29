# 02 — Roles and permissions

Source of truth: `security/access-catalog.json` (46 permissions, 9 roles). Permission **kinds** decide which extra checks apply (see 04):

* `subject` — another person's data → relationship + consent required (`dataScope` names the data category)
* `organization` — organization data → membership required
* `aggregate` — de-identified statistics, no patient-level rows
* `own` — the caller's own account data (sessions, consents, own access log)
* `global` — platform-wide capability (admin, content, AI configuration)

Status: **IMPLEMENTED NOW**. Adding a permission = edit the catalog, run `node security/build.mjs`.

| Permission | Kind | Patient | Physician | Pharmacist | PharmacyAdmin | PharmaceuticalCompany | Researcher | ContentManager | AIManager | SystemAdmin |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `patient.profile.read` | subject (profile) | ● | ● | ● |  |  |  |  |  |  |
| `patient.profile.update` | subject (profile) | ● |  |  |  |  |  |  |  |  |
| `patient.medications.read` | subject (medications) | ● | ● | ● |  |  |  |  |  |  |
| `patient.medications.update` | subject (medications) | ● |  |  |  |  |  |  |  |  |
| `patient.prescriptions.read` | subject (prescriptions) | ● | ● | ● |  |  |  |  |  |  |
| `patient.adherence.read` | subject (adherence) | ● | ● | ● |  |  |  |  |  |  |
| `patient.adr.read` | subject (adr) | ● | ● | ● |  |  |  |  |  |  |
| `patient.adr.create` | subject (adr) | ● |  |  |  |  |  |  |  |  |
| `patient.checkins.read` | subject (checkins) | ● | ● |  |  |  |  |  |  |  |
| `patient.checkins.create` | subject (checkins) | ● |  |  |  |  |  |  |  |  |
| `patient.symptoms.read` | subject (symptoms) | ● | ● |  |  |  |  |  |  |  |
| `patient.ai-summary.read` | subject (ai_summary) | ● | ● |  |  |  |  |  |  |  |
| `prescription.create` | subject (prescriptions) |  | ● |  |  |  |  |  |  |  |
| `prescription.update` | subject (prescriptions) |  | ● |  |  |  |  |  |  |  |
| `prescription.review` | subject (prescriptions) |  |  | ● |  |  |  |  |  |  |
| `adr.review` | subject (adr) |  | ● | ● |  |  |  |  |  |  |
| `pharmacy.inventory.read` | organization |  |  |  | ● |  |  |  |  |  |
| `pharmacy.inventory.update` | organization |  |  |  | ● |  |  |  |  |  |
| `pharmacy.prescriptions.read` | organization |  |  |  | ● |  |  |  |  |  |
| `pharmacy.dispensing.manage` | organization |  |  |  | ● |  |  |  |  |  |
| `pharmacy.requests.manage` | organization |  |  |  | ● |  |  |  |  |  |
| `pharmacy.alerts.read` | organization |  |  |  | ● |  |  |  |  |  |
| `org.read` | organization |  |  |  | ● | ● |  |  |  | ● |
| `org.manage` | organization |  |  |  | ● |  |  |  |  | ● |
| `analytics.read` | aggregate |  |  |  |  | ● | ● |  |  |  |
| `signals.read` | aggregate |  |  |  |  | ● | ● |  |  |  |
| `research.dataset.request` | aggregate |  |  |  |  |  | ● |  |  |  |
| `consent.read` | own | ● |  |  |  |  |  |  |  |  |
| `consent.grant` | own | ● |  |  |  |  |  |  |  |  |
| `consent.revoke` | own | ● |  |  |  |  |  |  |  |  |
| `session.read` | own | ● | ● | ● | ● | ● | ● | ● | ● | ● |
| `session.revoke` | own | ● | ● | ● | ● | ● | ● | ● | ● | ● |
| `audit.read.own` | own | ● |  |  |  |  |  |  |  |  |
| `interaction.read` | global | ● | ● | ● |  |  |  |  |  |  |
| `ai.use` | global | ● | ● | ● |  |  |  |  |  |  |
| `ai.review` | global |  | ● | ● |  |  |  |  | ● |  |
| `ai.manage` | global |  |  |  |  |  |  |  | ● |  |
| `ai.evaluation.read` | global |  |  |  |  |  |  |  | ● |  |
| `knowledge.read` | global |  |  |  |  |  |  | ● | ● |  |
| `knowledge.manage` | global |  |  |  |  |  |  | ● |  |  |
| `knowledge.publish` | global |  |  |  |  |  |  | ● |  |  |
| `user.read` | global |  |  |  |  |  |  |  |  | ● |
| `user.manage` | global |  |  |  |  |  |  |  |  | ● |
| `role.manage` | global |  |  |  |  |  |  |  |  | ● |
| `audit.read` | global |  |  |  |  |  |  |  |  | ● |
| `session.revoke.any` | global |  |  |  |  |  |  |  |  | ● |

## Notes
* Role → permission mapping is **least privilege**: e.g. only `PharmacyAdmin` may read pharmacy inventory; pharmacists cannot.
* `SystemAdmin` manages users/roles/sessions and reads the audit log but has **no patient-data permission**.
* Web/Flutter nav is derived from the same permissions (UX only, see 08).
