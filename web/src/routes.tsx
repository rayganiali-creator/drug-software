import type { ComponentType } from "react";
import { Outlet, useRouteError, type RouteObject } from "react-router-dom";
import { areasOf } from "./auth/areas";
import { useAuth } from "./auth/AuthContext";
import { AppIndexRedirect, RequireArea, RequireAuth } from "./auth/guards";
import { LoginPage, UnauthorizedPage } from "./features/auth/AuthPages";
import { Button, EmptyState, ErrorState } from "./components/ui";
import { useI18n } from "./i18n/I18nProvider";
import { AppShell } from "./layouts/AppShell";

/** Code-splits every page: `lazy` returns the named export as the route component. */
function page<K extends string>(loader: () => Promise<Record<K, ComponentType>>, name: K): Pick<RouteObject, "lazy"> {
  return { lazy: async () => ({ Component: (await loader())[name] }) };
}

function RouteError() {
  const { t } = useI18n();
  const error = useRouteError();
  if (import.meta.env.DEV) console.error(error);
  return <ErrorState title={t("common.errorTitle")} body={t("common.errorBody")} retryLabel={t("common.reload")} onRetry={() => window.location.reload()} />;
}

function NotFound() {
  const { t } = useI18n();
  return <div style={{ padding: "var(--space-12) var(--space-4)" }}><EmptyState icon="search" title={t("common.notFound")} body={t("common.notFoundBody")} action={<Button onClick={() => window.location.assign("/")}>{t("nav.landing")}</Button>} /></div>;
}

/** Account & security uses the shell of the user's first area. */
function AccountShell() {
  const { user } = useAuth();
  return <AppShell role={areasOf(user)[0] ?? "workspace"} />;
}

const P = () => import("./features/patient/PatientHome");
const guarded = (area: Parameters<typeof RequireArea>[0]["area"], role: Parameters<typeof AppShell>[0]["role"]) => <RequireArea area={area}><AppShell role={role} /></RequireArea>;

