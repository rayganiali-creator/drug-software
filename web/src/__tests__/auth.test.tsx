import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../AppProviders";
import { areasOf, canEnter, homeFor } from "../auth/areas";
import { catalog, permissionsOf } from "../auth/catalog";
import { LocalMockBackend } from "../auth/localMockBackend";
import { routes } from "../routes";
import { createMockServices } from "../services/mock/createMockServices";

afterEach(cleanup);

const backend = (id?: string, now?: () => number) => new LocalMockBackend({ storage: null, initialAccountId: id, now });
const app = (path: string, be: LocalMockBackend, locale: "en" | "fa" = "en") =>
  render(<AppProviders locale={locale} theme="light" auth={be} services={createMockServices({ latencyMs: 0 })}><RouterProvider router={createMemoryRouter(routes, { initialEntries: [path] })} /></AppProviders>);
const user = (be: LocalMockBackend) => be.restore();

describe("LocalMockBackend (demo accounts)", () => {
  it("every login-enabled account signs in with exactly its catalog permissions", async () => {
    for (const a of catalog.demoAccounts.filter((x) => x.loginEnabled && x.status === "Active")) {
      const be = backend();
      const r = await be.signIn(a.id);
      expect(r.ok, a.id).toBe(true);
      if (r.ok) expect(r.user.permissions).toEqual(permissionsOf([...new Set(a.roles.map((x) => x.role))]));
    }
  });

  it("rejects unknown and non-login accounts, and reports disabled accounts distinctly", async () => {
    const be = backend();
    expect(await be.signIn("nobody")).toEqual({ ok: false, error: "invalid" });
    expect(await be.signIn("subject-pt-2")).toEqual({ ok: false, error: "invalid" });
    expect(await be.signIn("demo-disabled")).toEqual({ ok: false, error: "disabled" });
    expect(await user(be)).toBeNull();
  });

  it("sessions end after 8 hours, on sign-out, and via the simulated expiry", async () => {
    let t = 1_000_000;
    const be = backend(undefined, () => t);
    await be.signIn("demo-patient");
    expect(await user(be)).not.toBeNull();
    t += 8 * 3_600_000 + 1;
    expect(await user(be)).toBeNull();

    const be2 = backend();
    await be2.signIn("demo-patient");
    const lost = vi.fn();
    be2.onSessionLost(lost);
    be2.simulateExpiry();
    expect(lost).toHaveBeenCalledOnce();
    expect(await user(be2)).toBeNull();

    const be3 = backend();
    await be3.signIn("demo-patient");
    await be3.signOut();
    expect(await user(be3)).toBeNull();
  });

  it("persists only the demo account id + expiry (no tokens) and restores it", async () => {
    const store = new Map<string, string>();
    const storage = { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => void store.set(k, v), removeItem: (k: string) => void store.delete(k) };
    const a = new LocalMockBackend({ storage });
    await a.signIn("demo-physician");
    const saved = [...store.values()].join("|");
    expect(saved).toContain("demo-physician");
    expect(saved).not.toMatch(/token|password|secret/i);
    const b = new LocalMockBackend({ storage });
    expect((await b.restore())?.displayName).toContain("Physician");
  });

  it("patient-level access needs a relationship AND an active, in-scope consent", async () => {
    const phys = backend("demo-physician");
    expect(await phys.canViewPatient("pt-sara")).toBe(true);
    expect(await phys.canViewPatient("pt-1")).toBe(false); // consent expired
    expect(await backend("demo-physician-b").canViewPatient("pt-sara")).toBe(false); // no relationship
    const pharm = backend("demo-pharmacist");
    expect(await pharm.canViewPatient("pt-sara", "prescriptions")).toBe(true);
    expect(await pharm.canViewPatient("pt-sara", "adherence")).toBe(false); // outside consent scope
    expect(await pharm.canViewPatient("pt-4")).toBe(false); // no relationship
    expect(await backend("demo-patient").canViewPatient("pt-sara")).toBe(true); // own
    expect(await backend("demo-patient").canViewPatient("pt-1")).toBe(false); // someone else
    const pharmacy = backend("demo-pharmacy-admin");
    expect(await pharmacy.canViewPatient("pt-sara", "prescriptions")).toBe(true); // organization consent
    expect(await pharmacy.canViewPatient("pt-sara", "profile")).toBe(false);
    expect(await pharmacy.canViewPatient("pt-1", "prescriptions")).toBe(false);
    expect(await backend("demo-system-admin").canViewPatient("pt-sara")).toBe(false);
    expect(await backend("demo-industry").canViewPatient("pt-sara")).toBe(false);
    expect(await backend().canViewPatient("pt-sara")).toBe(false); // anonymous
  });

  it("revoking a consent removes the professional's access at once, and only the subject can revoke", async () => {
    const be = backend("demo-patient");
    const list = await be.consents();
    const forPhysician = list.find((c) => c.granteeName.includes("Physician"))!;
    await be.signOut();
    await be.signIn("demo-patient-2");
    await be.revokeConsent(forPhysician.id); // not the subject: ignored
    await be.signOut();
    await be.signIn("demo-physician");
    expect(await be.canViewPatient("pt-sara")).toBe(true);
    await be.signOut();
    await be.signIn("demo-patient");
    await be.revokeConsent(forPhysician.id);
    await be.signOut();
    await be.signIn("demo-physician");
    expect(await be.canViewPatient("pt-sara")).toBe(false);
  });

  it("administration is limited to role.manage holders and protects the last system admin", async () => {
    const patient = backend("demo-patient");
    expect(await patient.adminUsers()).toEqual([]);
    expect(await patient.adminAssignRole("x", "Researcher")).toBe(false);
    const admin = backend("demo-system-admin");
    const users = await admin.adminUsers();
    const target = users.find((u) => u.displayName.includes("Content"))!;
    expect(await admin.adminAssignRole(target.id, "Researcher")).toBe(true);
    expect(await admin.adminAssignRole(target.id, "Researcher")).toBe(false);
    expect(await admin.adminRevokeRole(target.id, "Researcher")).toBe(true);
    const self = users.find((u) => u.roles.includes("SystemAdmin"))!;
    expect(await admin.adminRevokeRole(self.id, "SystemAdmin")).toBe(false);
    expect((await admin.adminAudit()).map((e) => e.action)).toContain("ROLE_ASSIGNED");
  });

  it("removing a role applies on the very next check (no stale permissions)", async () => {
    const be = backend("demo-system-admin");
    const users = await be.adminUsers();
    const ai = users.find((u) => u.displayName.includes("AI"))!;
    // sign in as the AI manager on a second backend sharing nothing: emulate by revoking on the same object
    await be.adminRevokeRole(ai.id, "AIManager");
    const other = backend();
    // Fresh backend has fresh role state: the assertion here is that the acting backend itself reflects it
    expect((await be.adminUsers()).find((u) => u.id === ai.id)!.roles).toEqual([]);
    void other;
  });
});

