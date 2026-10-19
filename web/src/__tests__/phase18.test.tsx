import { readFileSync } from "node:fs";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../AppProviders";
import { LocalMockBackend } from "../auth/localMockBackend";
import { routes } from "../routes";
import { createMockServices } from "../services/mock/createMockServices";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
beforeEach(() => { Object.defineProperty(window, "innerWidth", { value: 1200, configurable: true }); });

const json = (status: number, body: unknown = {}, extra: Record<string, string> = {}) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...extra } });
const row = (id: string, text = "Rash on both arms") => ({ id, subjectId: "p1", reporterRole: "Patient", origin: "PatientReported", reportedAt: "2026-10-10T08:00:00Z", category: null, status: "Submitted", lastActivityAt: "2026-10-10T08:00:00Z", excerpt: text });
const page = (rows: unknown[], truncated: string | null, cursor?: string, total?: string) =>
  json(200, rows, { ...(truncated === null ? {} : { "X-Result-Truncated": truncated }), ...(cursor ? { "X-Next-Cursor": cursor } : {}), ...(total ? { "X-Total-Count": total } : {}) });

function open(path: string, account: string, reply: (p: string) => Response | Promise<Response>) {
  const paths: string[] = [];
  const be = new LocalMockBackend({ storage: null, initialAccountId: account });
  Object.assign(be, { apiFetch: vi.fn(async (p: string) => { paths.push(p); return reply(p); }) });
  render(<AppProviders locale="en" theme="light" auth={be} services={createMockServices({ latencyMs: 0 })}><RouterProvider router={createMemoryRouter(routes, { initialEntries: [path] })} /></AppProviders>);
  return paths;
}

const pages: [string, string][] = [["/app/physician/adverse-events", "demo-physician"], ["/app/pharmacist/adverse-events", "demo-pharmacist"]];
const isQueue = (p: string) => p.startsWith("/adverse-events/queue");
const loadMore = () => screen.findByRole("button", { name: "Load more" });

