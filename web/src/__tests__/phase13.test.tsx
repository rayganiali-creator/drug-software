import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter, type RouteObject } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../AppProviders";
import { LocalMockBackend } from "../auth/localMockBackend";
import { navByRole, roles } from "../layouts/roles";
import { routes } from "../routes";
import { createMockServices } from "../services/mock/createMockServices";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
beforeEach(() => { Object.defineProperty(window, "innerWidth", { value: 900, configurable: true }); });

const json = (status: number, body: unknown = {}) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
type Call = { method: string; path: string; body: unknown };
function open(reply: (p: string, init?: RequestInit) => Response | Promise<Response>, path: string, account = "demo-pharmacist", locale: "en" | "fa" = "en") {
  const calls: Call[] = [];
  const be = new LocalMockBackend({ storage: null, initialAccountId: account });
  Object.assign(be, { apiFetch: vi.fn(async (p: string, i?: RequestInit) => { calls.push({ method: i?.method ?? "GET", path: p, body: i?.body ? JSON.parse(String(i.body)) : undefined }); return reply(p, i); }) });
  render(<AppProviders locale={locale} theme="light" auth={be} services={createMockServices({ latencyMs: 0 })}><RouterProvider router={createMemoryRouter(routes, { initialEntries: [path] })} /></AppProviders>);
  return calls;
}

function childPaths(r: RouteObject): string[] { return (r.children ?? []).map((c) => c.path ?? (c.index ? "" : "?")); }
function findRole(list: RouteObject[], role: string): RouteObject | undefined {
  for (const r of list) {
    if (r.path === role) return r;
    const hit = r.children ? findRole(r.children, role) : undefined;
    if (hit) return hit;
  }
  return undefined;
}

describe("role navigation matches the routes", () => {
  it("every navigation entry of every role has a route under that role (no dead links)", () => {
    for (const role of roles) {
      const node = findRole(routes, role);
      expect(node, `route group for ${role}`).toBeDefined();
      const have = new Set(childPaths(node!));
      for (const item of navByRole[role]) {
        expect(have.has(item.path), `${role}: nav "${item.id}" has no route`).toBe(true);
      }
    }
  });

  it("professional-only workspaces are not in the patient's navigation", () => {
    const patient = navByRole.patient.map((n) => n.id);
    for (const pro of ["safety-log", "product-concerns", "reportreviews", "rules", "care", "prescriptions"]) expect(patient).not.toContain(pro);
    expect(navByRole.pharmacist.map((n) => n.id)).toEqual(expect.arrayContaining(["safety-log", "product-concerns", "adverse-events"]));
    expect(navByRole.physician.map((n) => n.id)).not.toContain("safety-log");
  });

  it("a patient who opens a pharmacy page directly is not shown it", async () => {
    open(() => json(200, []), "/app/pharmacist/safety-log", "demo-patient");
    await waitFor(() => expect(document.body.textContent).not.toContain("Dispensing errors and near misses"));
  });
});

const care = (over: Record<string, unknown> = {}) => ({ relationship: { id: "r1", patientSubjectId: "p1", providerUserId: "u", kind: "Dispensing", status: "Active", initiatedBy: "Patient", requestedAt: "2026-10-01T00:00:00Z", patientConsentedAt: null, providerConsentedAt: null, endedAt: null, endedBy: null, endReason: null, ...over }, patientName: over.name ?? "DEMO Patient A", providerName: null });
const product = { id: "pr1", subjectId: "p1", productName: "Demopril", batchNumber: "LOT-9", expiryDate: "2027-05-01", manufacturerName: "Fictional Maker", version: 2 };

describe("batch picker", () => {
  it("lists only active relationships, opens only that patient's shared batches and posts the chosen ids (never typed ids)", async () => {
    const calls = open((p, i) => {
      if (i?.method === "POST") return json(201, { concern: { id: "c", organizationId: "o", type: "AppearanceOrPackaging", typeBasis: "ProvisionalNotSourceDefined", description: "d", observedOn: null, linkState: "LinkedToProductRecord", linked: null, asReported: null, flags: [], reporterRole: "Pharmacist", reportedAt: "2026-10-10T08:00:00Z", status: "Submitted", manufacturerReportProposed: false, version: 1, notice: "", externalStatus: "NotSubmitted", persianWordingStatus: "AwaitingClinicalReview" }, entries: [] });
      if (p === "/care-relationships") return json(200, [care(), care({ patientSubjectId: "p2", status: "Ended", name: "DEMO Ended Patient" })]);
      if (p === "/patients/p1/products") return json(200, [product]);
      if (p.endsWith("/product-concerns")) return json(200, []);
      return json(404);
    }, "/app/pharmacist/product-concerns");
    const user = userEvent.setup();
    await user.type(await screen.findByLabelText("What is the concern?"), "Cracked packs");
    const patient = await screen.findByLabelText("Patient");
    expect(screen.queryByRole("option", { name: "DEMO Ended Patient" })).not.toBeInTheDocument();
    await user.selectOptions(patient, "p1");
    await user.selectOptions(await screen.findByLabelText("Batch record"), "pr1");
    expect(screen.getByText("The batch identifiers will be copied from this record when you save.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Save this record" }));
    await waitFor(() => expect(calls.some((c) => c.method === "POST")).toBe(true));
    expect(calls.find((c) => c.method === "POST")!.body).toMatchObject({ productRecord: { subjectId: "p1", productRecordId: "pr1" } });
    expect(screen.queryByLabelText("Patient id")).not.toBeInTheDocument();
  });

  it("when the server refuses a patient's batches nothing is linked and the reporter is told they can save it as unmatched", async () => {
    const calls = open((p) => p === "/care-relationships" ? json(200, [care()]) : p === "/patients/p1/products" ? json(403, {}) : p.endsWith("/product-concerns") ? json(200, []) : json(404), "/app/pharmacist/product-concerns");
    const user = userEvent.setup();
    await screen.findByRole("option", { name: "DEMO Patient A" });
    await user.selectOptions(screen.getByLabelText("Patient"), "p1");
    expect(await screen.findByText(/not available to you/)).toBeInTheDocument();
    expect(screen.queryByLabelText("Batch record")).not.toBeInTheDocument();
    expect(calls.filter((c) => c.method === "POST")).toHaveLength(0);
  });
});