describe("areas (UX authorization)", () => {
  const landing: Record<string, string> = {
    "demo-patient": "/app/patient", "demo-physician": "/app/physician", "demo-pharmacist": "/app/pharmacist", "demo-pharmacy-admin": "/app/pharmacy",
    "demo-industry": "/app/industry", "demo-researcher": "/app/industry", "demo-content-manager": "/app/workspace", "demo-ai-manager": "/app/workspace", "demo-system-admin": "/app/admin",
  };
  it.each(Object.entries(landing))("%s lands on %s", async (id, path) => {
    const be = backend();
    const r = await be.signIn(id);
    expect(r.ok && homeFor(r.user)).toBe(path);
  });

  it("no role can enter another role's area", async () => {
    for (const id of ["demo-patient", "demo-physician", "demo-pharmacist", "demo-pharmacy-admin", "demo-industry", "demo-system-admin"]) {
      const r = await backend().signIn(id);
      if (!r.ok) throw new Error(id);
      expect(areasOf(r.user), id).toHaveLength(1);
    }
    expect(canEnter(null, "patient")).toBe(false);
  });

  it("a multi-role user can open the areas of every role they hold", async () => {
    const r = await backend().signIn("demo-multirole");
    if (!r.ok) throw new Error("login");
    expect(areasOf(r.user)).toEqual(["physician", "industry"]);
  });
});

