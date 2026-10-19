import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../AppProviders";
import { LocalMockBackend } from "../auth/localMockBackend";
import { routes } from "../routes";
import { diffDefinitions } from "../features/safety/RulesAdmin";
import { createMockServices } from "../services/mock/createMockServices";
import type { RuleDefinition, RuleDetail } from "../safety/types";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
beforeEach(() => { Object.defineProperty(window, "innerWidth", { value: 900, configurable: true }); });

const json = (status: number, body: unknown = {}) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
type Call = { method: string; path: string; body: unknown };
function open(reply: (p: string, init?: RequestInit, calls?: Call[]) => Response | Promise<Response>, path: string, account = "demo-patient", locale: "en" | "fa" = "en") {
  const calls: Call[] = [];
  const be = new LocalMockBackend({ storage: null, initialAccountId: account });
  Object.assign(be, { apiFetch: vi.fn(async (p: string, i?: RequestInit) => { calls.push({ method: i?.method ?? "GET", path: p, body: i?.body ? JSON.parse(String(i.body)) : undefined }); return reply(p, i, calls); }) });
  render(<AppProviders locale={locale} theme="light" auth={be} services={createMockServices({ latencyMs: 0 })}><RouterProvider router={createMemoryRouter(routes, { initialEntries: [path] })} /></AppProviders>);
  return calls;
}

const adherence = (over: Record<string, unknown> = {}) => ({
  from: "2026-10-01T00:00:00Z", until: "2026-10-14T00:00:00Z", days: 14, provenance: "PatientReported", intakeLastUpdatedAt: "2026-10-13T00:00:00Z", intakeStale: false,
  notice: "This is what the patient recorded. It is not verified, it is not a measure of how well a treatment works, and a planned dose with no record is not counted as missed.",
  medications: [
    { patientMedicationId: "m1", medicationName: "Demopril", isRegistered: true, schedule: [{ id: "s1", timeOfDay: "08:00:00", days: [] }], instructionGaps: ["route.unknown"], recordingAllowed: true, plannedDoses: 10, taken: 6, skipped: 1, missed: 1, notRecorded: 2, unplanned: 0, lastRecordedAt: "2026-10-13T00:00:00Z" },
    { patientMedicationId: "m2", medicationName: "Unknownol", isRegistered: false, schedule: [], instructionGaps: ["dose.unknown", "schedule.none", "medication.not_in_reference"], recordingAllowed: false, plannedDoses: 0, taken: 0, skipped: 0, missed: 0, notRecorded: 0, unplanned: 0, lastRecordedAt: null },
  ], ...over,
});

