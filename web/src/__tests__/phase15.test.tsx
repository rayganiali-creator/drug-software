import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../AppProviders";
import { LocalMockBackend } from "../auth/localMockBackend";
import { navByRole } from "../layouts/roles";
import { routes } from "../routes";
import { createMockServices } from "../services/mock/createMockServices";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
beforeEach(() => { Object.defineProperty(window, "innerWidth", { value: 1200, configurable: true }); });

const json = (status: number, body: unknown = {}) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
function open(reply: (p: string) => Response, account = "demo-industry", locale: "en" | "fa" = "en") {
  const paths: string[] = [];
  const be = new LocalMockBackend({ storage: null, initialAccountId: account });
  Object.assign(be, { apiFetch: vi.fn(async (p: string) => { paths.push(p); return reply(p); }) });
  render(<AppProviders locale={locale} theme="light" auth={be} services={createMockServices({ latencyMs: 0 })}><RouterProvider router={createMemoryRouter(routes, { initialEntries: ["/app/industry/insights"] })} /></AppProviders>);
  return paths;
}
const cell = (key: string, reports: number | null, label: string | null = null) => ({ key, label, reports, suppressed: reports === null });
const insights = (over: Record<string, unknown> = {}) => ({
  organizationId: "o", from: "2026-07-01", until: "2026-09-30", basis: "Real", linked: true, minCellSize: 5, productsTruncated: false,
  byProduct: [cell("m1", 8, "Demopril"), cell("unregistered", null)], byIssueType: [cell("QualityProblem", 12), cell("Other", null)], bySeverity: [cell("Mild", 12)], byMonth: [cell("2026-09", 12)], ...over,
});

describe("industry report counts", () => {
  it("is in the industry navigation only", () => {
    expect(navByRole.industry.map((n) => n.id)).toContain("insights");
    for (const r of ["patient", "physician", "pharmacist", "pharmacy"] as const) expect(navByRole[r].map((n) => n.id)).not.toContain("insights");
  });

  it("shows counts with the not-a-defect notice, hides small cells as 'fewer than k' and never shows a number for them", async () => {
    const paths = open(() => json(200, insights()));
    await waitFor(() => expect(screen.getByText("Demopril")).toBeInTheDocument());
    expect(screen.getByText(/not proof of a defect/)).toBeInTheDocument();
    expect(screen.getAllByText("fewer than 5").length).toBe(2);
    expect(screen.getByText("Products not in the reference (names not shown)")).toBeInTheDocument();
    expect(paths[0]).toMatch(/^\/organizations\/[^/]+\/industry\/insights\?basis=Real&from=\d{4}-\d{2}-\d{2}&until=\d{4}-\d{2}-\d{2}$/);
    expect(screen.queryByText(/Simulated or demo data/)).toBeNull();
    expect(screen.getByText("Period shown: 2026-07-01 to 2026-09-30")).toBeInTheDocument();
    expect(screen.getByText(/also hidden so that hidden groups cannot be worked out/)).toBeInTheDocument();
  });

  it("labels simulated or demo data loudly and asks for it separately", async () => {
    const paths = open((p) => json(200, insights({ basis: p.includes("SimulatedOrDemo") ? "SimulatedOrDemo" : "Real" })));
    await waitFor(() => expect(screen.getByText("Demopril")).toBeInTheDocument());
    await userEvent.selectOptions(screen.getByLabelText("Data"), "SimulatedOrDemo");
    await waitFor(() => expect(screen.getByText("Simulated or demo data")).toBeInTheDocument());
    expect(paths.some((p) => p.includes("basis=SimulatedOrDemo"))).toBe(true);
  });

  it("says plainly when the organization is not linked, and shows nothing else", async () => {
    open(() => json(200, insights({ linked: false, byProduct: [], byIssueType: [], bySeverity: [], byMonth: [] })));
    await waitFor(() => expect(screen.getByText("Your organization is not linked to a manufacturer")).toBeInTheDocument());
    expect(screen.queryByText("By product")).toBeNull();
  });

  it("a refusal or an offline server is explained, not rendered as empty data", async () => {
    open(() => json(403));
    await waitFor(() => expect(screen.getByText("You do not have access")).toBeInTheDocument());
    expect(document.body.textContent).not.toContain("Demopril");
  });

  it("Persian: right-to-left page with the same notice", async () => {
    open(() => json(200, insights()), "demo-industry", "fa");
    await waitFor(() => expect(screen.getByText("Demopril")).toBeInTheDocument());
    expect(document.body.textContent).toContain("نرخ بروز هم نیست");
  });
});
