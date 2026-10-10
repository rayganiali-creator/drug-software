import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../AppProviders";
import { AssistantApi } from "../ai/assistantApi";
import type { EvidenceItem, GroundedAnswer } from "../ai/types";
import { LocalMockBackend } from "../auth/localMockBackend";
import { routes } from "../routes";
import { createMockServices } from "../services/mock/createMockServices";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
beforeEach(() => { Object.defineProperty(window, "innerWidth", { value: 900, configurable: true }); });

const json = (status: number, body: unknown = {}) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const item = (over: Partial<EvidenceItem> = {}): EvidenceItem => ({
  id: "E1", medicationId: "m1", medicationName: "Nocturin", kind: "Warning", text: "Fictional warning: may cause demo drowsiness.", qualifier: null,
  source: { sourceId: "s1", name: "DEMO seed (fictional)", version: "demo-0.1", publisher: "AI MedSmarter test fixtures", receivedAt: "2026-09-01T00:00:00Z", validation: "Demo" },
  validation: "Demo", isDemo: true, stale: false, sourceDateUnknown: false, ...over,
});

const answer = (over: Partial<GroundedAnswer> = {}): GroundedAnswer => ({
  text: "[MOCK AI - DEMO ONLY - NOT FOR CLINICAL USE] No language model was used.\n\nNocturin: Warning ([E1]).", provider: "mock", isMock: true, answered: true, notice: "DEMO DATA - NOT FOR CLINICAL USE", error: "None",
  patientContextUsed: false, patientContextNote: null, status: "Answered", reason: "answered",
  evidence: { medications: [], items: [item()], conflicts: [], missingInformation: [], limitations: ["evidence.publication_date_not_recorded", "evidence.demo_data"], quality: "DemoOnly", quarantinedCount: 0, excludedCount: 0 },
  evidenceQuality: "DemoOnly", limitations: ["evidence.publication_date_not_recorded", "evidence.demo_data"], missingInformation: [], nextStep: "ConsultProfessional",
  generation: { provider: "mock", kind: "Mock", model: "mock-deterministic-2", isMock: true, external: false }, contractVersion: "ai-answer-1", ...over,
});

const empty = { medications: [], items: [], conflicts: [], missingInformation: [], limitations: ["evidence.none_found"], quality: "None" as const, quarantinedCount: 0, excludedCount: 0 };

function open(reply: ((init?: RequestInit) => Response | Promise<Response>) | null, locale: "en" | "fa" = "en", path = "/app/patient/assistant") {
  const be = new LocalMockBackend({ storage: null, initialAccountId: "demo-patient" });
  if (reply) Object.assign(be, { apiFetch: vi.fn(async (_p: string, i?: RequestInit) => reply(i)) });
  const view = render(<AppProviders locale={locale} theme="light" auth={be} services={createMockServices({ latencyMs: 0 })}><RouterProvider router={createMemoryRouter(routes, { initialEntries: [path] })} /></AppProviders>);
  return { be, view };
}

async function askQuestion(text: string) {
  const box = await screen.findByLabelText(/Your question|پرسش شما/);
  await userEvent.type(box, text);
  await userEvent.click(screen.getByRole("button", { name: /^(Ask|پرسیدن)$/ }));
}

describe("AssistantApi", () => {
  it("posts the question and reads the structured answer, also from a 503 (the evidence is still shown)", async () => {
    const f = vi.fn(async () => json(200, answer()));
    const a = await new AssistantApi(f).ask({ question: "q", locale: "en", includePatientContext: false });
    expect(a.status).toBe("Answered");
    const [path, init] = f.mock.calls[0] as unknown as [string, RequestInit];
    expect(path).toBe("/ai/medication-assistant");
    expect(JSON.parse(String(init.body))).toMatchObject({ question: "q", locale: "en", includePatientContext: false });
    expect((await new AssistantApi(async () => json(503, answer({ status: "Unavailable" }))).ask({ question: "q", locale: "en", includePatientContext: false })).status).toBe("Unavailable");
  });
  it.each([[401, "unauthorized"], [403, "unauthorized"], [429, "rateLimited"], [400, "invalid"], [500, "server"]])("maps HTTP %i to %s", async (status, kind) => {
    await expect(new AssistantApi(async () => json(status)).ask({ question: "q", locale: "en", includePatientContext: false })).rejects.toMatchObject({ kind });
  });
  it("reports no connection, a lost network and a timeout as different things", async () => {
    await expect(new AssistantApi(undefined).ask({ question: "q", locale: "en", includePatientContext: false })).rejects.toMatchObject({ kind: "notConnected" });
    await expect(new AssistantApi(async () => { throw new TypeError("offline"); }).ask({ question: "q", locale: "en", includePatientContext: false })).rejects.toMatchObject({ kind: "network" });
    const never = (_p: string, init?: RequestInit) => new Promise<Response>((_r, rej) => init?.signal?.addEventListener("abort", () => rej(new DOMException("aborted", "AbortError"))));
    await expect(new AssistantApi(never, 20).ask({ question: "q", locale: "en", includePatientContext: false })).rejects.toMatchObject({ kind: "timeout" });
  });
});

