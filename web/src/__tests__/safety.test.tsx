import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../AppProviders";
import { LocalMockBackend } from "../auth/localMockBackend";
import { routes } from "../routes";
import { SafetyApi } from "../safety/api";
import type { Assessment, EngineResult, Finding, RuleEvaluation } from "../safety/types";
import { createMockServices } from "../services/mock/createMockServices";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
beforeEach(() => { Object.defineProperty(window, "innerWidth", { value: 900, configurable: true }); });

const json = (status: number, body: unknown = {}) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const ME = "fa1f8b92-53cc-5ad2-a0a8-c39713c37946";

const evidence = { sourceId: "test-fixture", sourceName: "TEST FIXTURE (not a real source)", version: "1", publicationDate: null, validation: "Validated" as const, reviewStatus: "fixture", locator: null };
const finding = (over: Partial<Finding> = {}): Finding => ({
  key: "f-1", ruleId: "T-INT", ruleVersion: 1, domain: "Interaction", severity: "Major", urgency: "Soon", actionable: true, isDemo: false, guidanceTemplateKey: "safety.interaction",
  subjects: [{ kind: "medication", recordId: "m1", label: "Alpha" }, { kind: "medication", recordId: "m2", label: "Beta" }], evidence: [evidence], referenceSeverity: "Major",
  evidenceConflict: false, inputsStale: false, limitations: ["reference.not_validated"], emergencySigns: null, ...over,
});
const evalOf = (over: Partial<RuleEvaluation> = {}): RuleEvaluation => ({ ruleId: "T-INT", version: 1, domain: "Interaction", outcome: "Matched", reasons: [], activation: "Active", isDemo: false, partial: false, findingCount: 1, ...over });
const inputs = [
  { category: "profile", availability: "Available" as const, lastUpdatedAt: "2026-10-01T00:00:00Z", recordCount: 1, isStale: false, staleAfterDays: 365, source: "patient-record" },
  { category: "medications", availability: "Available" as const, lastUpdatedAt: "2026-10-01T00:00:00Z", recordCount: 2, isStale: false, staleAfterDays: 180, source: "patient-record" },
  { category: "allergies", availability: "NeverRecorded" as const, lastUpdatedAt: null, recordCount: 0, isStale: false, staleAfterDays: 365, source: "patient-record" },
  { category: "symptoms", availability: "NotAuthorized" as const, lastUpdatedAt: null, recordCount: 0, isStale: false, staleAfterDays: 30, source: "withheld" },
];
const result = (over: Partial<EngineResult> = {}): EngineResult => ({
  status: "CompletedWithFindings", complete: true, inputs, evaluations: [evalOf()], findings: [finding()],
  coverage: { activeRules: 1, demonstrationRules: 0, inactiveRules: 0, notEvaluable: 0, domainsCovered: ["Interaction"], unsupportedDomains: ["dose_and_route", "adherence"] },
  limitations: ["engine.not_a_diagnosis", "engine.not_triage", "engine.clean_result_is_not_safety"], engineVersion: "engine-1.0.0", ruleSetVersion: "rs-abc123", evaluatedAt: "2026-10-10T08:00:00Z",
  containsDemonstration: false, safetyClaimAllowed: false, ...over,
});
const assessment = (over: Partial<Assessment> = {}, r: Partial<EngineResult> = {}): Assessment => ({
  id: "a1", subjectId: ME, result: result(r), trigger: "manual", requestedByPatient: true, guidanceState: "Created", guidanceLinks: [], openGuidanceNoLongerMatching: [], outdated: false, outdatedReasons: [],
  notice: "Based on the rules listed. Not a diagnosis, not a triage and not medical advice.", ...over,
});

