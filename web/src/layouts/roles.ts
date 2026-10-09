import type { IconName } from "../components/ui";

export type Role = "patient" | "physician" | "pharmacist" | "pharmacy" | "industry" | "admin" | "workspace";
export const roles: Role[] = ["patient", "physician", "pharmacist", "pharmacy", "industry", "admin", "workspace"];
export const roleIcon: Record<Role, IconName> = { patient: "heart", physician: "stethoscope", pharmacist: "pill", pharmacy: "store", industry: "building", admin: "shield", workspace: "grid" };

export interface NavItem { id: string; path: string; icon: IconName; labelKey: string; end?: boolean }

const n = (id: string, icon: IconName, end = false): NavItem => ({ id, path: id === "home" ? "" : id, icon, labelKey: `nav.${id}`, end });

/** Information architecture per role (see docs/phase2/02-navigation-and-page-map.md). */
export const navByRole: Record<Role, NavItem[]> = {
  patient: [
    { ...n("home", "home", true), labelKey: "nav.home" },
    n("medications", "pill"),
    n("assistant", "sparkles"),
    n("checkin", "checkCircle"),
    n("profile", "user"),
    n("records", "clipboard"),
    n("taking", "pill"),
    n("batches", "box"),
    n("myreports", "fileText"),
    n("sharing", "shield"),
    n("messages", "message"),
    n("drugs", "bookOpen"),
  ],
  physician: [
    { ...n("home", "grid", true), labelKey: "nav.dashboard" },
    n("patients", "users"),
    n("prescriptions", "clipboard"),
    n("adr", "alertTriangle"),
    n("reports", "fileText"),
    n("reportreviews", "inbox"),
    n("care", "users"),
    n("drugs", "bookOpen"),
  ],
  pharmacist: [
    { ...n("home", "grid", true), labelKey: "nav.workQueue" },
    n("reviews", "clipboard"),
    n("interactions", "flask"),
    n("adr", "alertTriangle"),
    n("adherence", "activity"),
    n("questions", "message"),
    n("followups", "calendar"),
    n("reportreviews", "inbox"),
    n("care", "users"),
    n("drugs", "bookOpen"),
  ],
  pharmacy: [
    { ...n("home", "grid", true), labelKey: "nav.overview" },
    n("inventory", "box"),
    n("prescriptions", "clipboard"),
    n("dispensing", "truck"),
    n("requests", "inbox"),
    n("alerts", "bell"),
    n("drugs", "bookOpen"),
  ],
  admin: [{ ...n("home", "shield", true), labelKey: "role.admin" }, n("queue", "truck")],
  workspace: [{ ...n("home", "grid", true), labelKey: "role.workspace" }, n("drugs", "bookOpen")],
  industry: [
    { ...n("home", "chartBar", true), labelKey: "nav.analytics" },
    n("adr-trends", "trendUp"),
    n("experience", "heart"),
    n("signals", "activity"),
    n("reports", "fileText"),
    n("drugs", "bookOpen"),
  ],
};

export const isRole = (v: string | undefined): v is Role => !!v && (roles as string[]).includes(v);
