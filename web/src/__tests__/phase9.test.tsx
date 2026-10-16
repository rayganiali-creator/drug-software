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
function open(reply: (p: string, init?: RequestInit) => Response | Promise<Response>, path: string, account = "demo-patient", locale: "en" | "fa" = "en", connected = true) {
  const calls: Call[] = [];
  const be = new LocalMockBackend({ storage: null, initialAccountId: account });
  if (connected) Object.assign(be, { apiFetch: vi.fn(async (p: string, i?: RequestInit) => { calls.push({ method: i?.method ?? "GET", path: p, body: i?.body ? JSON.parse(String(i.body)) : undefined }); return reply(p, i); }) });
  render(<AppProviders locale={locale} theme="light" auth={be} services={createMockServices({ latencyMs: 0 })}><RouterProvider router={createMemoryRouter(routes, { initialEntries: [path] })} /></AppProviders>);
  return calls;
}

const SIGNAL = "This record is a signal for human review. It is not proof that a medicine caused anything.";
const INTERNAL = "This is an internal record. It has not been submitted to any regulator, manufacturer or insurer, and nothing here sends it anywhere.";

const event = (over: Record<string, unknown> = {}, pro = true) => ({
  id: "e1", subjectId: "s1", reporterRole: "Patient", origin: "PatientReported", reportedAt: "2026-10-10T08:00:00Z",
  original: { eventDescription: "Itchy rash on both arms", onsetAt: null, resolvedAt: null, outcomeAsStated: null, additionalDetails: null, doseAndAdministrationAsReported: null },
  effective: { onsetAt: { value: null, provenance: "NotRecorded", entryId: null }, resolvedAt: { value: null, provenance: "NotRecorded", entryId: null }, outcomeAsStated: { value: null, provenance: "NotRecorded", entryId: null } },
  suspectedMedications: [{ patientMedicationId: null, nameAsReported: "a white tablet", displayName: null, state: "ReportedNameOnly" }], concomitantMedications: [],
  category: pro ? "Unclassified" : null, status: "Submitted", missingInformation: ["onset.unknown", "resolution.unknown_or_ongoing", "medication.not_linked"], lastActivityAt: "2026-10-10T08:00:00Z", version: 3,
  signalNotice: SIGNAL, internalRecordNotice: INTERNAL, externalStatus: "NotSubmitted", persianWordingStatus: "AwaitingClinicalReview", ...over,
});
const detail = (over: Record<string, unknown> = {}, entries: unknown[] = [], pro = true) => ({ event: event(over, pro), entries });
const summary = (over: Record<string, unknown> = {}) => ({ id: "e1", subjectId: "s1", reporterRole: "Patient", origin: "PatientReported", reportedAt: "2026-10-10T08:00:00Z", category: null, status: "Submitted", lastActivityAt: "2026-10-10T08:00:00Z", excerpt: "Itchy rash on both arms", ...over });
const defs = { definitions: [
  { category: "AdverseEvent", term: "Adverse event (AE)", definition: "Any untoward medical occurrence that may present during treatment with a medicine or vaccine, but which does not necessarily have a causal relationship.", source: { document: "WHO — The global smart pharmacovigilance strategy (2025)", section: "Glossary", page: "printed p. xiii", kind: "SupplementaryDocument", isLectureSlide: false }, note: "", persianWordingStatus: "AwaitingClinicalReview" },
], signalNotice: SIGNAL, internalRecordNotice: INTERNAL, persianWordingStatus: "AwaitingClinicalReview" };

