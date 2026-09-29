import type { ConsentPurpose, DataScope, RoleName } from "./access.g";
import { accountById, accountByUserId, catalog, orgByKey, permissionsOf, subjectByAccount, subjectByKey } from "./catalog";
import type {
  AccessLogView, AdminUserView, AuditView, AuthBackend, AuthUser, ConsentView, DemoAccount, SessionView, SignInResult,
} from "./types";

const STORAGE_KEY = "ms.auth.demo-session";
const SESSION_HOURS = 8;

interface StoredSession { accountId: string; sessionId: string; expiresAt: number }
interface Consent {
  id: string; subjectAccountId: string; granteeAccountId: string | null; granteeOrganizationKey: string | null;
  purpose: ConsentPurpose; scope: DataScope[]; expiresAt: number; revokedAt: number | null;
}

export interface LocalMockOptions {
  now?: () => number;
  storage?: Pick<Storage, "getItem" | "setItem" | "removeItem"> | null;
  /** Start already signed in (tests). */
  initialAccountId?: string;
}

/**
 * DEMO ONLY. Fictional accounts from the shared catalog, kept in the browser. There are no passwords and nothing
 * is sent anywhere. It mirrors the server rules (roles, relationships, consent) so the UI can be explored without
 * a backend, but the real boundary is the API (see ApiBackend). Never ship this as a production login.
 */
export class LocalMockBackend implements AuthBackend {
  readonly kind = "local-mock" as const;
  readonly demo = true;
  private readonly now: () => number;
  private readonly storage: LocalMockOptions["storage"];
  private session: StoredSession | null = null;
  private listeners = new Set<() => void>();
  private consentState: Consent[];
  private roleOverrides = new Map<string, RoleName[]>();
  private audit: AuditView[] = [];
  private disabled = new Set<string>(catalog.demoAccounts.filter((a) => a.status === "Disabled").map((a) => a.id));

  constructor(opts: LocalMockOptions = {}) {
    this.now = opts.now ?? (() => Date.now());
    this.storage = opts.storage === undefined ? safeStorage() : opts.storage;
    const t = this.now();
    this.consentState = catalog.consents.map((c, i) => ({
      id: `consent-${i + 1}`, subjectAccountId: c.subjectAccountId, granteeAccountId: c.granteeAccountId, granteeOrganizationKey: c.granteeOrganizationKey,
      purpose: c.purpose, scope: c.scope, expiresAt: t + c.expiresInDays * 86_400_000, revokedAt: null,
    }));
    if (opts.initialAccountId) this.session = { accountId: opts.initialAccountId, sessionId: crypto.randomUUID(), expiresAt: t + SESSION_HOURS * 3_600_000 };
    else this.load();
  }

  private load() {
    try {
      const raw = this.storage?.getItem(STORAGE_KEY);
      if (raw) this.session = JSON.parse(raw) as StoredSession;
    } catch { this.session = null; }
  }

  private save() {
    try {
      if (this.session) this.storage?.setItem(STORAGE_KEY, JSON.stringify(this.session));
      else this.storage?.removeItem(STORAGE_KEY);
    } catch { /* storage unavailable: session lives in memory only */ }
  }

  private rolesOf(accountId: string): RoleName[] {
    return this.roleOverrides.get(accountId) ?? [...new Set(accountById(accountId)?.roles.map((r) => r.role) ?? [])];
  }

  private userFor(accountId: string): AuthUser | null {
    const a = accountById(accountId);
    if (!a) return null;
    const roles = this.rolesOf(accountId);
    if (roles.length === 0) return null;
    return {
      id: a.userId, displayName: a.displayName.en, email: a.email ?? undefined, roles, permissions: permissionsOf(roles),
      organizationIds: a.organizations, subjectKey: subjectByAccount(accountId)?.key,
    };
  }

