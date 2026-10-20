import { describe, expect, it, vi } from "vitest";
import { ApiBackend } from "../auth/apiBackend";

const mem = () => {
  const m = new Map<string, string>();
  return { store: m, storage: { getItem: (k: string) => m.get(k) ?? null, setItem: (k: string, v: string) => void m.set(k, v), removeItem: (k: string) => void m.delete(k) } };
};
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const authBody = (access = "access-1", refresh = "refresh-1") => ({
  tokens: { accessToken: access, refreshToken: refresh },
  user: { id: "fa1f8b92-53cc-5ad2-a0a8-c39713c37946", displayName: "DEMO Patient — Sara Ahmadi", roles: ["Patient"], permissions: ["patient.profile.read"], organizationIds: [] },
});

function make(handler: (url: string, init: RequestInit) => Response | Promise<Response>) {
  const session = mem();
  const local = mem();
  const calls: { url: string; init: RequestInit }[] = [];
  const fetchImpl = vi.fn(async (url: string | URL | Request, init?: RequestInit) => { calls.push({ url: String(url), init: init ?? {} }); return handler(String(url), init ?? {}); }) as unknown as typeof fetch;
  // one storage object for both is enough to prove where tokens do NOT go: use separate stores through subclass-free trick
  const be = new ApiBackend({ baseUrl: "http://api.test", fetchImpl, storage: session.storage });
  return { be, session, local, calls, fetchImpl };
}

