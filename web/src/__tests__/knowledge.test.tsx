import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../AppProviders";
import { LocalMockBackend } from "../auth/localMockBackend";
import { KnowledgeError, type MedicationDetail, type MedicationSummary, type ValidationStatus } from "../knowledge/types";
import { MedicationApi, type ApiFetch } from "../knowledge/medicationApi";
import { routes } from "../routes";
import { createMockServices } from "../services/mock/createMockServices";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const lt = (en: string, fa: string) => ({ en, fa });
const summary = (over: Partial<MedicationSummary> = {}): MedicationSummary => ({
  id: "11111111-1111-1111-1111-111111111111", name: lt("Nocturin", "نوکتورین"), brandName: lt("Nocturin", "نوکتورین"), dosageForm: lt("Tablet", "قرص"), strengthSummary: "5 mg",
  ingredients: [lt("nocturamide (fictional)", "نوکتورامید (فرضی)")], lifecycle: "Active", validation: "Demo", isDemo: true, matchedOn: "Nocturin", score: 1060, ...over,
});
const detail = (over: Partial<MedicationDetail> = {}): MedicationDetail => ({
  id: "11111111-1111-1111-1111-111111111111", version: 3, name: lt("Nocturin", "نوکتورین"), brand: { id: "b", name: lt("Nocturin", "نوکتورین") }, manufacturer: { id: "m", name: lt("DemoPharma A (fictional)", "دموفارما الف (فرضی)"), country: null },
  dosageForm: lt("Tablet", "قرص"), routes: [lt("Oral", "خوراکی")], strengthSummary: "5 mg",
  ingredients: [{ ingredientId: "i1", name: lt("nocturamide (fictional)", "نوکتورامید (فرضی)"), strengthValue: 5, strengthUnit: "mg", perUnit: null, order: 0 }],
  classifications: [{ id: "c", code: "DEMO-SLEEP", name: lt("Demo sleep-support class", "گروه نمایشی حمایت خواب") }], synonyms: [], identifiers: [],
  statements: [{ id: "s1", kind: "Warning", text: lt("Fictional warning: may cause demo drowsiness.", "هشدار فرضی: خواب‌آلودگی نمایشی."), severity: "info", frequency: null, population: null, revisionId: "r", sourceId: "src", validation: "Demo" }],
  missingKinds: ["Indication", "Contraindication", "Precaution", "AdverseReaction", "Storage", "Administration"],
  interactions: [{ id: "x", otherIngredientId: "i2", otherIngredientName: lt("demoprilate (fictional)", "دموپریلات (فرضی)"), severity: "Moderate", mechanism: lt("Fictional: dizziness may feel stronger.", "فرضی: سرگیجه بیشتر."), management: lt("Pharmacist review suggested.", "بازبینی داروساز."), sourceId: "src", validation: "Demo" }],
  sources: [{ id: "src", name: "DEMO seed (fictional)", publisher: "AI MedSmarter test fixtures", type: "Demo", url: null, version: "demo-0.1", licenseName: "Fictional test data", redistributionAllowed: true, usageRestrictions: null }],
  lifecycle: "Active", validation: "Demo", isDemo: true, updatedAt: "2026-10-09T08:00:00Z", notice: "DEMO", ...over,
});

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
type Handler = (path: string) => Response | Promise<Response>;

function backendWith(handler: Handler | null, account = "demo-patient") {
  const be = new LocalMockBackend({ storage: null, initialAccountId: account });
  if (handler) Object.assign(be, { apiFetch: vi.fn(async (path: string) => handler(path)) });
  return be;
}

function open(path: string, be: LocalMockBackend, locale: "en" | "fa" = "en", width = 800) {
  Object.defineProperty(window, "innerWidth", { value: width, configurable: true });
  return render(<AppProviders locale={locale} theme="light" auth={be} services={createMockServices({ latencyMs: 0 })}><RouterProvider router={createMemoryRouter(routes, { initialEntries: [path] })} /></AppProviders>);
}

