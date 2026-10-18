import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { RouterProvider, createMemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../AppProviders";
import { LocalMockBackend } from "../auth/localMockBackend";
import { navByRole } from "../layouts/roles";
import { routes } from "../routes";
import { createMockServices } from "../services/mock/createMockServices";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
beforeEach(() => { Object.defineProperty(window, "innerWidth", { value: 1200, configurable: true }); });

const json = (status: number, body: unknown = {}, extra: Record<string, string> = {}) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...extra } });
const queue = (rows: unknown[], truncated: string | null = "false") => json(200, rows, truncated === null ? {} : { "X-Result-Truncated": truncated });
const row = (id: string) => ({ id, subjectId: "p1", reporterRole: "Patient", origin: "PatientReported", reportedAt: "2026-10-10T08:00:00Z", category: null, status: "Submitted", lastActivityAt: "2026-10-10T08:00:00Z", excerpt: "Rash on both arms" });

function open(path: string, account: string, reply?: (p: string) => Response | Promise<Response>) {
  const paths: string[] = [];
  const be = new LocalMockBackend({ storage: null, initialAccountId: account });
  if (reply) Object.assign(be, { apiFetch: vi.fn(async (p: string) => { paths.push(p); return reply(p); }) });
  render(<AppProviders locale="en" theme="light" auth={be} services={createMockServices({ latencyMs: 0 })}><RouterProvider router={createMemoryRouter(routes, { initialEntries: [path] })} /></AppProviders>);
  return paths;
}

describe("pages that still use sample data say so", () => {
  const mock: [string, string][] = [
    ["/app/patient", "demo-patient"], ["/app/patient/medications", "demo-patient"], ["/app/patient/checkin", "demo-patient"], ["/app/patient/profile", "demo-patient"],
    ["/app/physician", "demo-physician"], ["/app/physician/patients", "demo-physician"], ["/app/physician/prescriptions", "demo-physician"],
    ["/app/pharmacist", "demo-pharmacist"], ["/app/pharmacist/reviews", "demo-pharmacist"], ["/app/pharmacist/followups", "demo-pharmacist"],
    ["/app/pharmacy", "demo-pharmacy-admin"], ["/app/pharmacy/dispensing", "demo-pharmacy-admin"],
  ];
  it.each(mock)("%s shows the sample-data notice", async (path, account) => {
    open(path, account);
    await waitFor(() => expect(screen.getByTestId("sample-data-notice")).toBeInTheDocument());
    expect(screen.getByText("Sample data on this page")).toBeInTheDocument();
  });

  it("points the patient's sample medicines page to the stored list", async () => {
    open("/app/patient/medications", "demo-patient");
    const link = await screen.findByRole("link", { name: /Open the stored list/ });
    expect(link).toHaveAttribute("href", "/app/patient/taking");
  });

  it.each([["/app/patient/taking", "demo-patient"], ["/app/patient/adverse-events", "demo-patient"], ["/app/pharmacist/adverse-events", "demo-pharmacist"], ["/app/physician/adverse-events", "demo-physician"], ["/app/pharmacist/safety-log", "demo-pharmacist"]])(
    "%s (stored data) never shows the sample-data notice", async (path, account) => {
      open(path, account, () => json(200, []));
      await waitFor(() => expect(document.querySelector("h1")).not.toBeNull());
      expect(screen.queryByTestId("sample-data-notice")).toBeNull();
    });
});

describe("one adverse-event queue per professional, backed by the server", () => {
  it("the duplicated sample 'adr' entry is gone from the physician and pharmacist menus; the stored queue stays", () => {
    for (const role of ["physician", "pharmacist"] as const) {
      const ids = navByRole[role].map((n) => n.id);
      expect(ids).not.toContain("adr");
      expect(ids).toContain("adverse-events");
    }
  });

  it.each([["/app/physician/adr", "demo-physician"], ["/app/pharmacist/adr", "demo-pharmacist"]])("old link %s lands on the stored queue and asks the server", async (path, account) => {
    const paths = open(path, account, (p) => p.startsWith("/adverse-events/queue") ? queue([row("r1")]) : json(200, []));
    await waitFor(() => expect(screen.getByText("Rash on both arms")).toBeInTheDocument());
    expect(paths.some((p) => p.startsWith("/adverse-events/queue"))).toBe(true);
    expect(screen.queryByText(/causality/i)).toBeNull();
  });
});