describe("the stored ADR queue loads page by page", () => {
  it.each(pages)("%s: first page asks for 50, 'Load more' follows the server's cursor and appends, the end is stated", async (path, account) => {
    const paths = open(path, account, (p) => !isQueue(p) ? json(200, []) : p.includes("cursor=") ? page([row("r3", "Third page record")], "false") : page([row("r1", "First page record"), row("r2", "Second record")], "true", "c%201+/="));
    expect(await screen.findByText("First page record")).toBeInTheDocument();
    expect(paths[0]).toBe("/adverse-events/queue?status=Submitted,InReview,InformationRequested&limit=50");
    expect(screen.getByRole("alert").textContent).toContain("This list may be incomplete");
    const user = userEvent.setup();
    await user.click(await loadMore());
    expect(await screen.findByText("Third page record")).toBeInTheDocument();
    expect(screen.getByText("First page record")).toBeInTheDocument(); // appended, not replaced
    expect(paths[1]).toBe("/adverse-events/queue?status=Submitted,InReview,InformationRequested&limit=50&cursor=c%25201%2B%2F%3D"); // the opaque value is sent back as one encoded parameter
    await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());                  // the last page said "false": no warning any more
    expect(screen.queryByRole("button", { name: "Load more" })).toBeNull();
    expect(screen.getByTestId("queue-footer").textContent).toContain("End of the list as of");
  });

  it.each(pages)("%s: a record that comes back on a later page is listed once", async (path, account) => {
    open(path, account, (p) => !isQueue(p) ? json(200, []) : p.includes("cursor=") ? page([row("r1", "Same record"), row("r2", "Other record")], "false") : page([row("r1", "Same record")], "true", "c1"));
    await userEvent.setup().click(await loadMore());
    expect(await screen.findByText("Other record")).toBeInTheDocument();
    expect(screen.getAllByText("Same record")).toHaveLength(1);
  });

  it.each(pages)("%s: a failed next page keeps the rows, says so, and 'Try again' asks for the same place", async (path, account) => {
    let fail = true;
    const paths = open(path, account, (p) => !isQueue(p) ? json(200, []) : p.includes("cursor=") ? (fail ? json(500) : page([row("r2", "Recovered record")], "false")) : page([row("r1", "Kept record")], "true", "c1"));
    const user = userEvent.setup();
    await user.click(await loadMore());
    await waitFor(() => expect(screen.getByTestId("queue-footer").textContent).toContain("The next page could not be loaded"));
    expect(screen.getByText("Kept record")).toBeInTheDocument();
    fail = false;
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByText("Recovered record")).toBeInTheDocument();
    expect(paths.filter((p) => p.includes("cursor=c1"))).toHaveLength(2);
  });

  it.each(pages)("%s: a cursor the server no longer accepts offers only a refresh from the top", async (path, account) => {
    const paths = open(path, account, (p) => !isQueue(p) ? json(200, []) : p.includes("cursor=") ? json(400, { code: "cursor.invalid", errors: ["cursor.invalid"] }) : page([row("r1", "Top record")], "true", "c1"));
    const user = userEvent.setup();
    await user.click(await loadMore());
    await waitFor(() => expect(screen.getByTestId("queue-footer").textContent).toContain("no longer valid"));
    expect(screen.queryByRole("button", { name: "Load more" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Try again" })).toBeNull();
    await user.click(screen.getByRole("button", { name: "Refresh" }));
    await waitFor(() => expect(paths.filter((p) => !p.includes("cursor="))).toHaveLength(2));
    expect(await screen.findByText("Top record")).toBeInTheDocument();
  });

  it.each(pages)("%s: access that ends while paging (403) falls back to the normal refusal and no record stays on screen", async (path, account) => {
    let revoked = false;
    open(path, account, (p) => !isQueue(p) ? json(200, []) : revoked ? json(403) : page([row("r1", "Soon unauthorized")], "true", "c1"));
    const user = userEvent.setup();
    await screen.findByText("Soon unauthorized");
    revoked = true; // the reviewer's access ends before the next page is asked for
    await user.click(await loadMore());
    await waitFor(() => expect(document.body.textContent).not.toContain("Soon unauthorized"));
    expect(screen.queryByRole("button", { name: "Load more" })).toBeNull();
  });

  it.each(pages)("%s: refresh asks for the first page again; changing the filter drops appended rows and the cursor", async (path, account) => {
    const paths = open(path, account, (p) => !isQueue(p) ? json(200, []) : p.includes("cursor=") ? page([row("r2", "Appended record")], "false") : page([row("r1", p.includes("Assessed") ? "All statuses record" : "Open record")], "true", "c1"));
    const user = userEvent.setup();
    await user.click(await loadMore());
    expect(await screen.findByText("Appended record")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Refresh" }));
    await waitFor(() => expect(screen.queryByText("Appended record")).toBeNull());
    expect(paths.filter((p) => !p.includes("cursor="))).toHaveLength(2);

    await user.click(await loadMore());
    expect(await screen.findByText("Appended record")).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Show"), "all");
    expect(await screen.findByText("All statuses record")).toBeInTheDocument();
    expect(screen.queryByText("Appended record")).toBeNull();
    expect(paths[paths.length - 1]).toMatch(/status=Submitted,InReview,InformationRequested,Assessed,Closed&limit=50$/); // a new filter starts from the top with no cursor
  });
});

describe("the notice always follows the last page the server sent", () => {
  it.each(pages)("%s: 'true' without a cursor warns and offers no 'Load more' (nothing to follow)", async (path, account) => {
    open(path, account, (p) => isQueue(p) ? page([row("r1")], "true") : json(200, []));
    expect((await screen.findByRole("alert")).textContent).toContain("This list may be incomplete");
    expect(screen.queryByRole("button", { name: "Load more" })).toBeNull();
    expect(screen.getByRole("button", { name: "Refresh" })).toBeInTheDocument();
  });

  it.each(pages.flatMap(([p, a]) => [null, "maybe", "", "TRUE"].map((h) => [p, a, h] as const)))("%s (%s): header %j with a cursor present never offers 'Load more' and never claims completeness", async (path, account, header) => {
    open(path, account, (p) => isQueue(p) ? page([row("r1")], header, "c1") : json(200, []));
    expect((await screen.findByRole("alert")).textContent).toContain("We cannot confirm this list is complete");
    expect(screen.queryByRole("button", { name: "Load more" })).toBeNull();
    expect(screen.getByTestId("queue-footer").textContent).not.toContain("End of the list");
  });

  it.each(pages)("%s: an exact 'false' on the first page shows no warning, no 'Load more', and the end line", async (path, account) => {
    open(path, account, (p) => isQueue(p) ? page([row("r1")], "false", "ignored-cursor") : json(200, []));
    await screen.findByText("Rash on both arms");
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByRole("button", { name: "Load more" })).toBeNull(); // a cursor next to "false" is ignored
    expect(screen.getByTestId("queue-footer").textContent).toContain("End of the list as of");
  });

  it.each(pages)("%s: an empty complete list says nothing is listed without a warning", async (path, account) => {
    open(path, account, (p) => isQueue(p) ? page([], "false") : json(200, []));
    expect(await screen.findByText("No records yet")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
  });
});

describe("the Persian wording exists for everything the queue footer says", () => {
  it("has every key", async () => {
    const fa = (await import("../shared/fa.json")).default as Record<string, string>;
    for (const k of ["adr.queue.more", "adr.queue.loadingMore", "adr.queue.moreError", "adr.queue.expired", "adr.queue.end", "adr.queue.asOf", "adr.queue.refresh", "adr.tile.incomplete"]) expect(fa[k]).toBeTruthy();
    expect(fa["adr.queue.end"]).toContain("{time}");
  });
});

// A page that still runs on the in-browser sample data must be labelled, and a labelled page must really be sample data: both directions, checked from the route table itself.
describe("sample pages are labelled, stored pages are not", () => {
  const routeSource = readFileSync("src/routes.tsx", "utf8");
  const entries = [...routeSource.matchAll(/(page|mockPage)\(\(\) => import\("\.\/(features\/[^"]+)"\), "(\w+)"/g)].map((m) => ({ kind: m[1] ?? "", module: m[2] ?? "", name: m[3] ?? "", source: readFileSync(`src/${m[2]}.tsx`, "utf8") }));
  const usesSampleData = (src: string) => /\buseAsync\(|\buseServices\(/.test(src);
  // Pages on the sample services that are NOT wrapped by the route notice, and why. A new entry here needs a reason a reviewer can read.
  const exempt: Record<string, string> = {
    "features/designSystem/DesignSystem": "component showcase; shows no patient, medicine or clinical record",
    "features/industry/Industry": "legacy industry pages; each carries its own DEMO badge (demo.badge)",
  };

  it("found the route table", () => { expect(entries.length).toBeGreaterThan(40); });

  it("every route page on the sample services is wrapped by the sample-data notice, unless listed with a reason", () => {
    const unlabelled = entries.filter((e) => e.kind === "page" && usesSampleData(e.source) && !(e.module in exempt)).map((e) => `${e.module}#${e.name}`);
    expect(unlabelled).toEqual([]);
  });

  it("every route wrapped by the sample-data notice really uses the sample services (a stored page is never labelled as sample)", () => {
    const mislabelled = entries.filter((e) => e.kind === "mockPage" && !usesSampleData(e.source)).map((e) => `${e.module}#${e.name}`);
    expect(mislabelled).toEqual([]);
  });

  it("the exemptions are still true: the legacy industry pages keep their DEMO badge; nothing else slipped in", () => {
    expect(readFileSync("src/features/industry/Industry.tsx", "utf8")).toContain('t("demo.badge")');
    for (const e of entries.filter((x) => x.kind === "page" && usesSampleData(x.source))) expect(Object.keys(exempt)).toContain(e.module);
  });

  it("the stored ADR pages never use the sample services", () => {
    const adr = readFileSync("src/features/adr/AdverseEvents.tsx", "utf8") + readFileSync("src/features/adr/hooks.ts", "utf8");
    expect(usesSampleData(adr)).toBe(false);
  });
});