beforeEach(() => { Object.defineProperty(window, "innerWidth", { value: 800, configurable: true }); });

describe("MedicationApi", () => {
  const ok = (body: unknown = { items: [], total: 0, limit: 20, offset: 0 }) => vi.fn<ApiFetch>(async () => json(200, body));

  it("builds the search URL (trimmed query, paging) and omits an empty query", async () => {
    const f = ok();
    const api = new MedicationApi(f);
    await api.search({ q: "  nocturin ", limit: 5, offset: 10 });
    await api.search({ q: "   " });
    expect(f.mock.calls[0]![0]).toBe("/medications/search?q=nocturin&limit=5&offset=10");
    expect(f.mock.calls[1]![0]).toBe("/medications/search?limit=20&offset=0");
  });

  it("encodes the id and returns the detail", async () => {
    const f = ok(detail());
    expect((await new MedicationApi(f).detail("a/b")).name.en).toBe("Nocturin");
    expect(f.mock.calls[0]![0]).toBe("/medications/a%2Fb");
  });

  it.each([[401, "unauthorized"], [403, "unauthorized"], [404, "notFound"], [429, "rateLimited"], [500, "server"], [503, "server"]])("maps HTTP %i to %s", async (status, kind) => {
    const api = new MedicationApi(async () => json(status, {}));
    await expect(api.search({ q: "x" })).rejects.toMatchObject({ kind });
  });

  it("keeps the server's validation code and nothing else", async () => {
    const api = new MedicationApi(async () => json(400, { code: "q.too_short", detail: "internal stack trace" }));
    const err = await api.search({ q: "a" }).catch((e: unknown) => e as KnowledgeError);
    expect(err).toBeInstanceOf(KnowledgeError);
    expect(err).toMatchObject({ kind: "invalid", code: "q.too_short" });
    expect(String((err as Error).message)).not.toContain("stack");
  });

  it("reports a network failure, and 'not connected' when there is no authorised fetch", async () => {
    await expect(new MedicationApi(async () => { throw new TypeError("fail"); }).search({})).rejects.toMatchObject({ kind: "network" });
    await expect(new MedicationApi(undefined).search({})).rejects.toMatchObject({ kind: "notConnected" });
  });
});

