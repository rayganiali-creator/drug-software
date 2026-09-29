import { apiBaseUrl } from "../api";
import type { DataScope, RoleName } from "./access.g";
import { catalog, orgById, subjectByKey } from "./catalog";
import type {
  AccessLogView, AdminUserView, AuditView, AuthBackend, AuthUser, ConsentView, DemoAccount, SessionView, SignInError, SignInResult,
} from "./types";

const REFRESH_KEY = "ms.auth.refresh";
const DEVICE_KEY = "ms.auth.device";

interface ApiUser { id: string; displayName: string; email?: string | null; roles: RoleName[]; permissions: string[]; organizationIds: string[] }
interface ApiAuth { tokens: { accessToken: string; refreshToken: string }; user: ApiUser }

const scopeRoute: Record<DataScope, string> = {
  profile: "profile", medications: "medications", prescriptions: "prescriptions", adherence: "adherence",
  adr: "adr", checkins: "checkins", symptoms: "symptoms", ai_summary: "ai-summary",
};

export interface ApiBackendOptions {
  baseUrl?: string;
  fetchImpl?: typeof fetch;
  /** Offer the fictional demo accounts (server exposes them only in DevelopmentMock mode). */
  demo?: boolean;
  storage?: Pick<Storage, "getItem" | "setItem" | "removeItem"> | null;
}

/**
 * Talks to the ASP.NET API. The access token lives ONLY in memory; the rotating refresh token lives in
 * sessionStorage (tab-scoped) so a reload can restore the session. No token is ever written to localStorage or logged.
 * Every authorization decision is made by the server: this class only carries credentials and maps responses.
 */
export class ApiBackend implements AuthBackend {
  readonly kind = "api" as const;
  readonly demo: boolean;
  private readonly base: string;
  private readonly doFetch: typeof fetch;
  private readonly session: NonNullable<ApiBackendOptions["storage"]> | null;
  private readonly local: NonNullable<ApiBackendOptions["storage"]> | null;
  private access: string | null = null;
  private refreshing: Promise<boolean> | null = null;
  private listeners = new Set<() => void>();
  private userCache: AuthUser | null = null;

  constructor(opts: ApiBackendOptions = {}) {
    this.base = opts.baseUrl ?? apiBaseUrl;
    this.doFetch = opts.fetchImpl ?? ((...a) => fetch(...a));
    this.demo = opts.demo ?? true;
    this.session = opts.storage === undefined ? pick("sessionStorage") : opts.storage;
    this.local = opts.storage === undefined ? pick("localStorage") : opts.storage;
  }

  private deviceId(): string | undefined {
    try {
      let id = this.local?.getItem(DEVICE_KEY);
      if (!id) { id = crypto.randomUUID(); this.local?.setItem(DEVICE_KEY, id); }
      return id;
    } catch { return undefined; }
  }

  private toUser(u: ApiUser): AuthUser {
    return { id: u.id, displayName: u.displayName, email: u.email ?? undefined, roles: u.roles, permissions: u.permissions, organizationIds: u.organizationIds, subjectKey: catalog.subjects.find((s) => s.userId === u.id)?.key };
  }

  private store(auth: ApiAuth): AuthUser {
    this.access = auth.tokens.accessToken;
    try { this.session?.setItem(REFRESH_KEY, auth.tokens.refreshToken); } catch { /* memory only */ }
    this.userCache = this.toUser(auth.user);
    return this.userCache;
  }

  private clear() {
    this.access = null;
    this.userCache = null;
    try { this.session?.removeItem(REFRESH_KEY); } catch { /* ignore */ }
  }

  /** fetch with Authorization; on 401 tries ONE refresh, then retries, otherwise reports the lost session. */
  private async request(path: string, init: RequestInit = {}, retry = true): Promise<Response> {
    const headers = new Headers(init.headers);
    headers.set("Accept", "application/json");
    headers.set("X-Client", "web");
    if (init.body) headers.set("Content-Type", "application/json");
    if (this.access) headers.set("Authorization", `Bearer ${this.access}`);
    const res = await this.doFetch(`${this.base}${path}`, { ...init, headers, credentials: "omit" });
    if (res.status === 401 && retry && this.access) {
      if (await this.refresh()) return this.request(path, init, false);
      this.clear();
      this.listeners.forEach((l) => l());
    }
    return res;
  }