describe("list filters", () => {
  const rows = [
    { id: "a", recordKind: "SafetyEvent", label: "NearMiss", status: "Submitted", reportedAt: "2026-10-10T08:00:00Z", lastActivityAt: "2026-10-10T08:00:00Z", excerpt: "Blue box mix-up" },
    { id: "b", recordKind: "SafetyEvent", label: "DispensingError", status: "Closed", reportedAt: "2026-10-09T08:00:00Z", lastActivityAt: "2026-10-09T08:00:00Z", excerpt: "Wrong quantity" },
  ];
  it("filters the pharmacy log by status and by text, and says when nothing matches", async () => {
    open((p) => p.endsWith("/safety-events") ? json(200, rows) : json(404), "/app/pharmacist/safety-log");
    const user = userEvent.setup();
    expect(await screen.findByText("Blue box mix-up")).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Status"), "Closed");
    expect(screen.queryByText("Blue box mix-up")).not.toBeInTheDocument();
    expect(screen.getByText("Wrong quantity")).toBeInTheDocument();
    await user.type(screen.getByLabelText("Search in the text"), "zzz");
    expect(screen.getByText("Nothing matches this filter.")).toBeInTheDocument();
  });

  it("filters the adverse-event queue by text", async () => {
    open((p) => p.includes("/queue") ? json(200, [
      { id: "1", subjectId: "s", reporterRole: "Patient", origin: "PatientReported", reportedAt: "2026-10-10T08:00:00Z", category: "Unclassified", status: "Submitted", lastActivityAt: "2026-10-10T08:00:00Z", excerpt: "Itchy rash" },
      { id: "2", subjectId: "s", reporterRole: "Patient", origin: "PatientReported", reportedAt: "2026-10-10T08:00:00Z", category: "Unclassified", status: "Submitted", lastActivityAt: "2026-10-10T08:00:00Z", excerpt: "Dizziness" },
    ]) : json(404), "/app/physician/adverse-events", "demo-physician");
    const user = userEvent.setup();
    expect(await screen.findByText("Itchy rash")).toBeInTheDocument();
    await user.type(screen.getByLabelText("Search in the text"), "dizz");
    expect(screen.queryByText("Itchy rash")).not.toBeInTheDocument();
    expect(screen.getByText("Dizziness")).toBeInTheDocument();
  });
});

describe("report status honesty", () => {
  const report = (over: Record<string, unknown>) => ({ id: "r1", subjectId: "s", productRecordId: "p", productName: "Demopril", batchNumber: "LOT-1", status: "Acknowledged", issueType: "QualityProblem", severity: "Mild", occurredOn: "2026-10-01", durationOfUseDays: null, description: null,
    includeConcomitantMedications: false, reviewRequired: false, consentActive: true, reviewerNote: null, reviewDecision: null, failureCode: null, attempts: 1, createdAt: "2026-10-02T00:00:00Z", updatedAt: "2026-10-03T00:00:00Z", submittedAt: null, reviewedAt: null, sentAt: null, acknowledgedAt: null,
    isMockDelivery: true, version: 3, isDemo: true, notice: null, payloadPreview: null, ...over });
  it("a sent or acknowledged report with a simulated delivery is never shown as plainly sent or acknowledged", async () => {
    open((p) => p.endsWith("/manufacturer-reports") ? json(200, [report({ status: "Acknowledged" }), report({ id: "r2", status: "Sent" })]) : json(404), "/app/patient/myreports", "demo-patient");
    expect(await screen.findByText("Acknowledged (simulated — not from a real organization)")).toBeInTheDocument();
    expect(screen.getByText("Sent (simulated — no real recipient)")).toBeInTheDocument();
    expect(screen.queryByText("Acknowledged", { selector: ".ms-badge" })).not.toBeInTheDocument();
  });
});