type Reply = (path: string, init?: RequestInit) => Response | Promise<Response>;
function open(reply: Reply | null, locale: "en" | "fa" = "en", path = "/app/patient/safety", account = "demo-patient") {
  const be = new LocalMockBackend({ storage: null, initialAccountId: account });
  if (reply) Object.assign(be, { apiFetch: vi.fn(async (p: string, i?: RequestInit) => reply(p, i)) });
  render(<AppProviders locale={locale} theme="light" auth={be} services={createMockServices({ latencyMs: 0 })}><RouterProvider router={createMemoryRouter(routes, { initialEntries: [path] })} /></AppProviders>);
  return be;
}
const latest = (a: Assessment): Reply => (p) => p.includes("/assessments/latest") ? json(200, a) : p.includes("/assessments?take") ? json(200, []) : json(404);

describe("SafetyApi", () => {
  it("builds the routes from the patient id only and reads the structured result", async () => {
    const f = vi.fn(async () => json(201, assessment()));
    const a = await new SafetyApi(f).run("p 1", "fa");
    expect(a.result.status).toBe("CompletedWithFindings");
    const [path, init] = f.mock.calls[0] as unknown as [string, RequestInit];
    expect(path).toBe("/patients/p%201/safety/assessments?locale=fa");
    expect(init.method).toBe("POST");
    expect(init.body).toBeUndefined(); // the body can never name another patient
  });
  it.each([[401, "unauthorized"], [403, "unauthorized"], [404, "notFound"], [400, "invalid"], [429, "rateLimited"], [503, "unavailable"], [500, "server"]])("maps HTTP %i to %s", async (status, kind) => {
    await expect(new SafetyApi(async () => json(status)).latest("p")).rejects.toMatchObject({ kind });
  });
  it("reports no connection, a lost network and a timeout apart", async () => {
    await expect(new SafetyApi(undefined).latest("p")).rejects.toMatchObject({ kind: "notConnected" });
    await expect(new SafetyApi(async () => { throw new TypeError("offline"); }).latest("p")).rejects.toMatchObject({ kind: "network" });
    const never = (_p: string, init?: RequestInit) => new Promise<Response>((_r, rej) => init?.signal?.addEventListener("abort", () => rej(new DOMException("aborted", "AbortError"))));
    await expect(new SafetyApi(never, 20).latest("p")).rejects.toMatchObject({ kind: "network" });
  });
});

