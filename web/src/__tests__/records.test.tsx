import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../AppProviders";
import { LocalMockBackend } from "../auth/localMockBackend";
import { RecordsApi } from "../records/api";
import type { Freshness, GuidanceMessage, Profile, Queue, Report } from "../records/types";
import { routes } from "../routes";
import { createMockServices } from "../services/mock/createMockServices";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
beforeEach(() => { Object.defineProperty(window, "innerWidth", { value: 800, configurable: true }); });

const json = (status: number, body: unknown = {}) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
type Handler = (path: string, init?: RequestInit) => Response | Promise<Response>;

function open(path: string, account: string, handler: Handler | null, locale: "en" | "fa" = "en") {
  const be = new LocalMockBackend({ storage: null, initialAccountId: account });
  if (handler) Object.assign(be, { apiFetch: vi.fn(async (p: string, i?: RequestInit) => handler(p, i)) });
  return render(<AppProviders locale={locale} theme="light" auth={be} services={createMockServices({ latencyMs: 0 })}><RouterProvider router={createMemoryRouter(routes, { initialEntries: [path] })} /></AppProviders>);
}

const profile: Profile = { subjectId: "u", yearOfBirth: 1985, ageYears: 41, sex: "Female", weightKg: null, heightCm: null, timeZone: "UTC", updatedAt: "2026-10-01T00:00:00Z", version: 1, isDemo: true, notice: "DEMO" };
const fresh = (category: Freshness["category"], over: Partial<Freshness> = {}): Freshness => ({ category, lastUpdatedAt: "2026-10-01T00:00:00Z", recordCount: 2, neverRecorded: false, isStale: false, staleAfterDays: 180, ...over });
const report = (over: Partial<Report> = {}): Report => ({
  id: "r1", subjectId: "u", productRecordId: "p1", productName: "Nocturin (DEMO)", batchNumber: "DEMO-B-1", status: "PendingConsentOrReview", issueType: "AbnormalAppearanceOrPackaging", severity: "Mild", occurredOn: "2026-10-01",
  durationOfUseDays: null, description: null, includeConcomitantMedications: false, reviewRequired: true, consentActive: true, reviewerNote: null, reviewDecision: null, failureCode: null, attempts: 0,
  createdAt: "2026-10-01T00:00:00Z", updatedAt: "2026-10-01T00:00:00Z", submittedAt: null, reviewedAt: null, sentAt: null, acknowledgedAt: null, isMockDelivery: false, version: 1, isDemo: true, notice: "DEMO", payloadPreview: null, ...over,
});

describe("RecordsApi", () => {
  it("fails with notConnected when there is no authorised fetch (never pretends)", async () => {
    await expect(new RecordsApi(undefined).freshness("u")).rejects.toMatchObject({ kind: "notConnected" });
  });
  it("encodes ids and sends JSON bodies", async () => {
    const f = vi.fn(async () => json(200, {}));
    await new RecordsApi(f).saveProfile("a/b", { yearOfBirth: 1990, sex: null, weightKg: null, heightCm: null, timeZone: null, expectedVersion: 1 });
    const [path, init] = f.mock.calls[0] as unknown as [string, RequestInit];
    expect(path).toBe("/patients/a%2Fb/profile");
    expect(init.method).toBe("PUT");
    expect(JSON.parse(String(init.body))).toMatchObject({ yearOfBirth: 1990, expectedVersion: 1 });
  });
  it.each([[401, "unauthorized"], [403, "unauthorized"], [404, "notFound"], [409, "conflict"], [400, "invalid"], [429, "rateLimited"], [501, "notAvailable"], [500, "server"]])("maps HTTP %i to %s", async (status, kind) => {
    await expect(new RecordsApi(async () => json(status, { code: "x" })).freshness("u")).rejects.toMatchObject({ kind });
  });
  it("reports a lost connection as network", async () => {
    await expect(new RecordsApi(async () => { throw new TypeError("offline"); }).freshness("u")).rejects.toMatchObject({ kind: "network" });
  });
});