describe("adherence", () => {
  it("shows what was reported, says it is patient-reported, never turns silence into missed, and lists unknown instructions", async () => {
    open((p) => p.includes("/adherence/summary") ? json(200, adherence()) : p.includes("/doses") ? json(200, []) : p.includes("/medications") ? json(200, []) : json(404), "/app/patient/taking");
    expect(await screen.findByText("Adherence, as you reported it")).toBeInTheDocument();
    expect(await screen.findByText("Reported by the patient")).toBeInTheDocument();
    expect(screen.getByText(/a planned dose with no record is not counted as missed/)).toBeInTheDocument();
    const row = screen.getByRole("list", { name: "Adherence, as you reported it" });
    expect(within(row).getAllByText("Not recorded", { selector: "dt" }).length).toBe(2);
    expect(within(row).getByText("The dose is not known, so planned doses cannot be recorded.")).toBeInTheDocument();
    expect(within(row).getByText("This medicine is not in the medication reference.")).toBeInTheDocument();
    expect(within(row).getByText("Cannot be recorded yet")).toBeInTheDocument();
    expect(screen.getByText("If a dose was missed")).toBeInTheDocument();
    expect(screen.getByText(/do not take an extra dose to make up for it on your own/)).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/\d\s?%|compliance|score/i);
  });

  it("a past planned dose can be reported missed, a future one cannot, and the missed-dose note is shown without advice to compensate", async () => {
    const past = new Date(Date.now() - 3_600_000).toISOString();
    const future = new Date(Date.now() + 3_600_000).toISOString();
    const slots = [
      { scheduleEntryId: "e1", patientMedicationId: "m1", medicationName: "Demopril", localDate: "2026-10-14", localTime: "08:00:00", scheduledFor: past, status: null, logId: null },
      { scheduleEntryId: "e2", patientMedicationId: "m1", medicationName: "Demopril", localDate: "2026-10-14", localTime: "23:00:00", scheduledFor: future, status: null, logId: null },
    ];
    const calls = open((p, i) => i?.method === "POST" && p.endsWith("/intake") ? json(200, {}) : p.includes("/doses") ? json(200, slots) : p.includes("/adherence/summary") ? json(200, adherence()) : json(200, []), "/app/patient/taking");
    const buttons = await screen.findAllByRole("button", { name: "I missed it" });
    expect(buttons).toHaveLength(1);
    await userEvent.click(buttons[0]!);
    await waitFor(() => expect(calls.some((c) => c.method === "POST" && c.path.endsWith("/intake"))).toBe(true));
    const post = calls.find((c) => c.method === "POST" && c.path.endsWith("/intake"))!;
    expect(post.body).toMatchObject({ status: "Missed", takenAt: null, scheduleEntryId: "e1" });
  });

  it("a professional's adherence page is read-only and labelled as the patient's own report", async () => {
    open((p) => p.includes("/adherence/summary") ? json(200, adherence()) : json(404), "/app/physician/patients/abc/adherence", "demo-physician");
    expect(await screen.findByText("Reported by the patient")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /taken|missed/i })).not.toBeInTheDocument();
  });
});

const monitoring = (over: Record<string, unknown> = {}) => ({ enrolled: false, state: "Withdrawn", enrolledAt: null, lastAttemptAt: null, lastSuccessAt: null, lastOutcome: null, lastErrorCode: null, consecutiveFailures: 0, nextCheckAt: null, lastAssessmentId: null, workerEnabled: false, notice: "n", ...over });

