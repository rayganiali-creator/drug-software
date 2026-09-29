import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { ApiBackend } from "./apiBackend";
import { LocalMockBackend } from "./localMockBackend";
import type { AuthBackend, AuthUser, SignInResult, SignedOutReason } from "./types";

export type AuthStatus = "loading" | "authenticated" | "anonymous";

export interface AuthContextValue {
  status: AuthStatus;
  user: AuthUser | null;
  /** Why the user is anonymous (expired session vs. explicit sign-out). */
  reason: SignedOutReason;
  backend: AuthBackend;
  signIn(accountId: string): Promise<SignInResult>;
  signOut(): Promise<void>;
  /** UX-only permission test. The server enforces the same rules on every request. */
  can(permission: string): boolean;
  hasRole(role: string): boolean;
}

const Ctx = createContext<AuthContextValue | null>(null);

/** Which backend to use: VITE_AUTH_MODE=api talks to the API; anything else (default) uses the in-browser demo accounts. */
export function createBackendFromEnv(): AuthBackend {
  return import.meta.env.VITE_AUTH_MODE === "api" ? new ApiBackend() : new LocalMockBackend();
}

export function AuthProvider({ children, backend }: { children: ReactNode; backend?: AuthBackend }) {
  const be = useMemo(() => backend ?? createBackendFromEnv(), [backend]);
  const [state, setState] = useState<{ status: AuthStatus; user: AuthUser | null; reason: SignedOutReason }>({ status: "loading", user: null, reason: "none" });
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    void be.restore().then((user) => { if (alive.current) setState(user ? { status: "authenticated", user, reason: "none" } : { status: "anonymous", user: null, reason: "none" }); });
    const off = be.onSessionLost(() => { if (alive.current) setState({ status: "anonymous", user: null, reason: "expired" }); });
    return () => { alive.current = false; off(); };
  }, [be]);

  const signIn = useCallback(async (accountId: string) => {
    const r = await be.signIn(accountId);
    if (r.ok) setState({ status: "authenticated", user: r.user, reason: "none" });
    else if (r.error === "disabled") setState({ status: "anonymous", user: null, reason: "disabled" });
    return r;
  }, [be]);

  const signOut = useCallback(async () => {
    await be.signOut();
    setState({ status: "anonymous", user: null, reason: "signedOut" });
  }, [be]);

  const value = useMemo<AuthContextValue>(() => ({
    ...state, backend: be, signIn, signOut,
    can: (p) => !!state.user?.permissions.includes(p),
    hasRole: (r) => !!state.user?.roles.includes(r as never),
  }), [state, be, signIn, signOut]);

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useAuth(): AuthContextValue {
  const v = useContext(Ctx);
  if (!v) throw new Error("useAuth must be used inside <AuthProvider>");
  return v;
}

/** The signed-in user; only call below a route guard. */
export function useUser(): AuthUser {
  const { user } = useAuth();
  if (!user) throw new Error("useUser requires a signed-in user");
  return user;
}

/** Like useAuth, but null when no <AuthProvider> is mounted (component tests, the design-system gallery). */
export function useAuthOptional(): AuthContextValue | null {
  return useContext(Ctx);
}

/** Key of the signed-in patient's own record. Falls back to the demo patient when no auth layer is mounted (component tests). */
export function useSubjectKey(): string {
  return useContext(Ctx)?.user?.subjectKey ?? "pt-sara";
}
