import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it } from "vitest";
import { AppProviders } from "../AppProviders";
import { LocalMockBackend } from "../auth/localMockBackend";
import { routes } from "../routes";
import { createMockServices } from "../services/mock/createMockServices";

afterEach(cleanup);
const accountFor: Record<string, string> = { patient: "demo-patient", physician: "demo-physician", pharmacist: "demo-pharmacist", pharmacy: "demo-pharmacy-admin", industry: "demo-industry", admin: "demo-system-admin" };
/** Opens a route signed in as the fictional account that owns that area (or `account`), without touching browser storage. */
const open = (path: string, locale: "fa" | "en" = "en", now = () => new Date(2026, 8, 29, 10, 0), account?: string | null) => {
  const area = /^\/app\/(\w+)/.exec(path)?.[1] ?? "";
  const id = account === undefined ? accountFor[area] : account ?? undefined;
  const auth = new LocalMockBackend({ storage: null, initialAccountId: id });
  return render(<AppProviders locale={locale} theme="light" auth={auth} services={createMockServices({ latencyMs: 0, now })}><RouterProvider router={createMemoryRouter(routes, { initialEntries: [path] })} /></AppProviders>);
};

describe("every route renders without errors", () => {
  const cases: [string, RegExp][] = [
    ["/", /Every medicine, every dose/], ["/design-system", /Design system/],
    ["/app/patient", /Next dose/], ["/app/patient/medications", /Medications/], ["/app/patient/medications/drug-demopril", /Demopril/], ["/app/patient/assistant", /AI Assistant/], ["/app/patient/checkin", /Check-in/], ["/app/patient/profile", /Profile/],
    ["/app/physician", /Patients needing attention/], ["/app/physician/patients", /Patients/], ["/app/physician/patients/pt-sara", /Sara Ahmadi/], ["/app/physician/prescriptions", /Prescriptions/], ["/app/physician/adr", /Adverse reactions/], ["/app/physician/reports", /Reports/],
    ["/app/pharmacist", /Work queue/], ["/app/pharmacist/reviews", /Medication review/], ["/app/pharmacist/interactions", /Interactions/], ["/app/pharmacist/adr", /Adverse reactions/], ["/app/pharmacist/adherence", /Adherence/], ["/app/pharmacist/questions", /Patient questions/], ["/app/pharmacist/followups", /Follow-up/],
    ["/app/pharmacy", /Overview/], ["/app/pharmacy/inventory", /Inventory/], ["/app/pharmacy/prescriptions", /Prescriptions/], ["/app/pharmacy/dispensing", /Dispensing/], ["/app/pharmacy/requests", /Patient requests/], ["/app/pharmacy/alerts", /Alerts/],
    ["/app/industry", /Analytics/], ["/app/industry/adr-trends", /ADR trends/], ["/app/industry/experience", /Patient experience/], ["/app/industry/signals", /Signals/], ["/app/industry/reports", /Reports/],
  ];
  it.each(cases)("%s", async (path, heading) => {
    open(path);
    await waitFor(() => expect(screen.getAllByRole("heading", { level: 1 }).some((h) => heading.test(h.textContent ?? "")) || screen.queryAllByText(heading).length > 0).toBe(true), { timeout: 4000 });
    // the design-system gallery intentionally renders a sample ErrorState
    if (path !== "/design-system") expect(screen.queryByText("Something went wrong")).toBeNull();
  });
});

describe("shell and safety labelling", () => {
  it("always shows the DEMO DATA banner inside the app", async () => {
    open("/app/patient");
    expect(await screen.findByText(/NOT FOR CLINICAL USE/)).toBeInTheDocument();
  });
  it("renders RTL Persian by default", async () => {
    open("/app/patient", "fa");
    await screen.findByText(/داده نمایشی/);
    expect(document.documentElement.dir).toBe("rtl");
    expect(await screen.findByText(/صبح بخیر|روز بخیر|عصر بخیر/)).toBeInTheDocument();
  });
  it("offers a skip link and a labelled main landmark", async () => {
    open("/app/patient/medications");
    expect((await screen.findAllByText("Skip to main content"))[0]).toHaveAttribute("href", "#main");
    expect(document.querySelector("main#main")).not.toBeNull();
  });
  it("professional roles show a role-specific navigation (IA differs per role)", async () => {
    open("/app/industry");
    const nav = await screen.findByRole("navigation", { name: "Industry" });
    expect(nav).toHaveTextContent("Signals");
    expect(nav).not.toHaveTextContent("Patients");
    cleanup();
    open("/app/pharmacy");
    expect(await screen.findByRole("navigation", { name: "Pharmacy" })).toHaveTextContent("Dispensing");
  });
  it("shows a 404 for unknown routes", async () => {
    open("/nope");
    expect(await screen.findByText("Page not found")).toBeInTheDocument();
  });
});

describe("patient journeys", () => {
  it("marks the next dose as taken and offers undo", async () => {
    const user = userEvent.setup();
    open("/app/patient");
    await user.click(await screen.findByRole("button", { name: "I took it" }));
    expect(await screen.findByText(/recorded as taken/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Undo" })).toBeInTheDocument();
  });
  it("the assistant answers with sources, confidence and a safety notice", async () => {
    const user = userEvent.setup();
    open("/app/patient/assistant");
    await user.click(await screen.findByRole("button", { name: /What should I do if I miss a dose/ }));
    expect(await screen.findByText(/Source match/, undefined, { timeout: 6000 })).toBeInTheDocument();
    expect(screen.getByText(/Informational only/)).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Sources" })).toBeInTheDocument();
  });
  it("check-in flags urgent symptoms with a non-diagnostic message", async () => {
    const user = userEvent.setup();
    open("/app/patient/checkin");
    await user.click(await screen.findByRole("radio", { name: /Good/ }));
    await user.click(screen.getByRole("button", { name: "Next" }));
    await user.click(screen.getByRole("checkbox", { name: "Chest pain" }));
    expect(screen.getByRole("alert")).toHaveTextContent("cannot assess emergencies");
  });
});

describe("industry privacy", () => {
  it("never shows counts below k", async () => {
    open("/app/industry/signals");
    await screen.findByText("Signal strength");
    expect(await screen.findAllByText("<11")).not.toHaveLength(0);
  });
});