describe("record pages", () => {
  it("says honestly that the page is not connected when the demo account has no API", async () => {
    open("/app/patient/records", "demo-patient", null);
    expect((await screen.findAllByText("Not connected to the server")).length).toBeGreaterThan(0);
  });

  it("shows profile and freshness, including 'never recorded' and 'may be out of date'", async () => {
    open("/app/patient/records", "demo-patient", (p) => {
      if (p.endsWith("/profile")) return json(200, profile);
      if (p.endsWith("/freshness")) return json(200, [fresh("Profile"), fresh("Allergies", { isStale: true }), fresh("Symptoms", { neverRecorded: true, lastUpdatedAt: null, recordCount: 0 })]);
      return json(200, []);
    });
    expect(await screen.findByText("May be out of date")).toBeInTheDocument();
    expect(screen.getByText("Not recorded yet")).toBeInTheDocument();
    expect(screen.getAllByText("Allergies").length).toBeGreaterThan(0);
  });

  it("offers to create a record when none exists", async () => {
    const calls: string[] = [];
    open("/app/patient/records", "demo-patient", (p, i) => { calls.push(`${i?.method ?? "GET"} ${p}`); return p.endsWith("/profile") ? json(404, { code: "patient.not_found" }) : json(200, []); });
    await userEvent.click(await screen.findByRole("button", { name: "Create my record" }));
    await waitFor(() => expect(calls).toContain("POST /patients/me"));
  });

  it("renders in Persian with RTL-safe content", async () => {
    open("/app/patient/records", "demo-patient", (p) => (p.endsWith("/profile") ? json(200, profile) : json(200, p.endsWith("/freshness") ? [fresh("Profile")] : [])), "fa");
    expect(await screen.findByText("تازگی اطلاعات")).toBeInTheDocument();
  });

  it("shows the empty state for batches", async () => {
    open("/app/patient/batches", "demo-patient", (p) => (p.includes("/medications") ? json(200, []) : json(200, [])));
    expect(await screen.findByText("No batches recorded")).toBeInTheDocument();
  });

  it("lists reports with a plain status and a MOCK-delivery warning", async () => {
    open("/app/patient/myreports", "demo-patient", (p) => (p.endsWith("/manufacturer-reports") ? json(200, [report({ status: "Acknowledged", isMockDelivery: true })]) : json(200, [])));
    expect((await screen.findAllByText("Acknowledged")).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/MOCK/).length).toBeGreaterThan(0);
  });

  it("shows an urgent message with its urgent-signs part and the 'done' action", async () => {
    const msg: GuidanceMessage = { id: "m1", templateKey: "t", level: "Urgent", status: "Sent", locale: "en", createdAt: "2026-10-01T00:00:00Z", isDemo: true, notice: "DEMO", professional: null,
      patient: { observed: "obs", whyItMatters: "why", suggestedAction: "act", whenToConsult: "consult", urgentSigns: "signs", basisAndConfidence: "basis" } };
    open("/app/patient/messages", "demo-patient", (p) => (p.includes("/guidance/messages") ? json(200, [msg]) : json(200, { demo: true, notice: "DEMO", samples: [] })));
    expect(await screen.findByText("Signs that need quick help")).toBeInTheDocument();
    expect(screen.getByText("signs")).toBeInTheDocument();
  });

  it("shows the review queue to the physician without exposing identity", async () => {
    open("/app/physician/reportreviews", "demo-physician", (p) => (p.endsWith("/pending-review") ? json(200, [report()]) : json(200, [])));
    expect(await screen.findByText(/Nocturin \(DEMO\)/)).toBeInTheDocument();
    expect(screen.queryByText(/subject/i)).not.toBeInTheDocument();
  });

  it("shows the admin queue as MOCK and says real sending needs an agreement", async () => {
    const q: Queue = { reportsByStatus: { Sent: 1 }, outboxByState: { Pending: 2 }, entries: [], provider: "mock", providerIsMock: true, providerConfigured: true, notice: "DEMO" };
    open("/app/admin/queue", "demo-system-admin", (p) => (p.endsWith("/queue") ? json(200, q) : json(200, [])));
    expect(await screen.findByText(/MOCK — test only/)).toBeInTheDocument();
    expect(screen.getByText(/signed agreement/)).toBeInTheDocument();
  });

  it("keeps patient-only pages out of reach of other roles", async () => {
    open("/app/patient/messages", "demo-physician", () => json(200, []));
    await waitFor(() => expect(screen.queryByText("Messages for you")).not.toBeInTheDocument());
  });
});