describe("dashboard adverse-event tile shows the server's number or says it has none", () => {
  it.each([["/app/physician", "demo-physician", "Open adverse-event records"], ["/app/pharmacist", "demo-pharmacist", "Open adverse-event records"]])("%s: real count", async (path, account, label) => {
    open(path, account, (p) => p.startsWith("/adverse-events/queue") ? queue([row("a"), row("b"), row("c")]) : json(200, []));
    const tile = (await screen.findByText(label)).closest(".ms-stat") as HTMLElement;
    await waitFor(() => expect(tile.querySelector(".ms-stat__value")?.textContent).toBe("3"));
    expect(tile.textContent).toContain("Only records of patients you may read");
  });

  it.each([["/app/physician", "demo-physician"], ["/app/pharmacist", "demo-pharmacist"]])("%s: a refusal shows a dash, never a sample number", async (path, account) => {
    open(path, account, () => json(403));
    const tile = (await screen.findByText("Open adverse-event records")).closest(".ms-stat") as HTMLElement;
    await waitFor(() => expect(tile.querySelector(".ms-stat__value")?.textContent).toBe("—"));
    expect(tile.textContent).toContain("Not available right now");
  });

  it("without a server connection (demo account only) the tile is a dash too", async () => {
    open("/app/pharmacist", "demo-pharmacist");
    const tile = (await screen.findByText("Open adverse-event records")).closest(".ms-stat") as HTMLElement;
    await waitFor(() => expect(tile.querySelector(".ms-stat__value")?.textContent).toBe("—"));
  });
});

describe("a possibly incomplete queue never produces an exact-looking count", () => {
  const tileOf = async (label = "Open adverse-event records") => (await screen.findByText(label)).closest(".ms-stat") as HTMLElement;
  const rows200 = Array.from({ length: 200 }, (_, i) => row("x" + i));

  it.each([["/app/physician", "demo-physician"], ["/app/pharmacist", "demo-pharmacist"]])("%s: the server says the read was capped → a dash and an explanation, not 200 and not any number", async (path, account) => {
    open(path, account, (p) => p.startsWith("/adverse-events/queue") ? queue(rows200, "true") : json(200, []));
    const tile = await tileOf();
    await waitFor(() => expect(tile.textContent).toContain("Too many records to count here"));
    expect(tile.querySelector(".ms-stat__value")?.textContent).toBe("—");
    expect(tile.textContent).not.toContain("200");
  });

  it("capped with fewer visible rows (the filter removed some): still no number", async () => {
    open("/app/pharmacist", "demo-pharmacist", (p) => p.startsWith("/adverse-events/queue") ? queue([row("a"), row("b")], "true") : json(200, []));
    const tile = await tileOf();
    await waitFor(() => expect(tile.querySelector(".ms-stat__value")?.textContent).toBe("—"));
    expect(tile.textContent).not.toMatch(/\b2\b/);
  });

  it.each([[null], ["maybe"], [""]])("completeness header %j (missing or not an explicit false) is treated as possibly incomplete", async (header) => {
    open("/app/physician", "demo-physician", (p) => p.startsWith("/adverse-events/queue") ? queue([row("a")], header) : json(200, []));
    const tile = await tileOf();
    await waitFor(() => expect(tile.querySelector(".ms-stat__value")?.textContent).toBe("—"));
  });

  it("only an explicit complete answer shows the number", async () => {
    open("/app/physician", "demo-physician", (p) => p.startsWith("/adverse-events/queue") ? queue([row("a"), row("b")], "false") : json(200, []));
    const tile = await tileOf();
    await waitFor(() => expect(tile.querySelector(".ms-stat__value")?.textContent).toBe("2"));
  });
});

