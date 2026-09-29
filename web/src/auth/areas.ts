import type { RoleName } from "./access.g";
import { permissions as P } from "./access.g";
import type { AuthUser } from "./types";

/** Application areas (route trees). Entry needs ONE permission that only the intended roles hold. UX only. */
export type Area = "patient" | "physician" | "pharmacist" | "pharmacy" | "industry" | "admin" | "workspace";

export const areaPermission: Record<Area, string> = {
  patient: P.patientCheckinsCreate,
  physician: P.prescriptionCreate,
  pharmacist: P.prescriptionReview,
  pharmacy: P.pharmacyInventoryRead,
  industry: P.analyticsRead,
  admin: P.roleManage,
  workspace: P.sessionRead,
};

/** Order = default landing priority for users holding several roles. */
export const areaOrder: Area[] = ["patient", "physician", "pharmacist", "pharmacy", "admin", "industry", "workspace"];

const has = (user: AuthUser, permission: string) => user.permissions.includes(permission);

export function canEnter(user: AuthUser | null, area: Area): boolean {
  if (!user) return false;
  if (area === "workspace") return user.roles.some((r: RoleName) => r === "ContentManager" || r === "AIManager");
  return has(user, areaPermission[area]);
}

export const areasOf = (user: AuthUser | null): Area[] => areaOrder.filter((a) => canEnter(user, a));

/** Where to send a user after sign-in (or when they hit a page they cannot open). */
export function homeFor(user: AuthUser | null): string {
  const first = areasOf(user)[0];
  return first ? `/app/${first}` : "/app/account";
}
