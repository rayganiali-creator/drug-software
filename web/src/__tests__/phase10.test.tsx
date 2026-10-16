import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../AppProviders";
import { LocalMockBackend } from "../auth/localMockBackend";
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

const eventNotice = "This is an internal pharmacy record for review and learning. It is not a regulatory report and it is not a nationally mandated classification. Do not enter patient names or other identifying details.";
const concernNotice = "A product concern is a suspicion. It does not show that a product is defective, and nothing here has been sent to a manufacturer, regulator or insurer.";
const ev = (over: Record<string, unknown> = {}) => ({
  id: "v1", organizationId: "o1", kind: "DispensingError", reachedPatient: "Yes", errorExamples: ["ExpiredMedicine"], description: "Handed over an expired box", occurredOn: null, medicineAsReported: null,
  howDetectedOrIntercepted: null, immediateActionTaken: null, reporterRole: "Pharmacist", reportedAt: "2026-10-10T08:00:00Z", status: "Submitted", missingInformation: ["occurred_on.unknown", "medicine.not_stated", "how_detected.not_stated"],
  linkedAdverseEventId: null, linkedProductConcernId: null, lastActivityAt: "2026-10-10T08:00:00Z", version: 2, notice: eventNotice, externalStatus: "NotSubmitted", persianWordingStatus: "AwaitingClinicalReview", ...over,
});
const cn = (over: Record<string, unknown> = {}) => ({
  id: "c1", organizationId: "o1", type: "AppearanceOrPackaging", typeBasis: "ProvisionalNotSourceDefined", description: "Tablets look discoloured", observedOn: null, linkState: "Unmatched", linked: null, linkedRecordVersion: null,
  linkedVerificationAtLinking: null, asReported: { productName: "Demopril", batchNumber: "LOT-TYPED", expiryDate: null, manufacturerName: null, gtin: null },
  flags: ["product_record.not_linked", "expiry.missing", "manufacturer.missing"], reporterRole: "Pharmacist", reportedAt: "2026-10-10T08:00:00Z", status: "Submitted", linkedAdverseEventId: null, linkedSafetyEventId: null,
  manufacturerReportProposed: false, manufacturerReportProposedAt: null, lastActivityAt: "2026-10-10T08:00:00Z", version: 3, notice: concernNotice, externalStatus: "NotSubmitted", persianWordingStatus: "AwaitingClinicalReview", ...over,
});
const sum = (over: Record<string, unknown> = {}) => ({ id: "v1", recordKind: "SafetyEvent", label: "DispensingError", status: "Submitted", reportedAt: "2026-10-10T08:00:00Z", lastActivityAt: "2026-10-10T08:00:00Z", excerpt: "Handed over an expired box", ...over });

describe("pharmacy safety log", () => {
  it("shows the internal-record notice, the examples note, and sends only what was entered (unknown stays null); choosing near miss sets 'No'", async () => {
    const calls = open((p, i) => i?.method === "POST" ? json(201, { event: ev(), entries: [] }) : p.endsWith("/safety-events") ? json(200, [sum()]) : json(404), "/app/pharmacist/safety-log");
    expect(await screen.findByText(eventNotice)).toBeInTheDocument();
    expect(await screen.findByText("Handed over an expired box")).toBeInTheDocument();
    expect(screen.getByText(/They are not a complete or official list\./)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /send|submit to|report to/i })).not.toBeInTheDocument();
    const user = userEvent.setup();
    await user.selectOptions(screen.getByLabelText("What kind of record?"), "NearMiss");
    expect(screen.getByLabelText("Did it reach the patient?")).toHaveValue("No");
    await user.type(screen.getByLabelText("What happened?"), "Caught at the final check");
    await user.click(screen.getByLabelText("Expired medicine"));
    await user.click(screen.getByRole("button", { name: "Save this record" }));
    await waitFor(() => expect(calls.some((c) => c.method === "POST")).toBe(true));
    const post = calls.find((c) => c.method === "POST")!;
    expect(post.path).toMatch(/^\/organizations\/[^/]+\/safety-events$/);
    expect(post.body).toMatchObject({ kind: "NearMiss", reachedPatient: "No", errorExamples: ["ExpiredMedicine"], description: "Caught at the final check", occurredOn: null, medicineAsReported: null, howDetectedOrIntercepted: null, immediateActionTaken: null });
  });

  it("the detail shows the original, what is not known, no external submission, and changes status with the record version", async () => {
    const calls = open((_p, i) => i?.method === "POST" ? json(200, { event: ev({ status: "InReview", version: 3 }), entries: [] }) : json(200, { event: ev(), entries: [] }), "/app/pharmacist/safety-log/v1");
    expect(await screen.findByText("As originally recorded (never edited)")).toBeInTheDocument();
    expect(screen.getByText("The date is not known.")).toBeInTheDocument();
    expect(screen.getByText("The medicine was not named.")).toBeInTheDocument();
    expect(screen.getByText("Not submitted anywhere")).toBeInTheDocument();
    expect(screen.getByText("Dispensing error (reached the patient)")).toBeInTheDocument();
    const user = userEvent.setup();
    await user.selectOptions(screen.getByLabelText("Change the status"), "InReview");
    await user.click(screen.getAllByRole("button", { name: "Save" })[0]!);
    await waitFor(() => expect(calls.some((c) => c.method === "POST")).toBe(true));
    expect(calls.find((c) => c.method === "POST")!.body).toEqual({ status: "InReview", note: null, expectedVersion: 2 });
  });

  it("a refusal to close without a note is explained in words", async () => {
    open((_p, i) => i?.method === "POST" ? json(400, { code: "note.required", errors: ["note.required"] }) : json(200, { event: ev({ status: "InReview" }), entries: [] }), "/app/pharmacist/safety-log/v1");
    const user = userEvent.setup();
    await screen.findByText("As originally recorded (never edited)");
    await user.selectOptions(screen.getByLabelText("Change the status"), "Closed");
    await user.click(screen.getAllByRole("button", { name: "Save" })[0]!);
    expect(await screen.findByText("A note is required to close or reopen a record.")).toBeInTheDocument();
  });
});

