import type { ConsentPurpose, DataScope, RoleName } from "./access.g";

export interface AuthUser {
  id: string;
  displayName: string;
  email?: string;
  roles: RoleName[];
  permissions: string[];
  organizationIds: string[];
  /** Demo-data key of this user's own patient record (patients only). */
  subjectKey?: string;
}

export type SignInError = "invalid" | "disabled" | "noRole" | "throttled" | "network";
export type SignInResult = { ok: true; user: AuthUser } | { ok: false; error: SignInError };

export interface DemoAccount {
  id: string;
  displayName: Record<"en" | "fa", string>;
  description: Record<"en" | "fa", string>;
  roles: RoleName[];
  primary: boolean;
  disabled: boolean;
}

export interface SessionView { id: string; deviceName: string; platform: string; lastSeenAt: string; isCurrent: boolean; revoked: boolean }
export interface ConsentView {
  id: string;
  granteeName: string;
  purpose: ConsentPurpose;
  scope: DataScope[];
  expiresAt: string;
  status: "active" | "expired" | "revoked";
}
export interface AccessLogView { at: string; actorName: string; what: DataScope | "prescriptions" | "profile" }
export interface AdminUserView { id: string; displayName: string; roles: RoleName[]; status: string }
export interface AuditView { at: string; action: string; result: string; actorName: string }

/** Why the user is signed out (drives which screen the login route shows). */
export type SignedOutReason = "none" | "signedOut" | "expired" | "disabled";

/**
 * Everything the UI needs from the identity layer. Two implementations:
 *  - LocalMockBackend: in-browser fictional accounts (DEMO ONLY, no server involved)
 *  - ApiBackend: talks to the ASP.NET API (JWT + rotating refresh token); the server is the security boundary
 */
export interface AuthBackend {
  readonly kind: "local-mock" | "api";
  /** True when fictional demo accounts are offered (development builds only). */
  readonly demo: boolean;
  restore(): Promise<AuthUser | null>;
  signIn(accountId: string): Promise<SignInResult>;
  signOut(): Promise<void>;
  demoAccounts(): Promise<DemoAccount[]>;
  /** Called by the provider when the session dies on the server (401 after refresh failed). */
  onSessionLost(listener: () => void): () => void;

  sessions(): Promise<SessionView[]>;
  revokeSession(id: string): Promise<void>;
  signOutEverywhere(): Promise<void>;
  consents(): Promise<ConsentView[]>;
  revokeConsent(id: string): Promise<void>;
  accessLog(): Promise<AccessLogView[]>;
  adminUsers(): Promise<AdminUserView[]>;
  adminAssignRole(userId: string, role: RoleName): Promise<boolean>;
  adminRevokeRole(userId: string, role: RoleName): Promise<boolean>;
  adminAudit(): Promise<AuditView[]>;
  adminVerifyAudit(): Promise<boolean>;

  /** UX hint only ("can this user open this patient?"). The server re-checks on every real request. */
  canViewPatient(subjectKey: string, scope?: DataScope): Promise<boolean>;
  /** DEMO DEV: end the session as if it expired. */
  simulateExpiry?(): void;
  /** Authorised request to the API (the token never leaves the backend). Absent for the in-browser demo accounts. */
  apiFetch?(path: string, init?: RequestInit): Promise<Response>;
}