describe("ApiBackend", () => {
  it("logs in with the mock provider, keeps the access token out of storage, and returns the server's user", async () => {
    const { be, session, calls } = make(() => json(200, authBody()));
    const r = await be.signIn("demo-patient");
    expect(r.ok).toBe(true);
    expect(r.ok && r.user.roles).toEqual(["Patient"]);
    expect(r.ok && r.user.subjectKey).toBe("pt-sara");
    const sent = JSON.parse(String(calls[0]!.init.body)) as { provider: string; credentials: Record<string, string> };
    expect(sent.provider).toBe("mock");
    expect(sent.credentials).toEqual({ accountId: "demo-patient" });
    expect(JSON.stringify([...session.store.values()])).not.toContain("access-1"); // access token: memory only
    expect(session.store.get("ms.auth.refresh")).toBe("refresh-1");
    expect(calls[0]!.init.credentials).toBe("omit");
  });

  it("maps server errors to sign-in errors", async () => {
    const cases: [number, unknown, string][] = [
      [401, { code: "invalid_credentials" }, "invalid"], [403, { code: "account_disabled" }, "disabled"],
      [403, { code: "no_active_role" }, "noRole"], [429, { code: "throttled" }, "throttled"],
    ];
    for (const [status, body, expected] of cases) {
      const { be } = make(() => json(status, body));
      expect(await be.signIn("x")).toEqual({ ok: false, error: expected });
    }
    const down = new ApiBackend({ baseUrl: "http://api.test", fetchImpl: (() => Promise.reject(new TypeError("fail"))) as unknown as typeof fetch, storage: mem().storage });
    expect(await down.signIn("x")).toEqual({ ok: false, error: "network" });
  });

  it("sends the bearer token, and on 401 refreshes once and retries", async () => {
    let refreshed = false;
    const { be, calls } = make((url, init) => {
      if (url.endsWith("/auth/login")) return json(200, authBody("old", "r1"));
      if (url.endsWith("/auth/refresh")) { refreshed = true; return json(200, authBody("new", "r2")); }
      const bearer = new Headers(init.headers).get("Authorization");
      return bearer === "Bearer new" ? json(200, []) : json(401, {});
    });
    await be.signIn("demo-patient");
    expect(await be.sessions()).toEqual([]);
    expect(refreshed).toBe(true);
    expect(calls.filter((c) => c.url.endsWith("/sessions"))).toHaveLength(2);
  });

  it("reports a lost session when the refresh fails, and clears the stored refresh token", async () => {
    const { be, session } = make((url) => (url.endsWith("/auth/login") ? json(200, authBody()) : json(401, {})));
    await be.signIn("demo-patient");
    const lost = vi.fn();
    be.onSessionLost(lost);
    await be.sessions();
    expect(lost).toHaveBeenCalledOnce();
    expect(session.store.has("ms.auth.refresh")).toBe(false);
  });

  it("concurrent 401s share one refresh (rotation-safe)", async () => {
    let refreshCalls = 0;
    const { be } = make((url, init) => {
      if (url.endsWith("/auth/login")) return json(200, authBody("old", "r1"));
      if (url.endsWith("/auth/refresh")) { refreshCalls++; return json(200, authBody("new", "r2")); }
      return new Headers(init.headers).get("Authorization") === "Bearer new" ? json(200, []) : json(401, {});
    });
    await be.signIn("demo-patient");
    await Promise.all([be.sessions(), be.consents(), be.accessLog()]);
    expect(refreshCalls).toBe(1);
  });

  it("sign-out calls the server and forgets everything locally", async () => {
    const { be, session, calls } = make(() => json(200, authBody()));
    await be.signIn("demo-patient");
    await be.signOut();
    expect(calls.some((c) => c.url.endsWith("/auth/logout"))).toBe(true);
    expect(session.store.has("ms.auth.refresh")).toBe(false);
  });

  it("restore uses the refresh token, and returns null without one", async () => {
    const empty = make(() => json(401, {}));
    expect(await empty.be.restore()).toBeNull();
    const { be, session } = make((url) => (url.endsWith("/auth/refresh") ? json(200, authBody()) : json(200, authBody().user)));
    session.store.set("ms.auth.refresh", "r0");
    expect((await be.restore())?.displayName).toContain("Sara");
  });

  it("asks the server whether a patient can be opened (the server decides)", async () => {
    const { be, calls } = make((url) => (url.includes("/auth/login") ? json(200, authBody()) : url.includes("fa1f8b92") ? json(200, {}) : json(403, {})));
    await be.signIn("demo-patient");
    expect(await be.canViewPatient("pt-1", "medications")).toBe(false);
    expect(calls.at(-1)!.url).toMatch(/\/patients\/64f9fd31-4b11-50de-9e63-815ae0c84a64\/medications$/);
    expect(await be.canViewPatient("pt-sara")).toBe(true); // own record
    expect(await be.canViewPatient("nobody")).toBe(false);
  });

  it("maps consents, demo accounts and the audit chain response", async () => {
    const { be } = make((url) => {
      if (url.endsWith("/auth/login")) return json(200, authBody());
      if (url.endsWith("/consents")) return json(200, [{ consent: { id: "c1", purpose: "Treatment", scope: ["profile"], expiresAt: "2027-01-01T00:00:00Z", status: "Active" }, granteeName: "Dr. X" }]);
      if (url.endsWith("/auth/demo-accounts")) return json(200, { accounts: [{ id: "a", displayName: { en: "A", fa: "آ" }, description: { en: "d", fa: "د" }, roles: ["Patient"], primary: true, status: "Disabled" }] });
      if (url.endsWith("/audit/verify")) return json(200, { intact: false });
      return json(404, {});
    });
    await be.signIn("demo-patient");
    expect((await be.consents())[0]).toMatchObject({ id: "c1", status: "active", granteeName: "Dr. X" });
    expect((await be.demoAccounts())[0]).toMatchObject({ id: "a", disabled: true });
    expect(await be.adminVerifyAudit()).toBe(false);
  });

  describe("demo access code", () => {
    const gated = (handler: (url: string, init: RequestInit) => Response) => {
      const session = mem();
      const calls: { url: string; init: RequestInit }[] = [];
      const fetchImpl = vi.fn(async (url: string | URL | Request, init?: RequestInit) => { calls.push({ url: String(url), init: init ?? {} }); return handler(String(url), init ?? {}); }) as unknown as typeof fetch;
      return { be: new ApiBackend({ baseUrl: "http://api.test", fetchImpl, storage: session.storage, demoGate: true }), session, calls };
    };
    const header = (c: { init: RequestInit }) => new Headers(c.init.headers).get("X-Demo-Code");

    it("is not required unless the build says the server is gated", () => {
      expect(make(() => json(200, {})).be.needsDemoCode).toBe(false);
      expect(make(() => json(200, {})).be.hasDemoCode()).toBe(true);
    });

    it("accepts a correct code, keeps it tab-scoped, and sends it only to the gated anonymous endpoints", async () => {
      const { be, session, calls } = gated((url, init) => {
        const ok = new Headers(init.headers).get("X-Demo-Code") === "right-code-123";
        if (url.endsWith("/auth/demo-accounts")) return ok ? json(200, { accounts: [] }) : json(401, { code: "demo_code_invalid" });
        if (url.endsWith("/auth/login")) return ok ? json(200, authBody()) : json(401, { code: "demo_code_required" });
        return json(404, {});
      });
      expect(be.hasDemoCode()).toBe(false);
      expect(await be.unlockDemo("wrong")).toBe("invalid");
      expect(be.hasDemoCode()).toBe(false);
      expect(await be.unlockDemo("right-code-123")).toBe("ok");
      expect(session.store.get("ms.demo.code")).toBe("right-code-123");
      expect((await be.signIn("demo-patient")).ok).toBe(true);
      expect(header(calls.at(-1)!)).toBe("right-code-123");
      expect(calls.at(-1)!.url).toBe("http://api.test/auth/login");
      await be.sessions();
      expect(header(calls.at(-1)!)).toBeNull(); // authenticated calls carry the bearer token, not the code
    });

    it("forgets a code the server rejects and reports it as a code error; maps throttling and network failures", async () => {
      const { be, session } = gated(() => json(401, { code: "demo_code_invalid" }));
      session.store.set("ms.demo.code", "stale");
      const stored = new ApiBackend({ baseUrl: "http://api.test", fetchImpl: (() => Promise.resolve(json(401, { code: "demo_code_invalid" }))) as unknown as typeof fetch, storage: session.storage, demoGate: true });
      expect(stored.hasDemoCode()).toBe(true);
      expect(await stored.signIn("demo-patient")).toEqual({ ok: false, error: "code" });
      expect(stored.hasDemoCode()).toBe(false);
      expect(session.store.has("ms.demo.code")).toBe(false);
      const limited = new ApiBackend({ baseUrl: "http://api.test", fetchImpl: (() => Promise.resolve(json(429, { code: "demo_code_throttled" }))) as unknown as typeof fetch, storage: mem().storage, demoGate: true });
      expect(await limited.unlockDemo("x")).toBe("throttled");
      expect(await be.unlockDemo("x")).toBe("invalid");
      const down = new ApiBackend({ baseUrl: "http://api.test", fetchImpl: (() => Promise.reject(new TypeError("fail"))) as unknown as typeof fetch, storage: mem().storage, demoGate: true });
      expect(await down.unlockDemo("x")).toBe("network");
    });
  });
});