  /** Resolves the signed-in user on EVERY call (role/disable changes apply immediately, like the server). */
  private current(): { user: AuthUser; account: string } | null {
    const s = this.session;
    if (!s) return null;
    if (this.now() >= s.expiresAt || this.disabled.has(s.accountId)) return null;
    const user = this.userFor(s.accountId);
    return user ? { user, account: s.accountId } : null;
  }

  async restore(): Promise<AuthUser | null> {
    const c = this.current();
    if (!c && this.session) { this.session = null; this.save(); }
    return c?.user ?? null;
  }

  async signIn(accountId: string): Promise<SignInResult> {
    const a = accountById(accountId);
    if (!a || !a.loginEnabled) return { ok: false, error: "invalid" };
    if (this.disabled.has(accountId)) return { ok: false, error: "disabled" };
    const user = this.userFor(accountId);
    if (!user) return { ok: false, error: "noRole" };
    this.session = { accountId, sessionId: crypto.randomUUID(), expiresAt: this.now() + SESSION_HOURS * 3_600_000 };
    this.save();
    this.log("LOGIN", "Success", accountId);
    return { ok: true, user };
  }

  async signOut(): Promise<void> {
    if (this.session) this.log("LOGOUT", "Success", this.session.accountId);
    this.session = null;
    this.save();
  }

  async demoAccounts(): Promise<DemoAccount[]> {
    return catalog.demoAccounts.filter((a) => a.loginEnabled).map((a) => ({
      id: a.id, displayName: a.displayName, description: a.description, roles: [...new Set(a.roles.map((r) => r.role))], primary: a.primary, disabled: a.status === "Disabled",
    }));
  }

  onSessionLost(listener: () => void) { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; }

  simulateExpiry() {
    if (!this.session) return;
    this.session = { ...this.session, expiresAt: this.now() - 1 };
    this.save();
    this.listeners.forEach((l) => l());
  }

  // ---- account & security ----
  async sessions(): Promise<SessionView[]> {
    const c = this.current();
    if (!c || !this.session) return [];
    return [
      { id: this.session.sessionId, deviceName: "This browser (demo)", platform: "web", lastSeenAt: new Date(this.now()).toISOString(), isCurrent: true, revoked: false },
      { id: "demo-other-1", deviceName: "Demo phone (fictional)", platform: "android", lastSeenAt: new Date(this.now() - 3 * 86_400_000).toISOString(), isCurrent: false, revoked: false },
    ];
  }
  async revokeSession(): Promise<void> { /* the fictional second device is simulated */ }
  async signOutEverywhere(): Promise<void> { await this.signOut(); this.listeners.forEach((l) => l()); }

  private statusOf(c: Consent): ConsentView["status"] {
    return c.revokedAt !== null ? "revoked" : this.now() >= c.expiresAt ? "expired" : "active";
  }

  async consents(): Promise<ConsentView[]> {
    const c = this.current();
    if (!c) return [];
    return this.consentState.filter((x) => x.subjectAccountId === c.account).map((x) => ({
      id: x.id, purpose: x.purpose, scope: x.scope, expiresAt: new Date(x.expiresAt).toISOString(), status: this.statusOf(x),
      granteeName: x.granteeAccountId ? (accountById(x.granteeAccountId)?.displayName.en ?? "?") : (orgByKey(x.granteeOrganizationKey ?? "")?.name ?? "?"),
    }));
  }

  async revokeConsent(id: string): Promise<void> {
    const c = this.current();
    const item = this.consentState.find((x) => x.id === id);
    if (!c || !item || item.subjectAccountId !== c.account) return; // only the subject can revoke
    item.revokedAt ??= this.now();
    this.log("CONSENT_REVOKED", "Success", c.account);
  }

  async accessLog(): Promise<AccessLogView[]> {
    const c = this.current();
    if (!c) return [];
    return this.consentState
      .filter((x) => x.subjectAccountId === c.account && x.granteeAccountId)
      .map((x, i) => ({
        at: new Date(this.now() - (i + 1) * 36 * 3_600_000).toISOString(),
        actorName: accountById(x.granteeAccountId!)?.displayName.en ?? "?",
        what: (x.scope.includes("medications") ? "medications" : x.scope[0] ?? "profile") as AccessLogView["what"],
      }));
  }