export const routes: RouteObject[] = [
  { path: "/", ...page(() => import("./features/landing/Landing"), "Landing"), errorElement: <RouteError /> },
  { path: "/login", element: <LoginPage />, errorElement: <RouteError /> },
  { path: "/design-system", ...page(() => import("./features/designSystem/DesignSystem"), "DesignSystem"), errorElement: <RouteError /> },
  {
    path: "/app", element: <RequireAuth><Outlet /></RequireAuth>, errorElement: <RouteError />,
    children: [
      { index: true, element: <AppIndexRedirect /> },
      { path: "no-access", element: <UnauthorizedPage /> },
      { path: "account", element: <AccountShell />, children: [{ index: true, ...page(() => import("./features/auth/Account"), "Account") }] },
      {
        path: "admin", element: guarded("admin", "admin"),
        children: [{ index: true, ...page(() => import("./features/auth/Admin"), "AdminConsole") },
{ path: "queue", ...page(() => import("./features/records/ProPages"), "ReportQueue") },],
      },
      {
        path: "workspace", element: guarded("workspace", "workspace"),
        children: [{ index: true, ...page(() => import("./features/auth/Workspace"), "Workspace") },
          { path: "drugs", ...page(() => import("./features/knowledge/DrugSearch"), "DrugSearch") },
          { path: "drugs/:id", ...page(() => import("./features/knowledge/DrugSearch"), "DrugDetail") }
        ],
      },
      {
        path: "patient", element: guarded("patient", "patient"),
        children: [
            { index: true, ...page(P, "PatientHome") },
            { path: "medications", ...page(() => import("./features/patient/Medications"), "Medications") },
            { path: "medications/:id", ...page(() => import("./features/patient/Medications"), "MedicationDetail") },
            { path: "assistant", ...page(() => import("./features/assistant/AssistantEntry"), "AssistantEntry") },
            { path: "assistant/prototype", ...page(() => import("./features/assistant/Assistant"), "Assistant") },
            { path: "checkin", ...page(() => import("./features/patient/CheckIn"), "CheckIn") },
            { path: "profile", ...page(() => import("./features/patient/Profile"), "Profile") },
            { path: "records", ...page(() => import("./features/records/RecordsPage"), "PatientRecords") },
            { path: "taking", ...page(() => import("./features/records/TakingPage"), "PatientTaking") },
            { path: "batches", ...page(() => import("./features/records/BatchesPage"), "PatientBatches") },
            { path: "myreports", ...page(() => import("./features/records/ReportsPage"), "PatientReports") },
            { path: "sharing", ...page(() => import("./features/records/SharingPage"), "PatientSharing") },
            { path: "messages", ...page(() => import("./features/records/MessagesPage"), "PatientMessages") },
            { path: "safety", ...page(() => import("./features/safety/SafetyCheck"), "PatientSafety") },
          { path: "drugs", ...page(() => import("./features/knowledge/DrugSearch"), "DrugSearch") },
          { path: "drugs/:id", ...page(() => import("./features/knowledge/DrugSearch"), "DrugDetail") },
        ],
      },
      {
        path: "physician", element: guarded("physician", "physician"),
        children: [
            { index: true, ...page(() => import("./features/physician/Physician"), "PhysicianDashboard") },
            { path: "patients", ...page(() => import("./features/physician/Physician"), "PhysicianPatients") },
            { path: "patients/:id", ...page(() => import("./features/physician/Physician"), "PatientOverview") },
            { path: "prescriptions", ...page(() => import("./features/physician/Physician"), "PhysicianPrescriptions") },
            { path: "adr", ...page(() => import("./features/physician/Physician"), "PhysicianAdr") },
            { path: "reports", ...page(() => import("./features/physician/Physician"), "PhysicianReports") },
            { path: "reportreviews", ...page(() => import("./features/records/ProPages"), "ReportReviews") },
            { path: "care", ...page(() => import("./features/records/ProPages"), "CareRequests") },
            { path: "rules", ...page(() => import("./features/safety/SafetyCheck"), "RulesPage") },
            { path: "patients/:id/safety", ...page(() => import("./features/safety/SafetyCheck"), "ProSafety") },
          { path: "drugs", ...page(() => import("./features/knowledge/DrugSearch"), "DrugSearch") },
          { path: "drugs/:id", ...page(() => import("./features/knowledge/DrugSearch"), "DrugDetail") },
        ],
      },
      {
        path: "pharmacist", element: guarded("pharmacist", "pharmacist"),
        children: [
            { index: true, ...page(() => import("./features/pharmacist/Pharmacist"), "PharmacistDashboard") },
            { path: "reviews", ...page(() => import("./features/pharmacist/Pharmacist"), "PharmacistReviews") },
            { path: "interactions", ...page(() => import("./features/pharmacist/Pharmacist"), "PharmacistInteractions") },
            { path: "adr", ...page(() => import("./features/pharmacist/Pharmacist"), "PharmacistAdr") },
            { path: "adherence", ...page(() => import("./features/pharmacist/Pharmacist"), "PharmacistAdherence") },
            { path: "questions", ...page(() => import("./features/pharmacist/Pharmacist"), "PharmacistQuestions") },
            { path: "followups", ...page(() => import("./features/pharmacist/Pharmacist"), "PharmacistFollowUps") },
            { path: "reportreviews", ...page(() => import("./features/records/ProPages"), "ReportReviews") },
            { path: "care", ...page(() => import("./features/records/ProPages"), "CareRequests") },
            { path: "rules", ...page(() => import("./features/safety/SafetyCheck"), "RulesPage") },
            { path: "patients/:id/safety", ...page(() => import("./features/safety/SafetyCheck"), "ProSafety") },
          { path: "drugs", ...page(() => import("./features/knowledge/DrugSearch"), "DrugSearch") },
          { path: "drugs/:id", ...page(() => import("./features/knowledge/DrugSearch"), "DrugDetail") },
        ],
      },
      {
        path: "pharmacy", element: guarded("pharmacy", "pharmacy"),
        children: [
            { index: true, ...page(() => import("./features/pharmacy/Pharmacy"), "PharmacyOverview") },
            { path: "inventory", ...page(() => import("./features/pharmacy/Pharmacy"), "PharmacyInventory") },
            { path: "prescriptions", ...page(() => import("./features/pharmacy/Pharmacy"), "PharmacyPrescriptions") },
            { path: "dispensing", ...page(() => import("./features/pharmacy/Pharmacy"), "PharmacyDispensing") },
            { path: "requests", ...page(() => import("./features/pharmacy/Pharmacy"), "PharmacyRequests") },
            { path: "alerts", ...page(() => import("./features/pharmacy/Pharmacy"), "PharmacyAlerts") },
          { path: "drugs", ...page(() => import("./features/knowledge/DrugSearch"), "DrugSearch") },
          { path: "drugs/:id", ...page(() => import("./features/knowledge/DrugSearch"), "DrugDetail") },
        ],
      },
      {
        path: "industry", element: guarded("industry", "industry"),
        children: [
            { index: true, ...page(() => import("./features/industry/Industry"), "IndustryOverview") },
            { path: "adr-trends", ...page(() => import("./features/industry/Industry"), "IndustryAdrTrends") },
            { path: "experience", ...page(() => import("./features/industry/Industry"), "IndustryExperience") },
            { path: "signals", ...page(() => import("./features/industry/Industry"), "IndustrySignals") },
            { path: "reports", ...page(() => import("./features/industry/Industry"), "IndustryReports") },
          { path: "drugs", ...page(() => import("./features/knowledge/DrugSearch"), "DrugSearch") },
          { path: "drugs/:id", ...page(() => import("./features/knowledge/DrugSearch"), "DrugDetail") },
        ],
      },
    ],
  },
  { path: "*", element: <NotFound /> },
];