describe("drug search page", () => {
  const list = (items: MedicationSummary[], total = items.length) => json(200, { items, total, limit: 20, offset: 0 });

  it("loads a first page and shows names, brand/generic, form, strength, ingredients and the validation label", async () => {
    open("/app/patient/drugs", backendWith(() => list([summary(), summary({ id: "2", name: lt("Generic X", "ژنریک"), brandName: null, validation: "Validated", isDemo: false, strengthSummary: "10 mg" })])));
    expect(await screen.findByText("Nocturin")).toBeInTheDocument();
    expect(screen.getAllByText(/nocturamide/).length).toBeGreaterThan(0);
    expect(screen.getByText("DEMO")).toBeInTheDocument();
    expect(screen.getByText("Source-validated")).toBeInTheDocument();
    expect(screen.getByText(/Generic product/)).toBeInTheDocument();
    expect(screen.getByText(/Showing 1–2 of 2/)).toBeInTheDocument();
  });

  it("searches after a short pause using the typed text", async () => {
    const calls: string[] = [];
    open("/app/patient/drugs", backendWith((p) => { calls.push(p); return list([summary()]); }));
    await screen.findByText("Nocturin");
    await userEvent.setup().type(screen.getByRole("searchbox", { name: "Search medicines" }), "noc");
    await waitFor(() => expect(calls.some((c) => c.includes("q=noc"))).toBe(true), { timeout: 3000 });
    expect(calls.filter((c) => c.includes("q=n&") || c.includes("q=no&")).length).toBe(0); // debounced: no request per keystroke
  });

  it("does not call the server for a one-character query", async () => {
    const calls: string[] = [];
    open("/app/patient/drugs", backendWith((p) => { calls.push(p); return list([summary()]); }));
    await screen.findByText("Nocturin");
    const before = calls.length;
    await userEvent.setup().type(screen.getByRole("searchbox", { name: "Search medicines" }), "n");
    await screen.findByText("Use between 2 and 64 characters.");
    expect(calls.length).toBe(before);
  });

  it("shows the empty state", async () => {
    open("/app/patient/drugs", backendWith(() => list([])));
    expect(await screen.findByText("No medicines found")).toBeInTheDocument();
  });

  it("shows an error with a working retry", async () => {
    let n = 0;
    open("/app/patient/drugs", backendWith(() => (n++ === 0 ? json(500, {}) : list([summary()]))));
    expect(await screen.findByText("The drug reference could not be loaded")).toBeInTheDocument();
    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByText("Nocturin")).toBeInTheDocument();
  });

  it("explains when the app is not connected to the API (demo accounts have no token)", async () => {
    open("/app/patient/drugs", backendWith(null));
    expect(await screen.findByText("The drug reference is not connected")).toBeInTheDocument();
  });

  it.each([[403, "You do not have access to the drug reference"], [429, "Too many searches"], [400, "That search is not valid"]])("handles HTTP %i", async (status, text) => {
    open("/app/patient/drugs", backendWith(() => json(status, { code: "x" })));
    expect(await screen.findByText(text)).toBeInTheDocument();
  });

  it("offers 'show more' and appends the next page", async () => {
    const second = summary({ id: "3", name: lt("Second page drug", "دارو دوم"), brandName: null });
    open("/app/patient/drugs", backendWith((p) => (p.includes("offset=0") ? json(200, { items: [summary()], total: 2, limit: 20, offset: 0 }) : json(200, { items: [second], total: 2, limit: 20, offset: 1 }))));
    await screen.findByText("Nocturin");
    await userEvent.setup().click(screen.getByRole("button", { name: "Show more" }));
    expect(await screen.findByText("Second page drug")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Show more" })).toBeNull();
  });

  it("shows Persian names in Persian and keeps RTL", async () => {
    open("/app/patient/drugs", backendWith(() => list([summary()])), "fa");
    expect(await screen.findByText("نوکتورین")).toBeInTheDocument();
    expect(document.documentElement.dir).toBe("rtl");
    expect(screen.getByText("نمایشی")).toBeInTheDocument();
  });

  it("on wide screens shows the selected medicine next to the list", async () => {
    open("/app/patient/drugs?id=11111111-1111-1111-1111-111111111111", backendWith((p) => (p.startsWith("/medications/search") ? list([summary()]) : json(200, detail()))), "en", 1400);
    expect(await screen.findByRole("heading", { name: "Nocturin", level: 2 })).toBeInTheDocument();
    expect(screen.getAllByText("Nocturin").length).toBeGreaterThan(1);
  });

  it("on narrow screens a result opens the detail page", async () => {
    open("/app/physician/drugs", backendWith((p) => (p.startsWith("/medications/search") ? list([summary()]) : json(200, detail())), "demo-physician"), "en", 500);
    await userEvent.setup().click(await screen.findByRole("link", { name: /Nocturin/ }));
    expect(await screen.findByRole("heading", { name: "Nocturin", level: 2 })).toBeInTheDocument();
  });
});