  // ---- administration (SystemAdmin only) ----
  private requireAdmin(): boolean { return this.current()?.user.permissions.includes("role.manage") ?? false; }

  async adminUsers(): Promise<AdminUserView[]> {
    if (!this.current()?.user.permissions.includes("user.read")) return [];
    return catalog.demoAccounts.map((a) => ({ id: a.userId, displayName: a.displayName.en, roles: this.rolesOf(a.id), status: this.disabled.has(a.id) ? "Disabled" : "Active" }));
  }

  async adminAssignRole(userId: string, role: RoleName): Promise<boolean> {
    const acc = accountByUserId(userId);
    if (!this.requireAdmin() || !acc) return false;
    const roles = this.rolesOf(acc.id);
    if (roles.includes(role)) return false;
    this.roleOverrides.set(acc.id, [...roles, role]);
    this.log("ROLE_ASSIGNED", "Success", this.current()!.account);
    return true;
  }

  async adminRevokeRole(userId: string, role: RoleName): Promise<boolean> {
    const acc = accountByUserId(userId);
    if (!this.requireAdmin() || !acc) return false;
    const roles = this.rolesOf(acc.id);
    if (!roles.includes(role)) return false;
    if (role === "SystemAdmin" && catalog.demoAccounts.filter((a) => this.rolesOf(a.id).includes("SystemAdmin")).length <= 1) return false;
    this.roleOverrides.set(acc.id, roles.filter((r) => r !== role));
    this.log("ROLE_REVOKED", "Success", this.current()!.account);
    return true;
  }

  async adminAudit(): Promise<AuditView[]> {
    return this.current()?.user.permissions.includes("audit.read") ? [...this.audit].reverse().slice(0, 100) : [];
  }
  async adminVerifyAudit(): Promise<boolean> { return true; }

  private log(action: string, result: string, accountId: string) {
    this.audit.push({ at: new Date(this.now()).toISOString(), action, result, actorName: accountById(accountId)?.displayName.en ?? "?" });
  }

  // ---- resource-level UX hints (mirror of the server rules) ----
  async canViewPatient(subjectKey: string, scope: DataScope = "profile"): Promise<boolean> {
    const c = this.current();
    const subject = subjectByKey(subjectKey);
    if (!c || !subject) return false;
    if (c.account === subject.accountId) return true;
    // Organization route (pharmacy staff): the organization's own care relationship and consent, prescriptions only.
    if (scope === "prescriptions" && c.user.permissions.includes("pharmacy.prescriptions.read") && !c.user.permissions.includes("patient.profile.read")) {
      return c.user.organizationIds.some((org) => catalog.careRelationships.some((r) => r.patientAccountId === subject.accountId && orgByKey(r.providerOrganizationKey ?? "")?.id === org)
        && this.consentState.some((x) => x.subjectAccountId === subject.accountId && this.statusOf(x) === "active" && x.scope.includes("prescriptions") && orgByKey(x.granteeOrganizationKey ?? "")?.id === org));
    }
    if (!c.user.permissions.includes("patient.profile.read")) return false;
    const related = catalog.careRelationships.some((r) => r.patientAccountId === subject.accountId
      && (r.providerAccountId === c.account || (r.providerOrganizationKey && c.user.organizationIds.includes(orgByKey(r.providerOrganizationKey)?.id ?? ""))));
    if (!related) return false;
    return this.consentState.some((x) => x.subjectAccountId === subject.accountId && this.statusOf(x) === "active" && x.scope.includes(scope)
      && (x.granteeAccountId === c.account || (x.granteeOrganizationKey && c.user.organizationIds.includes(orgByKey(x.granteeOrganizationKey)?.id ?? ""))));
  }
}

function safeStorage(): Storage | null {
  try { return typeof localStorage === "undefined" ? null : localStorage; } catch { return null; }
}