describe("patient adverse-event page", () => {
  it("shows the signal and internal-record notices, a no-emergency-service note, the patient's records without any category, and a form that never fills in unknown values", async () => {
    const calls = open((p, i) => i?.method === "POST" ? json(201, detail({}, [], false)) : p.includes("/medications") ? json(200, []) : p.includes("/adverse-events") ? json(200, [summary()]) : json(404), "/app/patient/adverse-events");
    expect(await screen.findByText(SIGNAL)).toBeInTheDocument();
    expect(screen.getByText(INTERNAL)).toBeInTheDocument();
    expect(screen.getByText(/does not send urgent alerts/)).toBeInTheDocument();
    expect(await screen.findByText("Itchy rash on both arms")).toBeInTheDocument();
    expect(screen.queryByText("Not classified")).not.toBeInTheDocument(); // the category is a professional label
    expect(screen.getByText("The patient's own account (not verified)")).toBeInTheDocument();

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("What happened?"), "Dizzy after lunch");
    const suspected = screen.getByRole("group", { name: "Medicines that may be involved (optional)" });
    await user.type(within(suspected).getByLabelText("Or a name as given"), "blue tablet");
    await user.click(within(suspected).getByRole("button", { name: "Add medicine" }));
    await user.click(screen.getByRole("button", { name: "Save this record" }));
    await waitFor(() => expect(calls.some((c) => c.method === "POST")).toBe(true));
    const post = calls.find((c) => c.method === "POST")!;
    expect(post.path).toMatch(/\/patients\/.+\/adverse-events$/);
    expect(post.body).toMatchObject({ eventDescription: "Dizzy after lunch", onsetAt: null, resolvedAt: null, outcomeAsStated: null, additionalDetails: null, doseAndAdministrationAsReported: null, concomitantMedications: [], suspectedMedications: [{ patientMedicationId: null, nameAsReported: "blue tablet" }] });
    expect(typeof (post.body as { clientRequestId: string }).clientRequestId).toBe("string");
  });

  it("dose and other medicines are sent as typed, kept apart from the suspected medicines, and an empty list is not read as 'none taken'", async () => {
    const calls = open((p, i) => i?.method === "POST" ? json(201, detail({}, [], false)) : p.includes("/medications") ? json(200, []) : p.includes("/adverse-events") ? json(200, []) : json(404), "/app/patient/adverse-events");
    const user = userEvent.setup();
    await user.type(await screen.findByLabelText("What happened?"), "Rash");
    await user.type(screen.getByLabelText("Dose and how it was taken, as described (optional)"), "one in the morning, I think");
    const others = screen.getByRole("group", { name: "Other medicines taken at the time (optional)" });
    await user.type(within(others).getByLabelText("Or a name as given"), "herbal tea");
    await user.click(within(others).getByRole("button", { name: "Add medicine" }));
    await user.click(screen.getByRole("button", { name: "Save this record" }));
    await waitFor(() => expect(calls.some((c) => c.method === "POST")).toBe(true));
    expect(calls.find((c) => c.method === "POST")!.body).toMatchObject({ doseAndAdministrationAsReported: "one in the morning, I think", suspectedMedications: [], concomitantMedications: [{ patientMedicationId: null, nameAsReported: "herbal tea" }] });
    expect(screen.getByText("Listing a medicine here does not mean it is suspected.")).toBeInTheDocument();
  });

  it("says plainly when the app is not connected to a server instead of pretending to work", async () => {
    open(() => json(404), "/app/patient/adverse-events", "demo-patient", "en", false);
    expect(await screen.findByText(/not connected|Not connected/i)).toBeInTheDocument();
  });
});