describe("assistant page", () => {
  it("shows the scripted prototype, clearly labelled, when there is no API connection", async () => {
    open(null);
    expect(await screen.findByText("Prototype conversation")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open source-based assistant" })).toBeInTheDocument();
  });

  it("shows the source-based assistant when signed in through the API, with its limits stated up front", async () => {
    open(() => json(200, answer()));
    expect(await screen.findByText("Medication information (source-based)")).toBeInTheDocument();
    expect(screen.queryByText("Prototype conversation")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Scripted prototype conversation" })).toBeInTheDocument();
  });

  it("an answer keeps the generated text apart from the evidence, labels MOCK, and shows source details without any number", async () => {
    open(() => json(200, answer()));
    await askQuestion("Tell me about Nocturin");
    expect(await screen.findByText("Answer")).toBeInTheDocument();
    expect(screen.getByText("MOCK: no language model was used")).toBeInTheDocument();
    expect(screen.getAllByText("DEMO DATA").length).toBeGreaterThan(0);
    const list = screen.getByRole("list", { name: "Sources and evidence" });
    const row = within(list).getByText("Fictional warning: may cause demo drowsiness.").closest("li")!;
    expect(within(row).getByText("[E1]")).toBeInTheDocument();
    expect(within(row).getByText("DEMO seed (fictional)")).toBeInTheDocument(); // inside <details>, still in the document
    expect(within(row).getByText("demo-0.1")).toBeInTheDocument();
    expect(screen.getByText("Fictional demonstration data only")).toBeInTheDocument();
    expect(screen.getByText(/not a probability/)).toBeInTheDocument();
    expect(screen.getByText("Publication dates of the sources are not recorded; only when we received them.")).toBeInTheDocument();
    expect(screen.getByText("Talk to a pharmacist or doctor about what this means for you.")).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/confidence|\d\s?%/i);
  });

  it("sends the chosen language and the patient-information switch, never anything else", async () => {
    const seen: unknown[] = [];
    open((init) => { seen.push(JSON.parse(String(init?.body))); return json(200, answer()); });
    await askQuestion("Nocturin");
    await userEvent.click(screen.getByRole("switch", { name: /Also use my health information/ }));
    await userEvent.click(screen.getByRole("button", { name: "Ask" }));
    await waitFor(() => expect(seen.length).toBe(2));
    expect(seen[0]).toEqual({ question: "Nocturin", locale: "en", medicationIds: null, includePatientContext: false });
    expect((seen[1] as { includePatientContext: boolean }).includePatientContext).toBe(true);
  });

  it("shows a loading state while waiting", async () => {
    let release: (r: Response) => void = () => undefined;
    open(() => new Promise<Response>((r) => { release = r; }));
    await askQuestion("Nocturin");
    expect(await screen.findByText("Looking through the sources…")).toBeInTheDocument();
    release(json(200, answer()));
    expect(await screen.findByText("Answer")).toBeInTheDocument();
  });

  it("an empty-evidence answer says nothing was found and offers the professional as the next step", async () => {
    open(() => json(200, answer({ status: "NoEvidence", reason: "no_evidence", answered: false, text: "I could not find anything about this in our medication knowledge base.", isMock: false, generation: null, provider: "none",
      evidence: empty, evidenceQuality: "None", limitations: ["evidence.none_found"] })));
    await askQuestion("xyzzy");
    expect(await screen.findByText("Nothing found in the sources")).toBeInTheDocument();
    expect(screen.getByText("No relevant source was found.", { selector: "p" })).toBeInTheDocument();
    expect(screen.getByText("Written by the system, not by a model")).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Sources and evidence" })).not.toBeInTheDocument();
  });

  it("a refusal is calm, names the reason and points to the prescriber", async () => {
    open(() => json(200, answer({ status: "Refused", reason: "medication_change", answered: false, text: "Whether to take a medicine, and at what dose, is a decision to make with your doctor or pharmacist.", isMock: false, generation: null, provider: "none",
      evidence: { ...empty, limitations: [] }, evidenceQuality: "None", limitations: [], nextStep: "ConsultPrescriber" })));
    await askQuestion("Should I stop?");
    expect(await screen.findByText("I can't help with that here")).toBeInTheDocument();
    expect(screen.getByText(/Starting, stopping or changing a medicine is for you and your prescriber/)).toBeInTheDocument();
    expect(screen.getByText("Talk to the doctor or pharmacist who prescribed or dispensed the medicine.")).toBeInTheDocument();
  });

  it("an escalation is an alert that stays clear and urgent, and says what the check is", async () => {
    open(() => json(200, answer({ status: "Escalated", reason: "emergency_signs", answered: false, text: "What you describe can be a sign of something that needs help right away. Please call your local emergency number now.", isMock: false, generation: null, provider: "none",
      evidence: { ...empty, limitations: [] }, evidenceQuality: "None", limitations: ["screen.keyword_based"], nextStep: "EmergencyServices" })));
    await askQuestion("I can't breathe");
    const alert = (await screen.findAllByRole("alert"))[0]!;
    expect(within(alert).getByText("This may need help right away")).toBeInTheDocument();
    expect(within(alert).getByText(/call your local emergency number now/)).toBeInTheDocument();
    expect(screen.getByText("Call your local emergency number now or go to the nearest emergency service.")).toBeInTheDocument();
    expect(screen.getByText(/works on keywords/)).toBeInTheDocument();
  });

  it("a withheld answer is shown as withheld, the evidence stays", async () => {
    open(() => json(200, answer({ status: "Blocked", reason: "answer.blocked", answered: false, text: "The generated answer did not pass our safety check, so it is not shown.", nextStep: "ConsultProfessional" })));
    await askQuestion("Nocturin");
    expect(await screen.findByText("The generated answer is not shown")).toBeInTheDocument();
    expect(screen.getByText(/did not pass the safety check/)).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Sources and evidence" })).toBeInTheDocument();
  });

  it("external generation is labelled as such, and a blocked external call says nothing was sent", async () => {
    open(() => json(200, answer({ isMock: false, provider: "external", generation: { provider: "external", kind: "External", model: "m", isMock: false, external: true } })));
    await askQuestion("Nocturin");
    expect(await screen.findByText("Written by an outside AI service")).toBeInTheDocument();
    cleanup();
    open(() => json(200, answer({ status: "Unavailable", reason: "external.consent_required", answered: false, isMock: false, generation: null, provider: "none", text: "The conditions for using an outside service are not met, so nothing was sent." })));
    await askQuestion("Nocturin");
    expect(await screen.findByText("No answer could be generated")).toBeInTheDocument();
    expect(screen.getByText("You have not agreed to use an outside service, so nothing was sent.")).toBeInTheDocument();
  });

  it("a provider timeout (503) still shows the sources", async () => {
    open(() => json(503, answer({ status: "Unavailable", reason: "provider.timeout", answered: false, error: "Timeout", isMock: false, generation: null, provider: "external", text: "I can't generate an answer right now." })));
    await askQuestion("Nocturin");
    expect(await screen.findByText("The AI provider did not answer in time.")).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Sources and evidence" })).toBeInTheDocument();
  });

  it("conflicting, stale and missing evidence are all stated", async () => {
    const ev = { medications: [], items: [item({ id: "E1", kind: "Interaction", qualifier: "x | Minor", stale: true }), item({ id: "E2", kind: "Interaction", qualifier: "x | Major", sourceDateUnknown: true })],
      conflicts: [{ code: "interaction.severity_disagrees", medicationName: "Nocturin", itemIds: ["E1", "E2"] }], missingInformation: ["kind.AdverseReaction"],
      limitations: ["evidence.conflict", "evidence.stale_source", "evidence.source_date_unknown", "evidence.incomplete"], quality: "Limited" as const, quarantinedCount: 1, excludedCount: 0 };
    open(() => json(200, answer({ evidence: ev, evidenceQuality: "Limited", limitations: ev.limitations, missingInformation: ev.missingInformation })));
    await askQuestion("interactions of Nocturin");
    expect(await screen.findByText(/Sources disagree about Nocturin/)).toBeInTheDocument();
    expect(screen.getByText("Some sources were received a long time ago and may be out of date.")).toBeInTheDocument();
    expect(screen.getByText("For some sources we do not know when they were received.")).toBeInTheDocument();
    expect(screen.getByText("Side effects", { selector: "li" })).toBeInTheDocument();
    expect(screen.getByText("May be out of date")).toBeInTheDocument();
    expect(screen.getByText("Validated, but with limits")).toBeInTheDocument();
  });

  it.each([[401, "Your account is not allowed to use the assistant."], [429, "Too many questions in a short time. Wait a moment."], [400, "The question could not be processed. Check its length and try again."], [500, "Something went wrong on the server. Try again later."]])("an HTTP %i is explained in words", async (status, text) => {
    open(() => json(status));
    await askQuestion("Nocturin");
    expect(await screen.findByText(text)).toBeInTheDocument();
  });

  it("limits the question to 500 characters and keeps the ask button off for an empty question", async () => {
    open(() => json(200, answer()));
    const box = await screen.findByLabelText("Your question");
    expect(screen.getByRole("button", { name: "Ask" })).toBeDisabled();
    await userEvent.click(box);
    await userEvent.paste("a".repeat(600));
    expect((box as HTMLTextAreaElement).value.length).toBe(500);
  });

  it("renders in Persian with right-to-left text and translated labels", async () => {
    open(() => json(200, answer({ text: "[MOCK AI] خلاصهٔ خودکار [E1]." })), "fa");
    await askQuestion("نوکتورین");
    expect(await screen.findByText("پاسخ")).toBeInTheDocument();
    expect(screen.getByText("آزمایشی (MOCK): مدل زبانی استفاده نشد")).toBeInTheDocument();
    expect(screen.getByText("فقط داده‌ٔ نمایشی ساختگی")).toBeInTheDocument();
    expect(document.documentElement.dir).toBe("rtl");
  });
});