describe("drug detail", () => {
  const view = (d: MedicationDetail | Response, locale: "en" | "fa" = "en") =>
    open("/app/pharmacist/drugs/11111111-1111-1111-1111-111111111111", backendWith(() => (d instanceof Response ? d : json(200, d)), "demo-pharmacist"), locale);

  it("shows facts, ingredients, statements with their status and source, interactions and sources", async () => {
    view(detail());
    expect(await screen.findByRole("heading", { name: "Nocturin", level: 2 })).toBeInTheDocument();
    expect(screen.getAllByText(/DEMO DATA — NOT FOR CLINICAL USE/).length).toBeGreaterThan(0);
    expect(screen.getByText("Warnings")).toBeInTheDocument();
    expect(screen.getByText(/Fictional warning: may cause demo drowsiness/)).toBeInTheDocument();
    expect(screen.getAllByText("DEMO").length).toBeGreaterThan(1);
    expect(screen.getAllByText(/DEMO seed \(fictional\)/).length).toBeGreaterThan(0);
    expect(screen.getByText("with demoprilate (fictional)")).toBeInTheDocument();
    expect(screen.getByText("Moderate")).toBeInTheDocument();
    expect(screen.getByText(/Licence: Fictional test data/)).toBeInTheDocument();
    expect(screen.getByText("No official identifiers are recorded.")).toBeInTheDocument();
  });

  it("lists what is NOT recorded instead of implying there is none", async () => {
    view(detail());
    const missing = await screen.findByRole("region", { name: "Information not available" });
    expect(within(missing).getByText(/Contraindications/)).toBeInTheDocument();
    expect(within(missing).getByText(/This does not mean that none exist/)).toBeInTheDocument();
  });

  it("says so when there is no interaction information", async () => {
    view(detail({ interactions: [] }));
    expect(await screen.findByText("No interaction information is recorded.")).toBeInTheDocument();
  });

  it.each([
    ["Validated" as ValidationStatus, false, /Source-validated reference information/],
    ["Unverified" as ValidationStatus, false, /NOT VALIDATED/],
    ["NeedsValidation" as ValidationStatus, false, /NOT VALIDATED/],
  ])("%s non-demo data shows the right notice", async (validation, isDemo, notice) => {
    view(detail({ validation, isDemo }));
    expect(await screen.findByText(notice)).toBeInTheDocument();
  });

  it("marks an inactive medicine", async () => {
    view(detail({ lifecycle: "Inactive" }));
    expect(await screen.findByText("Inactive")).toBeInTheDocument();
  });

  it("handles a missing medicine and a backend that is down", async () => {
    view(json(404, {}));
    expect(await screen.findByText("This medicine is not available")).toBeInTheDocument();
    cleanup();
    open("/app/pharmacist/drugs/x", backendWith(async () => { throw new TypeError("down"); }, "demo-pharmacist"));
    expect(await screen.findByText("The drug reference could not be loaded")).toBeInTheDocument();
  });

  it("renders the Persian record with Persian labels", async () => {
    view(detail(), "fa");
    expect(await screen.findByRole("heading", { name: "نوکتورین", level: 2 })).toBeInTheDocument();
    expect(screen.getByText("هشدارها")).toBeInTheDocument();
    expect(screen.getByText("اطلاعات موجود نیست")).toBeInTheDocument();
  });
});

describe("navigation", () => {
  it.each([["demo-patient", "patient"], ["demo-physician", "physician"], ["demo-pharmacist", "pharmacist"], ["demo-pharmacy-admin", "pharmacy"], ["demo-industry", "industry"], ["demo-content-manager", "workspace"]])("%s sees the drug reference in the %s area", async (account, area) => {
    const be = new LocalMockBackend({ storage: null, initialAccountId: account });
    open(`/app/${area}`, be);
    const nav = await screen.findAllByRole("link", { name: "Drug reference" });
    expect(nav.length).toBeGreaterThan(0);
  });

  it("the system administrator area has no drug reference link", async () => {
    open("/app/admin", new LocalMockBackend({ storage: null, initialAccountId: "demo-system-admin" }));
    await screen.findAllByText("Administration");
    expect(screen.queryByRole("link", { name: "Drug reference" })).toBeNull();
  });

  it("other roles cannot open another area's drug page by URL", async () => {
    open("/app/physician/drugs", new LocalMockBackend({ storage: null, initialAccountId: "demo-patient" }));
    expect(await screen.findByTestId("unauthorized")).toBeInTheDocument();
  });
});