  private refresh(): Promise<boolean> {
    this.refreshing ??= (async () => {
      try {
        const token = this.session?.getItem(REFRESH_KEY);
        if (!token) return false;
        const res = await this.doFetch(`${this.base}/auth/refresh`, { method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json" }, body: JSON.stringify({ refreshToken: token }), credentials: "omit" });
        if (!res.ok) return false;
        this.store((await res.json()) as ApiAuth);
        return true;
      } catch { return false; } finally { this.refreshing = null; }
    })();
    return this.refreshing;
  }

  async restore(): Promise<AuthUser | null> {
    if (!(await this.refresh())) { this.clear(); return null; }
    const me = await this.request("/auth/me");
    if (!me.ok) { this.clear(); return null; }
    this.userCache = this.toUser((await me.json()) as ApiUser);
    return this.userCache;
  }

  async signIn(accountId: string): Promise<SignInResult> {
    let res: Response;
    try {
      res = await this.doFetch(`${this.base}/auth/login`, {
        method: "POST", credentials: "omit",
        headers: { "Content-Type": "application/json", Accept: "application/json", "X-Client": "web" },
        body: JSON.stringify({ provider: "mock", credentials: { accountId }, device: { deviceId: this.deviceId(), platform: "web", appVersion: "0.1.0", deviceName: browserName() } }),
      });
    } catch { return { ok: false, error: "network" }; }
    if (res.ok) return { ok: true, user: this.store((await res.json()) as ApiAuth) };
    const code = ((await res.json().catch(() => ({}))) as { code?: string }).code;
    const error: SignInError = code === "account_disabled" ? "disabled" : code === "no_active_role" ? "noRole" : res.status === 429 ? "throttled" : "invalid";
    return { ok: false, error };
  }

  async signOut(): Promise<void> {
    try { if (this.access) await this.request("/auth/logout", { method: "POST" }, false); } catch { /* best effort: the local state is cleared anyway */ }
    this.clear();
  }

  async demoAccounts(): Promise<DemoAccount[]> {
    const res = await this.doFetch(`${this.base}/auth/demo-accounts`, { headers: { Accept: "application/json" }, credentials: "omit" });
    if (!res.ok) return [];
    const body = (await res.json()) as { accounts: { id: string; displayName: DemoAccount["displayName"]; description: DemoAccount["description"]; roles: RoleName[]; primary: boolean; status: string }[] };
    return body.accounts.map((a) => ({ ...a, disabled: a.status === "Disabled" }));
  }

  onSessionLost(listener: () => void) { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; }

  async sessions(): Promise<SessionView[]> {
    const res = await this.request("/sessions");
    if (!res.ok) return [];
    return ((await res.json()) as { id: string; deviceName: string; platform: string; lastSeenAt: string; isCurrent: boolean; revokedAt: string | null }[])
      .filter((s) => !s.revokedAt).map((s) => ({ ...s, revoked: false }));
  }
  async revokeSession(id: string): Promise<void> { await this.request(`/sessions/${encodeURIComponent(id)}`, { method: "DELETE" }); }
  async signOutEverywhere(): Promise<void> {
    await this.request("/auth/logout-all", { method: "POST" }, false).catch(() => undefined);
    this.clear();
    this.listeners.forEach((l) => l());
  }

  async consents(): Promise<ConsentView[]> {
    const res = await this.request("/consents");
    if (!res.ok) return [];
    type Row = { consent: { id: string; granteeOrganizationId?: string | null; purpose: ConsentView["purpose"]; scope: DataScope[]; expiresAt: string; status: string }; granteeName: string | null };
    return ((await res.json()) as Row[]).map((r) => ({
      id: r.consent.id, purpose: r.consent.purpose, scope: r.consent.scope, expiresAt: r.consent.expiresAt,
      status: r.consent.status.toLowerCase() as ConsentView["status"], granteeName: r.granteeName ?? orgById(r.consent.granteeOrganizationId ?? "")?.name ?? "Organization",
    }));
  }
  async revokeConsent(id: string): Promise<void> { await this.request(`/consents/${encodeURIComponent(id)}`, { method: "DELETE" }); }

  async accessLog(): Promise<AccessLogView[]> {
    const res = await this.request("/audit/me");
    if (!res.ok) return [];
    return ((await res.json()) as { timestamp: string; actorName: string | null; resourceType: string | null }[])
      .map((e) => ({ at: e.timestamp, actorName: e.actorName ?? "—", what: ((e.resourceType ?? "profile").replace("-", "_") as AccessLogView["what"]) }));
  }

  async adminUsers(): Promise<AdminUserView[]> {
    const res = await this.request("/admin/users");
    return res.ok ? ((await res.json()) as AdminUserView[]) : [];
  }
  async adminAssignRole(userId: string, role: RoleName): Promise<boolean> {
    return (await this.request(`/admin/users/${encodeURIComponent(userId)}/roles`, { method: "POST", body: JSON.stringify({ role }) })).status === 204;
  }
  async adminRevokeRole(userId: string, role: RoleName): Promise<boolean> {
    return (await this.request(`/admin/users/${encodeURIComponent(userId)}/roles/${encodeURIComponent(role)}`, { method: "DELETE" })).status === 204;
  }
  async adminAudit(): Promise<AuditView[]> {
    const [audit, users] = await Promise.all([this.request("/audit?take=100"), this.adminUsers()]);
    if (!audit.ok) return [];
    const names = new Map(users.map((u) => [u.id, u.displayName]));
    return ((await audit.json()) as { timestamp: string; action: string; result: string; actorUserId: string | null }[])
      .map((e) => ({ at: e.timestamp, action: e.action, result: e.result, actorName: (e.actorUserId && names.get(e.actorUserId)) || "—" }));
  }
  async adminVerifyAudit(): Promise<boolean> {
    const res = await this.request("/audit/verify");
    return res.ok && ((await res.json()) as { intact: boolean }).intact;
  }

  async canViewPatient(subjectKey: string, scope: DataScope = "profile"): Promise<boolean> {
    const subject = subjectByKey(subjectKey);
    if (!subject) return false;
    if (this.userCache?.id === subject.userId) return true;
    const org = this.userCache?.organizationIds[0];
    if (scope === "prescriptions" && org && this.userCache?.permissions.includes("pharmacy.prescriptions.read") && !this.userCache.permissions.includes("patient.profile.read")) {
      try { return (await this.request(`/organizations/${org}/patients/${subject.userId}/prescriptions`)).ok; } catch { return false; }
    }
    try { return (await this.request(`/patients/${subject.userId}/${scopeRoute[scope]}`)).ok; } catch { return false; }
  }
}

function pick(name: "localStorage" | "sessionStorage"): Storage | null {
  try { return typeof window === "undefined" ? null : window[name]; } catch { return null; }
}

function browserName(): string {
  const ua = typeof navigator === "undefined" ? "" : navigator.userAgent;
  const browser = /Edg\//.test(ua) ? "Edge" : /Chrome\//.test(ua) ? "Chrome" : /Firefox\//.test(ua) ? "Firefox" : /Safari\//.test(ua) ? "Safari" : "Browser";
  return `${browser} on web`;
}
