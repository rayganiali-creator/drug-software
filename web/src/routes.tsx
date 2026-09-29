import type { ComponentType } from "react";
import { Navigate, useRouteError, type RouteObject } from "react-router-dom";
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

const P = () => import("./features/patient/PatientHome");
export const routes: RouteObject[] = [
  { path: "/", ...page(() => import("./features/landing/Landing"), "Landing"), errorElement: <RouteError /> },
  { path: "/design-system", ...page(() => import("./features/designSystem/DesignSystem"), "DesignSystem"), errorElement: <RouteError /> },
  { path: "/app", element: <Navigate to="/app/patient" replace /> },
  {
    path: "/app/patient", element: <AppShell role="patient" />, errorElement: <RouteError />,
    children: [
      { index: true, ...page(P, "PatientHome") },
      { path: "medications", ...page(() => import("./features/patient/Medications"), "Medications") },
      { path: "medications/:id", ...page(() => import("./features/patient/Medications"), "MedicationDetail") },
      { path: "assistant", ...page(() => import("./features/assistant/Assistant"), "Assistant") },
      { path: "checkin", ...page(() => import("./features/patient/CheckIn"), "CheckIn") },
      { path: "profile", ...page(() => import("./features/patient/Profile"), "Profile") },
    ],
  },
  {
    path: "/app/physician", element: <AppShell role="physician" />, errorElement: <RouteError />,
    children: [
      { index: true, ...page(() => import("./features/physician/Physician"), "PhysicianDashboard") },
      { path: "patients", ...page(() => import("./features/physician/Physician"), "PhysicianPatients") },
      { path: "patients/:id", ...page(() => import("./features/physician/Physician"), "PatientOverview") },
      { path: "prescriptions", ...page(() => import("./features/physician/Physician"), "PhysicianPrescriptions") },
      { path: "adr", ...page(() => import("./features/physician/Physician"), "PhysicianAdr") },
      { path: "reports", ...page(() => import("./features/physician/Physician"), "PhysicianReports") },
    ],
  },
  {
    path: "/app/pharmacist", element: <AppShell role="pharmacist" />, errorElement: <RouteError />,
    children: [
      { index: true, ...page(() => import("./features/pharmacist/Pharmacist"), "PharmacistDashboard") },
      { path: "reviews", ...page(() => import("./features/pharmacist/Pharmacist"), "PharmacistReviews") },
      { path: "interactions", ...page(() => import("./features/pharmacist/Pharmacist"), "PharmacistInteractions") },
      { path: "adr", ...page(() => import("./features/pharmacist/Pharmacist"), "PharmacistAdr") },
      { path: "adherence", ...page(() => import("./features/pharmacist/Pharmacist"), "PharmacistAdherence") },
      { path: "questions", ...page(() => import("./features/pharmacist/Pharmacist"), "PharmacistQuestions") },
      { path: "followups", ...page(() => import("./features/pharmacist/Pharmacist"), "PharmacistFollowUps") },
    ],
  },
  {
    path: "/app/pharmacy", element: <AppShell role="pharmacy" />, errorElement: <RouteError />,
    children: [
      { index: true, ...page(() => import("./features/pharmacy/Pharmacy"), "PharmacyOverview") },
      { path: "inventory", ...page(() => import("./features/pharmacy/Pharmacy"), "PharmacyInventory") },
      { path: "prescriptions", ...page(() => import("./features/pharmacy/Pharmacy"), "PharmacyPrescriptions") },
      { path: "dispensing", ...page(() => import("./features/pharmacy/Pharmacy"), "PharmacyDispensing") },
      { path: "requests", ...page(() => import("./features/pharmacy/Pharmacy"), "PharmacyRequests") },
      { path: "alerts", ...page(() => import("./features/pharmacy/Pharmacy"), "PharmacyAlerts") },
    ],
  },
  {
    path: "/app/industry", element: <AppShell role="industry" />, errorElement: <RouteError />,
    children: [
      { index: true, ...page(() => import("./features/industry/Industry"), "IndustryOverview") },
      { path: "adr-trends", ...page(() => import("./features/industry/Industry"), "IndustryAdrTrends") },
      { path: "experience", ...page(() => import("./features/industry/Industry"), "IndustryExperience") },
      { path: "signals", ...page(() => import("./features/industry/Industry"), "IndustrySignals") },
      { path: "reports", ...page(() => import("./features/industry/Industry"), "IndustryReports") },
    ],
  },
  { path: "*", element: <NotFound /> },
];