describe("record detail", () => {
  it("a professional sees the original unedited, what is not known, the default 'Not classified' and no seriousness or causality control", async () => {
    open((p) => p.includes("/definitions") ? json(200, defs) : json(200, detail()), "/app/physician/adverse-events/e1", "demo-physician");
    expect(await screen.findByText("As originally reported (never edited)")).toBeInTheDocument();
    expect(screen.getByText(SIGNAL)).toBeInTheDocument();
    expect(screen.getAllByText("Not classified").length).toBeGreaterThan(0);
    expect(screen.getByText("Not submitted anywhere")).toBeInTheDocument();
    expect(screen.getByText("The start is not known.")).toBeInTheDocument();
    expect(screen.getByText("The end is not known (it may be ongoing).")).toBeInTheDocument();
    expect(screen.getByText("Only a name as given; not matched to anything")).toBeInTheDocument();
    expect(screen.getByText("None named (this is not the same as none taken).")).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/seriousness (grade|score)\b(?! and)|causality (score|grade)\b(?!\.)/i);
    expect(screen.queryByRole("button", { name: /submit to|send to|report to/i })).not.toBeInTheDocument();
  });

  it("classifying needs a reason, sends the category with the record version, and keeps definitions labelled as supplementary, not lecture slides", async () => {
    const calls = open((p, i) => p.includes("/definitions") ? json(200, defs) : i?.method === "POST" ? json(200, detail({ category: "AdverseEvent", version: 4 })) : json(200, detail()), "/app/physician/adverse-events/e1", "demo-physician");
    const user = userEvent.setup();
    await screen.findByText("Classification");
    const save = screen.getAllByRole("button", { name: "Save" })[0]!;
    expect(save).toBeDisabled();
    await user.selectOptions(screen.getByLabelText("Set the category"), "AdverseEvent");
    await user.type(screen.getByLabelText("Reason (required)"), "Happened during treatment; cause not established");
    expect(save).toBeEnabled();
    await user.click(save);
    await waitFor(() => expect(calls.some((c) => c.path.endsWith("/classification"))).toBe(true));
    expect(calls.find((c) => c.path.endsWith("/classification"))!.body).toEqual({ category: "AdverseEvent", reason: "Happened during treatment; cause not established", expectedVersion: 3 });

    await user.click(screen.getByText(/Definitions \(quoted from supplementary documents, not lecture slides\)/));
    expect(await screen.findByText(/does not necessarily have a causal relationship/)).toBeInTheDocument();
    expect(screen.getByText(/WHO — The global smart pharmacovigilance strategy \(2025\), Glossary, printed p\. xiii/)).toBeInTheDocument();
  });

  it("the template summary is labelled as not a conclusion, says no language model was used and is not part of the record", async () => {
    open((p, i) => p.includes("/definitions") ? json(200, defs) : i?.method === "POST" ? json(200, {
      origin: "Template:no-model", label: "Template summary of the record — not a conclusion", generatedAt: "2026-10-11T08:00:00Z", summary: [{ text: "Event, as written: \"Itchy rash\"", fromSection: "original.eventDescription" }],
      missingInformation: ["onset.unknown"], uncertainty: ["This record does not show whether any medicine caused the event."], notice: "x", persianWordingStatus: "AwaitingClinicalReview" }) : json(200, detail()), "/app/physician/adverse-events/e1", "demo-physician");
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Show a template summary" }));
    const region = await screen.findByRole("region", { name: "Template summary of the record — not a conclusion" });
    expect(within(region).getByText(/not a conclusion and it was not checked by a professional/)).toBeInTheDocument();
    expect(screen.getByText(/No language model was used/)).toBeInTheDocument();
    expect(within(region).getByText("This record does not show whether any medicine caused the event.")).toBeInTheDocument();
  });

  it("the patient's view has no professional panel, no category and only the entries the server returned", async () => {
    open(() => json(200, detail({}, [{ id: "n1", kind: "InformationRequest", authorRole: "Physician", authorUserId: null, at: "2026-10-11T08:00:00Z", text: "When did the rash start?", visibility: "PatientVisible", provenance: "ProfessionalReview", fromStatus: "Submitted", toStatus: "InformationRequested", fromCategory: null, toCategory: null, supplementOnsetAt: null, supplementResolvedAt: null, supplementOutcomeAsStated: null }], false)), "/app/patient/adverse-events/e1");
    expect(await screen.findByText("When did the rash start?")).toBeInTheDocument();
    expect(screen.queryByText("Professional review")).not.toBeInTheDocument();
    expect(screen.queryByText("Classification")).not.toBeInTheDocument();
    expect(screen.queryByText("Not classified")).not.toBeInTheDocument();
  });

  it("a value that is already known cannot be filled again, only what is still unknown is offered", async () => {
    open(() => json(200, detail({ effective: { onsetAt: { value: "2026-10-01T00:00:00Z", provenance: "Original", entryId: null }, resolvedAt: { value: null, provenance: "NotRecorded", entryId: null }, outcomeAsStated: { value: "settled", provenance: "SupplementByPatient", entryId: "x" } } }, [], false)), "/app/patient/adverse-events/e1");
    await screen.findByText("Add something that was not known");
    expect(screen.getAllByLabelText("Ended").length).toBe(1);
    expect(screen.queryByLabelText("Started")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Outcome, as stated")).not.toBeInTheDocument();
    expect(screen.getByText(/added later by the patient/)).toBeInTheDocument();
  });

  it("a version conflict is explained in words, not shown as a code", async () => {
    open((p, i) => p.includes("/definitions") ? json(200, defs) : i?.method === "POST" ? json(409, { code: "version.mismatch", errors: ["version.mismatch"] }) : json(200, detail()), "/app/physician/adverse-events/e1", "demo-physician");
    const user = userEvent.setup();
    await screen.findByText("Classification");
    await user.selectOptions(screen.getByLabelText("Set the category"), "NearMiss");
    await user.type(screen.getByLabelText("Reason (required)"), "intercepted");
    await user.click(screen.getAllByRole("button", { name: "Save" })[0]!);
    expect(await screen.findByText("The record was changed meanwhile. Reload it and try again.")).toBeInTheDocument();
  });

  it("Persian renders with the unreviewed-wording banner and no raw keys", async () => {
    open((p) => p.includes("/definitions") ? json(200, defs) : json(200, detail()), "/app/physician/adverse-events/e1", "demo-physician", "fa");
    expect(await screen.findByText("متن فارسی هنوز بازبینی نشده است")).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/\badr\.[a-z]/i);
  });
});

describe("queue", () => {
  it("lists what the server returned, asks only for open statuses by default and offers no way to send anything outside", async () => {
    const calls = open((p) => p.includes("/queue") ? json(200, [summary({ category: "Unclassified" })]) : json(404), "/app/physician/adverse-events", "demo-physician");
    expect(await screen.findByText("Itchy rash on both arms")).toBeInTheDocument();
    expect(calls[0]?.path).toBe("/adverse-events/queue?status=Submitted,InReview,InformationRequested");
    expect(screen.getByText("Not classified")).toBeInTheDocument();
    expect(screen.getByText("Adverse-event records to review")).toBeInTheDocument();
  });
});
