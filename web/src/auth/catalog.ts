import raw from "../shared/access-catalog.json";
import type { ConsentPurpose, DataScope, RoleName } from "./access.g";

// The catalog is generated from security/access-catalog.json (same file the backend embeds).
// It holds ONLY fictional demo identities and the role→permission mapping. No credentials.

export interface CatalogRole { name: RoleName; description: string; permissions: string[] }
export interface CatalogAccount {
  id: string;
  userId: string;
  displayName: Record<"en" | "fa", string>;
  email: string | null;
  roles: { role: RoleName; organizationId: string | null }[];
  organizations: string[];
  status: "Active" | "Disabled";
  loginEnabled: boolean;
  primary: boolean;
  description: Record<"en" | "fa", string>;
}
export interface CatalogOrganization { id: string; key: string; name: string; type: string }
export interface CatalogSubject { key: string; accountId: string; userId: string }
export interface CatalogRelationship { kind: "Treating" | "Dispensing"; patientAccountId: string; providerAccountId: string | null; providerOrganizationKey: string | null }
export interface CatalogConsent {
  subjectAccountId: string; granteeAccountId: string | null; granteeOrganizationKey: string | null;
  purpose: ConsentPurpose; scope: DataScope[]; grantedDaysAgo: number; expiresInDays: number; version: string;
}

interface CatalogFile {
  roles: CatalogRole[];
  organizations: CatalogOrganization[];
  demoAccounts: CatalogAccount[];
  subjects: CatalogSubject[];
  careRelationships: CatalogRelationship[];
  consents: CatalogConsent[];
}

export const catalog = raw as unknown as CatalogFile;

export function permissionsOf(roles: readonly RoleName[]): string[] {
  const set = new Set<string>();
  for (const r of roles) for (const p of catalog.roles.find((x) => x.name === r)?.permissions ?? []) set.add(p);
  return [...set].sort();
}

export const orgById = (id: string) => catalog.organizations.find((o) => o.id === id);
export const orgByKey = (key: string) => catalog.organizations.find((o) => o.key === key);
export const accountById = (id: string) => catalog.demoAccounts.find((a) => a.id === id);
export const accountByUserId = (userId: string) => catalog.demoAccounts.find((a) => a.userId === userId);
export const subjectByKey = (key: string) => catalog.subjects.find((s) => s.key === key);
export const subjectByAccount = (accountId: string) => catalog.subjects.find((s) => s.accountId === accountId);