describe("patient safety page", () => {
  it("not connected: says so and runs nothing", async () => {
    open(null);
    expect(await screen.findByText("Not connected")).toBeInTheDocument();
  });

  it("shows a stored result without running a new check by itself", async () => {
    const reply = vi.fn(latest(assessment()));
    open(reply);
    expect(await screen.findByText("The check found something to look at")).toBeInTheDocument();
    expect(reply.mock.calls.every(([, i]) => (i as RequestInit | undefined)?.method !== "POST")).toBe(true);
  });

  it("completed with findings: what, why, next step, severity as words, sources, rule and version, and never a safety claim", async () => {
    open(latest(assessment()));
    const list = await screen.findByRole("list", { name: "What was found" });
    const item = within(list).getAllByRole("listitem")[0]!;
    expect(within(item).getAllByText("Major").length).toBeGreaterThan(0); // a word, not only a colour
    expect(within(item).getByText(/Alpha · Beta/)).toBeInTheDocument();
    expect(within(item).getByText(/Do not change anything on your own/)).toBeInTheDocument();
    expect(within(item).getByText("T-INT v1")).toBeInTheDocument();
    expect(within(item).getByText("TEST FIXTURE (not a real source)")).toBeInTheDocument();
    expect(within(item).getByText("Publication date not recorded")).toBeInTheDocument();
    expect(screen.getByText("A result without findings is not a statement that anything is safe.", { selector: "strong" })).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/\d\s?%|you are safe|no risk/i);
  });

  it("no approved rules: says so, labels demonstration findings and never presents them as advice", async () => {
    const demo = finding({ isDemo: true, actionable: false, ruleId: "DEMO-INT-001", severity: "Moderate" });
    open(latest(assessment({}, { status: "NoApprovedCoverage", complete: false, containsDemonstration: true, findings: [demo], evaluations: [evalOf({ ruleId: "DEMO-INT-001", activation: "DemonstrationOnly", isDemo: true })],
      coverage: { activeRules: 0, demonstrationRules: 5, inactiveRules: 0, notEvaluable: 0, domainsCovered: [], unsupportedDomains: ["dose_and_route"] } })));
    expect(await screen.findByText("No approved rules are in force")).toBeInTheDocument();
    expect(screen.getByText("Demonstration findings")).toBeInTheDocument();
    expect(screen.getAllByText("DEMO - not clinically validated").length).toBeGreaterThan(0);
    expect(screen.getByText("This comes from a demonstration rule or demonstration data. It is not approved advice.")).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "What was found" })).not.toBeInTheDocument();
  });

  it("completed with no matches: states the coverage limits next to it", async () => {
    open(latest(assessment({}, { status: "CompletedNoMatches", findings: [], evaluations: [evalOf({ outcome: "NoMatch", findingCount: 0 })] })));
    expect(await screen.findByText("The approved rules found nothing")).toBeInTheDocument();
    expect(screen.getByText(/That is not the same as being safe/)).toBeInTheDocument();
    expect(screen.getByText("Not checked at all")).toBeInTheDocument();
    expect(screen.getByText("Doses and how a medicine is taken")).toBeInTheDocument();
    expect(screen.getByText("Whether medicines are taken as planned")).toBeInTheDocument();
  });

  it("incomplete: names the missing, withheld and out-of-date information instead of an empty result", async () => {
    const ev = evalOf({ outcome: "MissingData", findingCount: 0, reasons: ["input.symptoms.not_authorized", "input.allergies.never_recorded"] });
    open(latest(assessment({}, { status: "Incomplete", complete: false, findings: [], evaluations: [ev] })));
    expect(await screen.findByText("The check is incomplete")).toBeInTheDocument();
    expect(screen.getByText(/Missing information is never treated as nothing found/)).toBeInTheDocument();
    expect(screen.getByText(/Symptoms may not be read by this account\./)).toBeInTheDocument();
    expect(screen.getByText(/No allergies have been recorded\./)).toBeInTheDocument();
    expect(screen.getAllByText("Not available to this account").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Never recorded").length).toBeGreaterThan(0);
  });

  it("a failed internal check is an alert that says not to rely on the result", async () => {
    open(latest(assessment({}, { status: "Failed", complete: false })));
    expect((await screen.findAllByRole("alert")).some((a) => a.textContent?.includes("Do not rely on this result"))).toBe(true);
  });

  it("an outdated result says why, and nothing re-runs", async () => {
    const reply = vi.fn(latest(assessment({ outdated: true, outdatedReasons: ["rules.changed", "data.changed.allergies"] })));
    open(reply);
    expect(await screen.findByText("This result may be out of date")).toBeInTheDocument();
    expect(screen.getByText("The set of rules in force changed.")).toBeInTheDocument();
    expect(screen.getByText("Your allergy records changed.")).toBeInTheDocument();
    expect(reply.mock.calls.some(([, i]) => (i as RequestInit | undefined)?.method === "POST")).toBe(false);
  });

  it("blocked by authorization is its own state, different from a technical failure", async () => {
    open(() => json(403, { title: "Access denied" }));
    expect(await screen.findByText("This is not available to you")).toBeInTheDocument();
    expect(screen.queryByText("The check is unavailable right now")).not.toBeInTheDocument();
  });

  it("a technical failure says there is no result and that this is not 'nothing found'", async () => {
    open(() => json(503, { code: "assessment.unavailable" }));
    expect(await screen.findByText("The check is unavailable right now")).toBeInTheDocument();
    expect(screen.getByText(/This does not mean nothing was found/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
  });

  it("no check yet: offers to run one, and running shows the new result and the guidance state without claiming it was read", async () => {
    const calls: string[] = [];
    open((p, i) => {
      calls.push(`${i?.method ?? "GET"} ${p}`);
      if (p.includes("/assessments/latest")) return json(404, { code: "assessment.none" });
      if (p.includes("/assessments?take")) return json(200, []);
      if (i?.method === "POST") return json(201, assessment());
      return json(404);
    });
    expect(await screen.findByText("No check has been run yet")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Run the check" }));
    expect(await screen.findByText("The check found something to look at")).toBeInTheDocument();
    expect(calls.some((c) => c === `POST /patients/${ME}/safety/assessments?locale=en`)).toBe(true);
    expect(screen.getByText(/Messages were created in the patient's inbox \(this does not mean they were read\)/)).toBeInTheDocument();
  });

  it("a non-actionable finding from unvalidated data is shown as such", async () => {
    open(latest(assessment({}, { findings: [finding({ actionable: false })], evaluations: [evalOf()] })));
    expect(await screen.findByText("Notes that are not actionable")).toBeInTheDocument();
    expect(screen.getAllByText("Not actionable").length).toBeGreaterThan(0);
  });

  it("Persian: right-to-left, the wording-not-reviewed notice, and no untranslated keys", async () => {
    open(latest(assessment({}, { findings: [finding({ limitations: ["reference.not_validated", "evidence.conflicting_entries"], evidenceConflict: true })] })), "fa");
    expect(await screen.findByText("متن فارسی هنوز بازبینی نشده است")).toBeInTheDocument();
    expect(document.documentElement.dir).toBe("rtl");
    expect(screen.getByText("بررسی مواردی برای توجه پیدا کرد")).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/\bsf\.[a-z]/i);
  });
});

describe("professional safety page and rules page", () => {
  it("a professional page uses the route patient id and shows a refusal as a refusal", async () => {
    const seen: string[] = [];
    open((p) => { seen.push(p); return json(403); }, "en", "/app/physician/patients/abc-123/safety", "demo-physician");
    expect(await screen.findByText("This is not available to you")).toBeInTheDocument();
    expect(seen.some((p) => p.startsWith("/patients/abc-123/safety/assessments/latest"))).toBe(true);
  });

  it("the rules page shows coverage, every version with its status, and says when nothing is in force", async () => {
    open((p) => p.startsWith("/safety/coverage")
      ? json(200, { activeRules: 0, demonstrationRules: 1, inactiveRules: 0, domainsWithActiveCoverage: [], domainsWithoutActiveCoverage: ["Interaction"], unsupportedDomains: ["dose_and_route"], ruleSetVersion: "rs-1", demonstrationAllowed: true })
      : json(200, [{ ruleId: "DEMO-INT-001", version: 1, title: "DEMO: interaction entry listed in the reference", domain: "Interaction", status: "Draft", isDemo: true, activation: "DemonstrationOnly", activationReasons: ["demo.not_clinically_validated"], authoredAt: "2026-10-01T00:00:00Z" }]),
    "en", "/app/physician/rules", "demo-physician");
    expect(await screen.findByText("No approved rule is in force")).toBeInTheDocument();
    expect(screen.getByText("DEMO-INT-001 v1")).toBeInTheDocument();
    expect(screen.getByText("Draft")).toBeInTheDocument();
    expect(screen.getByText("Demonstration rule, not clinically validated.")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText("Not checked at all")).toBeInTheDocument());
  });
});

describe("messages raised by the safety check", () => {
  it("show where they came from and stay 'New' until the patient reads them", async () => {
    const msg = {
      id: "g1", templateKey: "safety.unregistered_medication", level: "FollowUp", status: "Sent", locale: "en", professional: null, createdAt: "2026-10-10T08:00:00Z", isDemo: false, notice: null,
      patient: { observed: "X is not in the medication reference.", whyItMatters: "w", suggestedAction: "a", whenToConsult: "c", urgentSigns: null, basisAndConfidence: "b" },
      origin: { kind: "safety", assessmentId: "a1", ruleId: "t-dq", ruleVersion: 2, findingKey: "f-1", dataAsOf: "2026-10-09T00:00:00Z" },
    };
    open((p) => p.startsWith("/guidance/messages") ? json(200, [msg]) : json(404), "en", "/app/patient/messages");
    expect(await screen.findByText(/Raised by safety rule t-dq \(version 2\)/)).toBeInTheDocument();
    expect(screen.getByText("New")).toBeInTheDocument();
    expect(screen.queryByText("Read")).not.toBeInTheDocument();
  });
});
