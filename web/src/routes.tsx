import type { ComponentType } from "react";
import { Navigate, Outlet, useRouteError, type RouteObject } from "react-router-dom";
import { areasOf } from "./auth/areas";
import { useAuth } from "./auth/AuthContext";
import { AppIndexRedirect, RequireArea, RequireAuth } from "./auth/guards";
import { LoginPage, UnauthorizedPage } from "./features/auth/AuthPages";
import { Button, EmptyState, ErrorState } from "./components/ui";
import { useI18n } from "./i18n/I18nProvider";
import { AppShell } from "./layouts/AppShell";
import { SampleDataNotice, type RealPage } from "./features/SampleDataNotice";

/** Code-splits every page: `lazy` returns the named export as the route component. */
function page<K extends string>(loader: () => Promise<Record<K, ComponentType>>, name: K): Pick<RouteObject, "lazy"> {
  return { lazy: async () => ({ Component: (await loader())[name] }) };
}

/** Wraps a page that still runs on the in-browser sample data with the notice that says so (and points to the stored-data page for the same job, when there is one). */
function withSampleNotice(Page: ComponentType, real?: RealPage): ComponentType {
  return function SamplePage() {
    return <><SampleDataNotice real={real} /><Page /></>;
  };
}

function mockPage<K extends string>(loader: () => Promise<Record<K, ComponentType>>, name: K, real?: RealPage): Pick<RouteObject, "lazy"> {
  return { lazy: async () => ({ Component: withSampleNotice((await loader())[name], real) }) };
}