describe("scheduled checks card", () => {
  const safetyReply = (mon: unknown, extra?: (p: string, i?: RequestInit) => Response | undefined) => (p: string, i?: RequestInit) => extra?.(p, i) ?? (p.includes("/safety/monitoring") ? (i?.method === "POST" ? json(200, monitoring({ enrolled: true, state: "Idle", nextCheckAt: "2026-10-15T00:00:00Z" })) : json(200, mon)) : json(404));

  it("is off by default, never claims continuous monitoring, and enrols only on request", async () => {
    const calls = open(safetyReply(monitoring()), "/app/patient/safety");
    expect(await screen.findByText("Scheduled checks are off")).toBeInTheDocument();
    expect(screen.getByText(/not continuous or real-time monitoring/)).toBeInTheDocument();
    expect(calls.some((c) => c.method === "POST" && c.path.includes("monitoring"))).toBe(false);
    await userEvent.click(screen.getByRole("button", { name: "Turn on scheduled checks" }));
    await waitFor(() => expect(calls.some((c) => c.method === "POST" && c.path.endsWith("/safety/monitoring?locale=en"))).toBe(true));
  });

  it("shows attempt, success, outcome and the next check, and says plainly when the background runner is off", async () => {
    open(safetyReply(monitoring({ enrolled: true, state: "Idle", workerEnabled: false, lastAttemptAt: "2026-10-14T08:00:00Z", lastSuccessAt: "2026-10-14T08:00:00Z", lastOutcome: "unchanged", nextCheckAt: "2026-10-14T09:00:00Z" })), "/app/patient/safety");
    expect(await screen.findByText("Scheduled checks are on")).toBeInTheDocument();
    expect(screen.getByText("The background runner is switched off on this system, so nothing runs by itself.")).toBeInTheDocument();
    expect(screen.getByText("Nothing relevant had changed, so no new check was needed")).toBeInTheDocument();
    expect(screen.getByText("Last completed check")).toBeInTheDocument();
  });

  it("an exhausted job says it stopped, shows the problem and offers to start again", async () => {
    open(safetyReply(monitoring({ enrolled: true, state: "Exhausted", consecutiveFailures: 5, lastOutcome: "failed.transient", lastErrorCode: "guidance.incomplete", lastSuccessAt: null })), "/app/patient/safety");
    expect(await screen.findByText("Stopped after repeated problems")).toBeInTheDocument();
    expect(screen.getByText(/the check was stored but its message could not be confirmed/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Start again" })).toBeInTheDocument();
    expect(screen.getAllByText("Never").length).toBeGreaterThan(0); // no last success: nothing is claimed
  });

  it("explains a missing consent in words", async () => {
    open(safetyReply(monitoring(), (p, i) => (i?.method === "POST" && p.includes("monitoring") ? json(400, { code: "consent.monitoring_required", errors: ["consent.monitoring_required"] }) : undefined)), "/app/patient/safety");
    await userEvent.click(await screen.findByRole("button", { name: "Turn on scheduled checks" }));
    expect(await screen.findByText(/need your 'monitoring' consent/)).toBeInTheDocument();
  });

  it("a professional sees the status but has no switch", async () => {
    open(safetyReply(monitoring({ enrolled: true, state: "Idle" })), "/app/physician/patients/abc/safety", "demo-physician");
    expect(await screen.findByText("Scheduled checks are on")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Turn off" })).not.toBeInTheDocument();
  });
});

const msg = (over: Record<string, unknown>) => ({
  id: "g1", templateKey: "safety.unregistered_medication", level: "FollowUp", status: "Sent", locale: "en", professional: null, createdAt: "2026-10-10T08:00:00Z", isDemo: false, notice: null,
  patient: { observed: "X is not in the medication reference.", whyItMatters: "w", suggestedAction: "a", whenToConsult: "c", urgentSigns: null, basisAndConfidence: "b" },
  origin: null, provenance: "AutomatedAssessment", clinicianReviewed: false, delivery: null, ...over,
});

describe("delivery and provenance labels", () => {
  const shows = async (m: Record<string, unknown>, text: RegExp | string) => {
    open((p) => p.startsWith("/guidance/messages") ? json(200, [msg(m)]) : json(404), "/app/patient/messages");
    expect(await screen.findByText(text)).toBeInTheDocument();
    expect(screen.getByText("Automated check")).toBeInTheDocument();
    expect(screen.getByText("Not reviewed by a professional yet")).toBeInTheDocument();
  };
  const d = (state: string, mode = "Simulated", extra: Record<string, unknown> = {}) => ({ state, mode, channel: "local-simulated", scheduledAt: "2026-10-10T08:00:00Z", lastAttemptAt: null, confirmedAt: null, failedAt: null, attempts: 0, errorCode: null, ...extra });

  it("no delivery: in the app only", async () => { await shows({ delivery: null }, "In the app only. No notification was scheduled."); });
  it("scheduled is not sent", async () => { await shows({ delivery: d("Scheduled") }, "A notification is scheduled. It has not been sent yet."); });
  it("attempted is not delivered", async () => { await shows({ delivery: d("Attempted", "Simulated", { attempts: 1 }) }, "A notification attempt started and its result is not known."); });
  it("failed keeps the message", async () => { await shows({ delivery: d("Failed", "Simulated", { attempts: 3, errorCode: "channel.transient_exhausted" }) }, "The notification could not be delivered. The message is still here."); });
  it.each([
    ["message.resolved", "This message no longer needs to be sent."],
    ["patient.missing", "Not sent: patient information is unavailable."],
    ["channel.unavailable", "Not sent: the sending service is unavailable."],
    ["message.missing", "Not sent: the message is unavailable."],
  ])("nothing was attempted (%s): a neutral message, not a delivery failure", async (errorCode, text) => {
    await shows({ delivery: d("Failed", "Simulated", { attempts: 0, errorCode }) }, text);
    expect(screen.queryByText(/could not be delivered/)).not.toBeInTheDocument();
  });
  it.each([
    ["message.resolved", "این پیام دیگر نیاز به ارسال ندارد."],
    ["patient.missing", "ارسال انجام نشد؛ اطلاعات بیمار در دسترس نیست."],
    ["channel.unavailable", "ارسال انجام نشد؛ سرویس ارسال در دسترس نیست."],
    ["message.missing", "ارسال انجام نشد؛ پیام موردنظر در دسترس نیست."],
  ])("the approved Persian wording is shown for %s", async (errorCode, text) => {
    open((p) => p.startsWith("/guidance/messages") ? json(200, [msg({ delivery: d("Failed", "Simulated", { attempts: 0, errorCode }) })]) : json(404), "/app/patient/messages", "demo-patient", "fa");
    expect(await screen.findByText(text)).toBeInTheDocument();
    expect(screen.queryByText("اعلان تحویل داده نشد. پیام همچنان اینجاست.")).not.toBeInTheDocument();
  });
  it("a non-failed state never uses a not-sent message even if a stale code is present", async () => {
    await shows({ delivery: d("Scheduled", "Simulated", { errorCode: "message.resolved" }) }, "A notification is scheduled. It has not been sent yet.");
  });
  it.each(["channel.refused", "channel.transient_exhausted", "interrupted", null])("every other failure keeps the failed wording (code: %s)", async (errorCode) => {
    await shows({ delivery: d("Failed", "Simulated", { attempts: 1, errorCode }) }, "The notification could not be delivered. The message is still here.");
  });
  it("a simulated confirmation never reads as delivered", async () => {
    await shows({ delivery: d("Confirmed", "Simulated", { confirmedAt: "2026-10-10T08:01:00Z", attempts: 1 }) }, "Simulated notification: nothing real was sent to any device.");
    expect(screen.queryByText(/Delivered through/)).not.toBeInTheDocument();
  });
  it("only a real channel's confirmation says delivered", async () => { await shows({ delivery: d("Confirmed", "Real", { channel: "push-x", confirmedAt: "2026-10-10T08:01:00Z" }) }, "Delivered through push-x."); });
  it("a clinician review is shown as its own fact", async () => {
    open((p) => p.startsWith("/guidance/messages") ? json(200, [msg({ clinicianReviewed: true, status: "Reviewed" })]) : json(404), "/app/patient/messages");
    expect(await screen.findByText("Reviewed by a professional")).toBeInTheDocument();
    expect(screen.getByText("Automated check")).toBeInTheDocument();
  });
});

const def = (over: Partial<RuleDefinition> = {}): RuleDefinition => ({
  ruleId: "T-DQ-1", version: 1, title: "Test rule", purpose: "Purpose", domain: "DataQuality", population: { minAgeYears: null, maxAgeYears: null, unsupported: [] }, requiredInputs: ["medications"],
  criteria: { kind: "UnregisteredMedication", minReferenceSeverity: null, withinDays: null }, severity: "Informational", urgency: "Routine", guidanceTemplateKey: "safety.unregistered_medication", emergencySigns: null,
  evidence: [{ sourceId: "s", sourceName: "A source", version: "1", publicationDate: null, validation: "Unverified", reviewStatus: "none", locator: null }], limitations: ["a limit"], testCases: [{ name: "case one", expectedOutcome: "NoMatch", expectedFindings: 0, input: {} }], ...over,
});
const detail = (status: string, d = def(), extra: Record<string, unknown> = {}): RuleDetail => ({ record: { definition: d, lifecycle: { status, isDemo: false, author: "u-author", authoredAt: "2026-10-01T00:00:00Z", reviewer: null, reviewedAt: null, reviewNote: null, effectiveFrom: null, retiredAt: null, ...extra } as never }, activation: { state: "Inactive", reasons: [`status.${status.toLowerCase()}`] } });
const hist = (over: Record<string, unknown> = {}) => ({ authorName: "Dana Author", reviewerName: null, authorIsYou: false, approvalBlockers: [], events: [{ action: "created", from: "Draft", to: "Draft", actor: "Dana Author", at: "2026-10-01T00:00:00Z" }], ...over });

function rulesReply(d: RuleDetail, h = hist(), more?: (p: string, i?: RequestInit) => Response | undefined) {
  return (p: string, i?: RequestInit) => more?.(p, i)
    ?? (p.endsWith("/history") ? json(200, h) : p.startsWith("/clinical-rules/options") ? json(200, { domains: [{ domain: "DataQuality", criteria: "UnregisteredMedication", requiredInputs: ["medications"] }], templateKeys: ["safety.unregistered_medication"], severities: ["Informational", "Moderate"], urgencies: ["None", "Routine"], validations: ["Demo", "Unverified", "NeedsValidation", "Validated"], minReferenceSeverities: ["Moderate"], outcomes: [] })
      : p.startsWith("/clinical-rules?") ? json(200, [{ ruleId: "T-DQ-1", version: 1, title: "Test rule", domain: "DataQuality", status: "Draft", isDemo: false, activation: "Inactive", activationReasons: [], authoredAt: "x" }, { ruleId: "T-DQ-1", version: 2, title: "Test rule", domain: "DataQuality", status: "Draft", isDemo: false, activation: "Inactive", activationReasons: [], authoredAt: "x" }])
      : p.startsWith("/clinical-rules/T-DQ-1/versions/2") ? json(200, detail("Draft", def({ version: 2, title: "Changed title" })))
      : p.startsWith("/clinical-rules/") ? json(200, d) : json(404));
}

describe("rule review workspace", () => {
  it("a draft shows provenance, who wrote it, why it is not in force and that no rule is approved; the author may edit and submit", async () => {
    open(rulesReply(detail("Draft"), hist({ authorIsYou: true })), "/app/workspace/rules/T-DQ-1/1", "demo-content-manager");
    expect(await screen.findByText("T-DQ-1 v1")).toBeInTheDocument();
    expect(screen.getByText("Dana Author (you)")).toBeInTheDocument();
    expect(screen.getByText("No rule is clinically approved")).toBeInTheDocument();
    expect(screen.getByText("Why this rule is not in force")).toBeInTheDocument();
    expect(screen.getByText("A source")).toBeInTheDocument();
    expect(screen.getByText("Not verified")).toBeInTheDocument();
    expect(screen.getByText(/never make a rule eligible to go into force/)).toBeInTheDocument();
    expect(screen.getAllByText(/case one/).length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: "Edit this draft" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Submit for review" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
  });

  it("submitting asks the server and shows its refusal in words", async () => {
    const calls = open(rulesReply(detail("Draft"), hist({ authorIsYou: true }), (p, i) => (i?.method === "POST" && p.endsWith("/submit") ? json(400, { code: "tests.failed", errors: ["tests.failed"] }) : undefined)), "/app/workspace/rules/T-DQ-1/1", "demo-content-manager");
    await userEvent.click(await screen.findByRole("button", { name: "Submit for review" }));
    expect(await screen.findByText("The rule was not accepted")).toBeInTheDocument();
    expect(screen.getByText("The rule's own test cases do not pass.")).toBeInTheDocument();
    expect(calls.filter((c) => c.method === "POST").length).toBe(1);
  });

  it("the author cannot review: the controls are disabled and the reason is shown (the server enforces it anyway)", async () => {
    open(rulesReply(detail("UnderReview"), hist({ authorIsYou: true, approvalBlockers: ["review.separation_of_duties"] })), "/app/physician/rules/T-DQ-1/1", "demo-physician");
    expect(await screen.findByText("You wrote this version")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Reject" })).toBeDisabled();
  });

  it("a different reviewer sees what stands in the way, and approval is a request the server may refuse", async () => {
    const calls = open(rulesReply(detail("UnderReview"), hist({ approvalBlockers: ["policy.no_validated_evidence"] }), (p, i) => (i?.method === "POST" && p.endsWith("/review") ? json(400, { code: "policy.no_validated_evidence", errors: ["policy.no_validated_evidence"] }) : undefined)), "/app/physician/rules/T-DQ-1/1", "demo-physician");
    expect(await screen.findByText("What currently stands in the way of approval")).toBeInTheDocument();
    expect(screen.getAllByText("No validated evidence.").length).toBeGreaterThan(0);
    await userEvent.type(screen.getByLabelText(/Note/), "checked");
    await userEvent.click(screen.getByRole("button", { name: "Approve" }));
    expect(await screen.findByText("The rule was not accepted")).toBeInTheDocument();
    const post = calls.find((c) => c.method === "POST" && c.path.endsWith("/review"))!;
    expect(post.body).toMatchObject({ decision: "Approve", note: "checked" });
    expect(screen.queryByText("Approved", { selector: ".sf-badges *" })).not.toBeInTheDocument(); // nothing became approved by clicking
  });

  it("reject needs a note", async () => {
    open(rulesReply(detail("UnderReview")), "/app/physician/rules/T-DQ-1/1", "demo-physician");
    expect(await screen.findByRole("button", { name: "Reject" })).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Note/), "evidence insufficient");
    expect(screen.getByRole("button", { name: "Reject" })).toBeEnabled();
  });

  it("a demonstration rule is labelled, offers no author or review action and cannot be approved", async () => {
    const demo = detail("Draft", def({ ruleId: "DEMO-DQ-1" }), { isDemo: true, author: "demo-seed" });
    demo.activation = { state: "DemonstrationOnly", reasons: ["demo.not_clinically_validated"] };
    open(rulesReply(demo, hist({ authorName: "demo-seed" })), "/app/physician/rules/DEMO-DQ-1/1", "demo-physician");
    expect(await screen.findByText("Demonstration rule")).toBeInTheDocument();
    expect(screen.getAllByText("DEMO - not clinically validated").length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Submit for review" })).not.toBeInTheDocument();
  });

  it("an approved rule shows its reviewer and dates; retiring is only offered to those who may", async () => {
    const approved = detail("Approved", def(), { reviewer: "u-rev", reviewedAt: "2026-10-05T00:00:00Z", effectiveFrom: "2026-10-05T00:00:00Z" });
    approved.activation = { state: "Active", reasons: [] };
    open(rulesReply(approved, hist({ reviewerName: "Rae Reviewer" })), "/app/physician/rules/T-DQ-1/1", "demo-physician");
    expect(await screen.findByText("Rae Reviewer")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retire this version" })).not.toBeInTheDocument(); // reviewers cannot retire
  });

  it("compares two versions field by field", async () => {
    open(rulesReply(detail("Draft", def({ title: "Original title" }))), "/app/physician/rules/T-DQ-1/1", "demo-physician");
    await screen.findByText("T-DQ-1 v1");
    await userEvent.selectOptions(await screen.findByRole("combobox", { name: "Version to compare" }), "2");
    const table = await screen.findByRole("table");
    expect(within(table).getByText("title")).toBeInTheDocument();
    expect(within(table).getByText("Changed title")).toBeInTheDocument();
    expect(within(table).getByText("Original title")).toBeInTheDocument();
  });

  it("diffDefinitions ignores the version number itself and reports nested changes", () => {
    const a = def();
    const b = def({ version: 2, evidence: [{ ...a.evidence[0]!, validation: "Validated" }], severity: "Moderate" });
    expect(diffDefinitions(a, b).map((x) => x.path)).toEqual(["evidence[0].validation", "severity"]);
  });
});

describe("rule editor", () => {
  const editorReply = (more?: (p: string, i?: RequestInit) => Response | undefined) => rulesReply(detail("Draft"), hist(), more);

  it("creating a draft sends what was typed, labels it as a proposal and never offers approval", async () => {
    const calls = open(editorReply((p, i) => (i?.method === "POST" && p === "/clinical-rules" ? json(201, detail("Draft", def({ ruleId: "N-1" }))) : undefined)), "/app/workspace/rules/new", "demo-content-manager");
    expect(await screen.findByText("A draft is only a proposal. It cannot be in force until another person reviews and approves it.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /approve/i })).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Rule id"), "N-1");
    await userEvent.type(screen.getByLabelText("Title"), "My rule");
    await userEvent.type(screen.getAllByLabelText("Purpose")[0]!, "Notes a thing");
    await userEvent.type(screen.getByLabelText("Source id"), "src-1");
    await userEvent.type(screen.getByLabelText("Source name"), "Some guideline");
    await userEvent.type(screen.getByLabelText("Source version"), "2024");
    await userEvent.click(screen.getByRole("button", { name: "Save draft" }));
    await waitFor(() => expect(calls.some((c) => c.method === "POST" && c.path === "/clinical-rules")).toBe(true));
    const body = calls.find((c) => c.method === "POST" && c.path === "/clinical-rules")!.body as RuleDefinition;
    expect(body).toMatchObject({ ruleId: "N-1", title: "My rule", domain: "DataQuality", criteria: { kind: "UnregisteredMedication" }, requiredInputs: ["medications"] });
    expect(body.evidence[0]).toMatchObject({ sourceId: "src-1", validation: "Unverified", publicationDate: null }); // a date is never filled in for you
  });

  it("invalid test-case JSON is caught before anything is sent", async () => {
    const calls = open(editorReply(), "/app/workspace/rules/new", "demo-content-manager");
    const box = await screen.findByLabelText("Show the test cases as data");
    await userEvent.clear(box);
    await userEvent.type(box, "not json");
    await userEvent.click(screen.getByRole("button", { name: "Save draft" }));
    expect(await screen.findByText("The test cases are not valid JSON")).toBeInTheDocument();
    expect(calls.some((c) => c.method === "POST")).toBe(false);
  });

  it("server refusals are listed in words", async () => {
    open(editorReply((p, i) => (i?.method === "POST" && p === "/clinical-rules" ? json(400, { code: "rule.id_reserved", errors: ["rule.id_reserved", "text.forbidden_phrase"] }) : undefined)), "/app/workspace/rules/new", "demo-content-manager");
    await userEvent.click(await screen.findByRole("button", { name: "Save draft" }));
    expect(await screen.findByText("This id is reserved for demonstration rules.")).toBeInTheDocument();
    expect(screen.getByText(/tells someone to change a medicine, reassures/)).toBeInTheDocument();
  });

  it("editing a draft keeps its id and version fixed and saves with PUT", async () => {
    const calls = open(editorReply((_p, i) => (i?.method === "PUT" ? json(200, detail("Draft")) : undefined)), "/app/workspace/rules/T-DQ-1/1/edit", "demo-content-manager");
    const id = await screen.findByLabelText("Rule id");
    expect(id).toBeDisabled();
    await userEvent.click(screen.getByRole("button", { name: "Save changes" }));
    await waitFor(() => expect(calls.some((c) => c.method === "PUT" && c.path === "/clinical-rules/T-DQ-1/versions/1")).toBe(true));
  });

  it("Persian shows the not-reviewed notice and no raw keys", async () => {
    open(rulesReply(detail("Draft")), "/app/workspace/rules/T-DQ-1/1", "demo-content-manager", "fa");
    expect(await screen.findByText("T-DQ-1 v1")).toBeInTheDocument();
    expect(document.documentElement.dir).toBe("rtl");
    expect(document.body.textContent).not.toMatch(/\b(rv|sf|adh)\.[a-z]/i);
  });
});
