import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { LoadingState } from "../components/ui";
import { useI18n } from "../i18n/I18nProvider";
import { AppShell } from "../layouts/AppShell";
import { areasOf, canEnter, homeFor, type Area } from "./areas";
import { useAuth } from "./AuthContext";
import { UnauthorizedPage } from "../features/auth/AuthPages";

/** Redirects anonymous visitors to /login and remembers where they wanted to go. UX only: the API re-checks every call. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { status } = useAuth();
  const loc = useLocation();
  const { t } = useI18n();
  if (status === "loading") return <div style={{ padding: "var(--space-8) var(--space-4)", maxInlineSize: 520, marginInline: "auto" }}><LoadingState label={t("auth.checking")} rows={1} /></div>;
  if (status === "anonymous") return <Navigate to="/login" replace state={{ next: loc.pathname + loc.search }} />;
  return <>{children}</>;
}

/** Renders the area only if the user's permissions allow it, otherwise the neutral "no access" page (deny by default). */
export function RequireArea({ area, children }: { area: Area; children: ReactNode }) {
  const { user } = useAuth();
  // Not allowed: keep the user's own shell (menu, sign-out) around a neutral message instead of a dead end.
  return canEnter(user, area) ? <>{children}</> : <AppShell role={areasOf(user)[0] ?? "workspace"} content={<UnauthorizedPage />} />;
}

export function RequirePermission({ permission, children }: { permission: string; children: ReactNode }) {
  const { can } = useAuth();
  return can(permission) ? <>{children}</> : <UnauthorizedPage />;
}

/** /app → the signed-in user's own home. */
export function AppIndexRedirect() {
  const { user } = useAuth();
  return <Navigate to={homeFor(user)} replace />;
}