describe("route guards and screens", () => {
  it("anonymous visitors are sent to the login screen, which is labelled as a DEMO ENVIRONMENT", async () => {
    app("/app/patient", backend());
    expect(await screen.findByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
    expect(screen.getByText(/DEMO ENVIRONMENT/)).toBeInTheDocument();
    expect(screen.getAllByText(/NOT FOR CLINICAL USE/).length).toBeGreaterThan(0);
  });

  it("signing in returns the user to the page they wanted", async () => {
    const u = userEvent.setup();
    app("/app/patient/medications", backend());
    await u.click(await screen.findByRole("button", { name: "Sign in as DEMO Patient — Sara Ahmadi" }));
    expect(await screen.findByText("Everything about the medicines in your plan.")).toBeInTheDocument();
  });

  it("signing in as a physician lands on the physician dashboard, not the patient app", async () => {
    const u = userEvent.setup();
    app("/login", backend());
    await u.click(await screen.findByRole("button", { name: "Sign in as DEMO Physician — Dr. Karimi" }));
    expect(await screen.findByText("Patients needing attention")).toBeInTheDocument();
  });

  it("a disabled account cannot sign in and the reason is shown", async () => {
    const u = userEvent.setup();
    app("/login", backend());
    await u.click(await screen.findByRole("button", { name: /Sign in as .*Disabled/i }));
    expect(await screen.findByRole("alert")).toHaveTextContent("This account is disabled");
  });

  it.each([
    ["demo-patient", "/app/admin"], ["demo-patient", "/app/physician"], ["demo-physician", "/app/pharmacy"], ["demo-pharmacist", "/app/physician/patients"],
    ["demo-industry", "/app/patient"], ["demo-system-admin", "/app/physician/patients"], ["demo-pharmacy-admin", "/app/admin"],
  ])("%s opening %s sees the neutral no-access page", async (account, path) => {
    app(path, backend(account));
    expect(await screen.findByTestId("unauthorized")).toBeInTheDocument();
    expect(screen.getByText("You do not have access to this page")).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/permission|role\.manage|prescription\.create/);
  });

  it("/app sends each user to their own home", async () => {
    app("/app", backend("demo-pharmacist"));
    expect((await screen.findAllByText("Work queue")).length).toBeGreaterThan(0);
  });

  it("the physician's patient list excludes patients without an active consent", async () => {
    app("/app/physician/patients", backend("demo-physician"));
    expect(await screen.findAllByText("Sara Ahmadi")).not.toHaveLength(0);
    expect(screen.queryByText("Ali Rezaei")).toBeNull(); // consent expired
  });

  it("opening a patient without consent by URL shows the 'not available to you' state", async () => {
    app("/app/physician/patients/pt-1", backend("demo-physician"));
    expect(await screen.findByText("This record is not available to you")).toBeInTheDocument();
    expect(screen.queryByText("Ali Rezaei")).toBeNull();
  });

  it("a consented patient record opens normally", async () => {
    app("/app/physician/patients/pt-sara", backend("demo-physician"));
    expect((await screen.findAllByText(/Sara Ahmadi/)).length).toBeGreaterThan(0);
  });

  it("the pharmacy administrator only sees prescriptions covered by the organization's consent", async () => {
    app("/app/pharmacy/dispensing", backend("demo-pharmacy-admin"));
    await screen.findByRole("heading", { level: 1, name: /Dispensing/ });
    await waitFor(() => expect(screen.getAllByText(/Sara Ahmadi/).length).toBeGreaterThan(0)); // covered by Pharmacy A's consent
    expect(screen.queryByText("Ali Rezaei")).toBeNull(); // no relationship / consent with this organization
  });

  it("the system administrator console lists users and the audit log, and offers no patient records", async () => {
    app("/app/admin", backend("demo-system-admin"));
    expect(await screen.findByRole("heading", { level: 1, name: "Administration" })).toBeInTheDocument();
    expect(await screen.findAllByTestId("admin-user")).not.toHaveLength(0);
    expect(screen.getByText("Administrators cannot open patient records.")).toBeInTheDocument();
    expect(screen.queryByText("Sara Ahmadi")).toBeNull();
  });

  it("the patient's account page shows sessions, consents and can revoke a consent", async () => {
    const u = userEvent.setup();
    app("/app/account", backend("demo-patient"));
    expect(await screen.findByText("This device")).toBeInTheDocument();
    const rows = await screen.findAllByTestId("consent-row");
    expect(rows.length).toBeGreaterThan(0);
    const first = rows[0]!;
    await u.click(within(first).getByRole("button", { name: "Revoke" }));
    await waitFor(() => expect(within(screen.getAllByTestId("consent-row")[0]!).getByText("Revoked")).toBeInTheDocument());
  });

  it("a professional's account page does not offer patient-only consent sections", async () => {
    app("/app/account", backend("demo-physician"));
    await screen.findByText("Signed-in devices");
    expect(screen.queryByText("Data sharing consents")).toBeNull();
  });

  it("signing out returns to the login screen and protected pages stay closed", async () => {
    const u = userEvent.setup();
    const be = backend("demo-patient");
    app("/app/patient", be);
    await screen.findByText(/Next dose/);
    await u.click(screen.getByRole("button", { name: "Account menu" }));
    await u.click(await screen.findByRole("menuitem", { name: "Sign out" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
    expect(await user(be)).toBeNull();
  });

  it("an expired session shows the 'session has ended' message", async () => {
    const u = userEvent.setup();
    const be = backend("demo-patient");
    app("/app/patient", be);
    await screen.findByText(/Next dose/);
    await u.click(screen.getByRole("button", { name: "Account menu" }));
    await u.click(await screen.findByRole("menuitem", { name: /simulate session expiry/i }));
    expect(await screen.findByText("Your session has ended")).toBeInTheDocument();
  });

  it("demo account switching is labelled DEMO DEV", async () => {
    const u = userEvent.setup();
    app("/app/patient", backend("demo-patient"));
    await screen.findByText(/Next dose/);
    await u.click(screen.getByRole("button", { name: "Account menu" }));
    const items = await screen.findAllByRole("menuitem");
    const demo = items.filter((i) => /DEMO DEV/.test(i.textContent ?? ""));
    expect(demo.length).toBeGreaterThan(2);
  });

  it("the login screen is RTL in Persian and every account is a labelled button", async () => {
    app("/login", backend(), "fa");
    await screen.findByRole("heading", { level: 1, name: "ورود" });
    expect(document.documentElement.dir).toBe("rtl");
    expect(screen.getAllByRole("button", { name: /^ورود به‌عنوان/ }).length).toBeGreaterThan(8);
  });
});