describe("product concerns", () => {
  it("an unmatched concern says what was typed and what is unknown, calls its types provisional and never says the product is defective", async () => {
    open(() => json(200, { concern: cn(), entries: [] }), "/app/pharmacist/product-concerns/c1");
    expect(await screen.findByText("Not matched to any batch record")).toBeInTheDocument();
    expect(screen.getByText("As typed by the reporter")).toBeInTheDocument();
    expect(screen.getByText("LOT-TYPED")).toBeInTheDocument();
    expect(screen.getByText("Not linked to a batch record.")).toBeInTheDocument();
    expect(screen.getByText("No expiry date was typed.")).toBeInTheDocument();
    expect(screen.getByText(/provisional labels of MedSmarter/)).toBeInTheDocument();
    expect(screen.getByText(concernNotice)).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/\bconfirmed defect|is defective\b(?!,)/i);
  });

  it("a linked concern shows the copied identifiers beside the typed ones and says when they differ, without choosing", async () => {
    open(() => json(200, { concern: cn({ linkState: "LinkedToProductRecord", linked: { productName: "Demopril", batchNumber: "LOT-RECORD", expiryDate: "2027-01-01", manufacturerName: "Fictional Maker", gtin: null }, linkedRecordVersion: 2, linkedVerificationAtLinking: "SelfReported", flags: ["batch.differs_from_linked_record"] }), entries: [] }), "/app/pharmacist/product-concerns/c1");
    expect(await screen.findByText("Copied from the linked batch record")).toBeInTheDocument();
    expect(screen.getByText("LOT-RECORD")).toBeInTheDocument();
    expect(screen.getByText("LOT-TYPED")).toBeInTheDocument();
    expect(screen.getByText("The typed batch differs from the linked record. Both are kept; check the package.")).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/patient id|subjectId/i);
  });

  it("the manufacturer-report note is a human note only: disabled on a first report, enabled after review, says it sends nothing, and posts the note with the version", async () => {
    let status = "Submitted";
    const calls = open((_p, i) => i?.method === "POST" ? json(200, { concern: cn({ status: "InReview", manufacturerReportProposed: true }), entries: [] }) : json(200, { concern: cn({ status }), entries: [] }), "/app/pharmacist/product-concerns/c1");
    expect(await screen.findByText(/It creates, queues and sends nothing\./)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Record this note" })).toBeDisabled();
    cleanup();
    status = "InReview";
    const calls2 = open((_p, i) => i?.method === "POST" ? json(200, { concern: cn({ status: "InReview", manufacturerReportProposed: true }), entries: [] }) : json(200, { concern: cn({ status }), entries: [] }), "/app/pharmacist/product-concerns/c1");
    const user = userEvent.setup();
    await user.type(await screen.findByLabelText("Why it may be worth considering (required)"), "Same appearance reported twice");
    await user.click(screen.getByRole("button", { name: "Record this note" }));
    await waitFor(() => expect(calls2.some((c) => c.method === "POST")).toBe(true));
    expect(calls2.find((c) => c.method === "POST")).toMatchObject({ path: "/product-concerns/c1/manufacturer-report-proposal", body: { note: "Same appearance reported twice", expectedVersion: 3 } });
    expect(calls.filter((c) => c.method === "POST")).toHaveLength(0);
  });

  it("the form sends typed identifiers as typed, a null link when none is given, and asks for no guess", async () => {
    const calls = open((p, i) => i?.method === "POST" ? json(201, { concern: cn(), entries: [] }) : p.endsWith("/product-concerns") ? json(200, []) : json(404), "/app/pharmacist/product-concerns");
    const user = userEvent.setup();
    await user.type(await screen.findByLabelText("What is the concern?"), "Cracked blister packs");
    await user.type(screen.getByLabelText("Batch / lot, as read (optional)"), "LOT-77");
    await user.click(screen.getByRole("button", { name: "Save this record" }));
    await waitFor(() => expect(calls.some((c) => c.method === "POST")).toBe(true));
    expect(calls.find((c) => c.method === "POST")!.body).toMatchObject({ type: "AppearanceOrPackaging", description: "Cracked blister packs", productRecord: null, asReported: { productName: null, batchNumber: "LOT-77", expiryDate: null, manufacturerName: null, gtin: null } });
    expect(screen.getByText("Only what is typed here is kept. Nothing is looked up or filled in for you.")).toBeInTheDocument();
  });

  it("Persian renders with the unreviewed banner and no raw keys, and pharmacy administrators have the same pages", async () => {
    open(() => json(200, { concern: cn(), entries: [] }), "/app/pharmacy/product-concerns/c1", "demo-pharmacy-admin", "fa");
    expect(await screen.findByText("متن فارسی هنوز بازبینی نشده است")).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/\bph\.[a-z]/i);
    expect(within(document.body).getByText("تطبیق داده نشده", { exact: false })).toBeInTheDocument();
  });
});