describe("the stored queue page discloses when its list may be incomplete", () => {
  const pages: [string, string][] = [["/app/physician/adverse-events", "demo-physician"], ["/app/pharmacist/adverse-events", "demo-pharmacist"]];
  const reply = (header: string | null, rows: unknown[] = [row("r1")]) => (p: string) => (p.startsWith("/adverse-events/queue") ? queue(rows, header) : json(200, []));

  it.each(pages)("%s: header exactly 'true' → a warning not to assume the list holds every open report; the records are still shown", async (path, account) => {
    open(path, account, reply("true"));
    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("This list may be incomplete");
    expect(alert.textContent).toContain("Do not assume this list contains every open report");
    expect(screen.getByText("Rash on both arms")).toBeInTheDocument();
  });

  it.each(pages)("%s: header exactly 'false' → no truncation warning", async (path, account) => {
    open(path, account, reply("false"));
    await screen.findByText("Rash on both arms");
    expect(screen.queryByRole("alert")).toBeNull();
    expect(document.body.textContent).not.toContain("may be incomplete");
    expect(document.body.textContent).not.toContain("cannot confirm");
  });

  it.each(pages.flatMap(([p, a]) => [null, "maybe", "", "TRUE", "True", "1"].map((h) => [p, a, h] as const)))("%s (%s): header %j (missing or unexpected) → an uncertainty notice, never a claim of completeness", async (path, account, header) => {
    open(path, account, reply(header));
    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("We cannot confirm this list is complete");
    expect(alert.textContent).toContain("Do not assume this list contains every open report");
    expect(alert.textContent).not.toContain("may be incomplete");
  });

  it.each(pages)("%s: an empty capped list still warns (an empty list is not 'nothing open')", async (path, account) => {
    open(path, account, reply("true", []));
    expect((await screen.findByRole("alert")).textContent).toContain("This list may be incomplete");
  });

  it.each(pages)("%s: a refusal shows no truncation notice and no records", async (path, account) => {
    open(path, account, (p) => (p.startsWith("/adverse-events/queue") ? json(403, {}, { "X-Result-Truncated": "true" }) : json(200, [])));
    await waitFor(() => expect(document.querySelector("h1")).not.toBeNull());
    await waitFor(() => expect(document.body.textContent).not.toContain("Loading"));
    expect(document.body.textContent).not.toContain("This list may be incomplete");
    expect(document.body.textContent).not.toContain("Rash on both arms");
  });

  it.each(pages)("%s: a server error shows no truncation notice", async (path, account) => {
    open(path, account, (p) => (p.startsWith("/adverse-events/queue") ? json(500, {}) : json(200, [])));
    await waitFor(() => expect(document.querySelector("h1")).not.toBeNull());
    expect(document.body.textContent).not.toContain("This list may be incomplete");
    expect(document.body.textContent).not.toContain("We cannot confirm");
  });

  it("the Persian wording exists for both notices", async () => {
    const fa = (await import("../shared/fa.json")).default as Record<string, string>;
    for (const k of ["adr.queue.truncated.title", "adr.queue.truncated.body", "adr.queue.unknown.title", "adr.queue.unknown.body"]) expect(fa[k]).toBeTruthy();
  });
});

describe("role boundary on the client is only a convenience", () => {
  it("a patient who opens a professional queue directly is not shown it", async () => {
    open("/app/pharmacist/adverse-events", "demo-patient", () => json(200, [row("r1")]));
    await waitFor(() => expect(document.body.textContent).not.toContain("Rash on both arms"));
  });
});

describe("care list leads to the stored adverse-event records of an active patient", () => {
  const care = (id: string, kind: string, status: string) => ({ relationship: { id: "r" + id, patientSubjectId: id, providerUserId: "u", kind, status, initiatedBy: "Patient", requestedAt: "2026-10-01T00:00:00Z", patientConsentedAt: null, providerConsentedAt: null, endedAt: null, endedBy: null, endReason: null }, patientName: "DEMO Patient " + id, providerName: null });

  it.each([["/app/pharmacist/care", "demo-pharmacist", "/app/pharmacist"], ["/app/physician/care", "demo-physician", "/app/physician"]])("%s: link only for active relationships, server still decides", async (path, account, base) => {
    open(path, account, () => json(200, [care("p-active", "Dispensing", "Active"), care("p-pending", "Treating", "PendingProvider")]));
    await waitFor(() => expect(screen.getByText("DEMO Patient p-active")).toBeInTheDocument());
    const links = screen.getAllByRole("link", { name: "Adverse events" }).filter((a) => a.getAttribute("href")?.includes("/patients/")); // the side menu has a link of the same name
    expect(links).toHaveLength(1);
    expect(links[0]).toHaveAttribute("href", `${base}/patients/p-active/adverse-events`);
  });
});