const toTaking: RealPage = { to: "/app/patient/taking", labelKey: "sample.real.taking" };
const toRecords: RealPage = { to: "/app/patient/records", labelKey: "sample.real.records" };

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
          { path: "drugs/:id", ...page(() => import("./features/knowledge/DrugSearch"), "DrugDetail") },
          { path: "rules", ...page(() => import("./features/safety/SafetyCheck"), "RulesPage") },
          { path: "rules/new", ...page(() => import("./features/safety/RulesAdmin"), "RuleEditorNew") },
          { path: "rules/:ruleId/:version", ...page(() => import("./features/safety/RulesAdmin"), "RuleDetailPage") },
          { path: "rules/:ruleId/:version/edit", ...page(() => import("./features/safety/RulesAdmin"), "RuleEditorEdit") },
        ],
      },
      {
        path: "patient", element: guarded("patient", "patient"),
        children: [
            { index: true, ...mockPage(P, "PatientHome", toTaking) },
            { path: "medications", ...mockPage(() => import("./features/patient/Medications"), "Medications", toTaking) },
            { path: "medications/:id", ...mockPage(() => import("./features/patient/Medications"), "MedicationDetail", toTaking) },
            { path: "assistant", ...page(() => import("./features/assistant/AssistantEntry"), "AssistantEntry") },
            { path: "assistant/prototype", ...mockPage(() => import("./features/assistant/Assistant"), "Assistant") },
            { path: "checkin", ...mockPage(() => import("./features/patient/CheckIn"), "CheckIn", toRecords) },
            { path: "profile", ...mockPage(() => import("./features/patient/Profile"), "Profile", toRecords) },
            { path: "records", ...page(() => import("./features/records/RecordsPage"), "PatientRecords") },
            { path: "taking", ...page(() => import("./features/records/TakingPage"), "PatientTaking") },
            { path: "batches", ...page(() => import("./features/records/BatchesPage"), "PatientBatches") },
            { path: "myreports", ...page(() => import("./features/records/ReportsPage"), "PatientReports") },
            { path: "sharing", ...page(() => import("./features/records/SharingPage"), "PatientSharing") },
            { path: "messages", ...page(() => import("./features/records/MessagesPage"), "PatientMessages") },
            { path: "safety", ...page(() => import("./features/safety/SafetyCheck"), "PatientSafety") },
            { path: "adverse-events", ...page(() => import("./features/adr/AdverseEvents"), "PatientAdverseEvents") },
            { path: "adverse-events/:rid", ...page(() => import("./features/adr/AdverseEvents"), "AdverseEventPage") },
          { path: "drugs", ...page(() => import("./features/knowledge/DrugSearch"), "DrugSearch") },
          { path: "drugs/:id", ...page(() => import("./features/knowledge/DrugSearch"), "DrugDetail") },
        ],
      },
      {
        path: "physician", element: guarded("physician", "physician"),
        children: [
            { index: true, ...mockPage(() => import("./features/physician/Physician"), "PhysicianDashboard") },
            { path: "patients", ...mockPage(() => import("./features/physician/Physician"), "PhysicianPatients") },
            { path: "patients/:id", ...mockPage(() => import("./features/physician/Physician"), "PatientOverview") },
            { path: "prescriptions", ...mockPage(() => import("./features/physician/Physician"), "PhysicianPrescriptions") },
            { path: "adr", element: <Navigate to="/app/physician/adverse-events" replace /> },
            { path: "reports", ...mockPage(() => import("./features/physician/Physician"), "PhysicianReports") },
            { path: "reportreviews", ...page(() => import("./features/records/ProPages"), "ReportReviews") },
            { path: "care", ...page(() => import("./features/records/ProPages"), "CareRequests") },
            { path: "rules", ...page(() => import("./features/safety/SafetyCheck"), "RulesPage") },
            { path: "rules/new", ...page(() => import("./features/safety/RulesAdmin"), "RuleEditorNew") },
            { path: "rules/:ruleId/:version", ...page(() => import("./features/safety/RulesAdmin"), "RuleDetailPage") },
            { path: "rules/:ruleId/:version/edit", ...page(() => import("./features/safety/RulesAdmin"), "RuleEditorEdit") },
            { path: "patients/:id/adherence", ...page(() => import("./features/records/Adherence"), "ProAdherence") },
            { path: "patients/:id/safety", ...page(() => import("./features/safety/SafetyCheck"), "ProSafety") },
            { path: "adverse-events", ...page(() => import("./features/adr/AdverseEvents"), "ProAdverseEventQueue") },
            { path: "adverse-events/:rid", ...page(() => import("./features/adr/AdverseEvents"), "AdverseEventPage") },
            { path: "patients/:id/adverse-events", ...page(() => import("./features/adr/AdverseEvents"), "ProPatientAdverseEvents") },
          { path: "drugs", ...page(() => import("./features/knowledge/DrugSearch"), "DrugSearch") },
          { path: "drugs/:id", ...page(() => import("./features/knowledge/DrugSearch"), "DrugDetail") },
        ],
      },
      {
        path: "pharmacist", element: guarded("pharmacist", "pharmacist"),
        children: [
            { index: true, ...mockPage(() => import("./features/pharmacist/Pharmacist"), "PharmacistDashboard") },
            { path: "reviews", ...mockPage(() => import("./features/pharmacist/Pharmacist"), "PharmacistReviews") },
            { path: "interactions", ...mockPage(() => import("./features/pharmacist/Pharmacist"), "PharmacistInteractions") },
            { path: "adr", element: <Navigate to="/app/pharmacist/adverse-events" replace /> },
            { path: "adherence", ...mockPage(() => import("./features/pharmacist/Pharmacist"), "PharmacistAdherence") },
            { path: "questions", ...mockPage(() => import("./features/pharmacist/Pharmacist"), "PharmacistQuestions") },
            { path: "followups", ...mockPage(() => import("./features/pharmacist/Pharmacist"), "PharmacistFollowUps") },
            { path: "reportreviews", ...page(() => import("./features/records/ProPages"), "ReportReviews") },
            { path: "care", ...page(() => import("./features/records/ProPages"), "CareRequests") },
            { path: "rules", ...page(() => import("./features/safety/SafetyCheck"), "RulesPage") },
            { path: "rules/new", ...page(() => import("./features/safety/RulesAdmin"), "RuleEditorNew") },
            { path: "rules/:ruleId/:version", ...page(() => import("./features/safety/RulesAdmin"), "RuleDetailPage") },
            { path: "rules/:ruleId/:version/edit", ...page(() => import("./features/safety/RulesAdmin"), "RuleEditorEdit") },
            { path: "patients/:id/adherence", ...page(() => import("./features/records/Adherence"), "ProAdherence") },
            { path: "patients/:id/safety", ...page(() => import("./features/safety/SafetyCheck"), "ProSafety") },
            { path: "adverse-events", ...page(() => import("./features/adr/AdverseEvents"), "ProAdverseEventQueue") },
            { path: "adverse-events/:rid", ...page(() => import("./features/adr/AdverseEvents"), "AdverseEventPage") },
            { path: "patients/:id/adverse-events", ...page(() => import("./features/adr/AdverseEvents"), "ProPatientAdverseEvents") },
            { path: "safety-log", ...page(() => import("./features/pharmacysafety/PharmacySafety"), "SafetyLogPage") },
            { path: "safety-log/:rid", ...page(() => import("./features/pharmacysafety/PharmacySafety"), "SafetyEventPage") },
            { path: "product-concerns", ...page(() => import("./features/pharmacysafety/PharmacySafety"), "ProductConcernsPage") },
            { path: "product-concerns/:rid", ...page(() => import("./features/pharmacysafety/PharmacySafety"), "ProductConcernPage") },
          { path: "drugs", ...page(() => import("./features/knowledge/DrugSearch"), "DrugSearch") },
          { path: "drugs/:id", ...page(() => import("./features/knowledge/DrugSearch"), "DrugDetail") },
        ],
      },
      {
        path: "pharmacy", element: guarded("pharmacy", "pharmacy"),
        children: [
            { index: true, ...mockPage(() => import("./features/pharmacy/Pharmacy"), "PharmacyOverview") },
            { path: "inventory", ...mockPage(() => import("./features/pharmacy/Pharmacy"), "PharmacyInventory") },
            { path: "prescriptions", ...mockPage(() => import("./features/pharmacy/Pharmacy"), "PharmacyPrescriptions") },
            { path: "dispensing", ...mockPage(() => import("./features/pharmacy/Pharmacy"), "PharmacyDispensing") },
            { path: "requests", ...mockPage(() => import("./features/pharmacy/Pharmacy"), "PharmacyRequests") },
            { path: "alerts", ...mockPage(() => import("./features/pharmacy/Pharmacy"), "PharmacyAlerts") },
            { path: "safety-log", ...page(() => import("./features/pharmacysafety/PharmacySafety"), "SafetyLogPage") },
            { path: "safety-log/:rid", ...page(() => import("./features/pharmacysafety/PharmacySafety"), "SafetyEventPage") },
            { path: "product-concerns", ...page(() => import("./features/pharmacysafety/PharmacySafety"), "ProductConcernsPage") },
            { path: "product-concerns/:rid", ...page(() => import("./features/pharmacysafety/PharmacySafety"), "ProductConcernPage") },
          { path: "drugs", ...page(() => import("./features/knowledge/DrugSearch"), "DrugSearch") },
          { path: "drugs/:id", ...page(() => import("./features/knowledge/DrugSearch"), "DrugDetail") },
        ],
      },
      {
        path: "industry", element: guarded("industry", "industry"),
        children: [
            { index: true, ...page(() => import("./features/industry/Industry"), "IndustryOverview") },
            { path: "insights", ...page(() => import("./features/industry/Insights"), "IndustryInsightsPage") },
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
