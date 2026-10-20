import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it } from "vitest";
import { AppProviders } from "../AppProviders";
import { ApiBackend } from "../auth/apiBackend";
import { routes } from "../routes";
import { createMockServices } from "../services/mock/createMockServices";

afterEach(cleanup);

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const account = { id: "demo-patient", displayName: { en: "DEMO Patient", fa: "بیمار" }, description: { en: "d", fa: "د" }, roles: ["Patient"], primary: true, status: "Active" };

function gatedApp() {
  const seen: (string | null)[] = [];
  const fetchImpl = (async (url: string | URL | Request, init?: RequestInit) => {
    const code = new Headers(init?.headers).get("X-Demo-Code");
    seen.push(code);
    if (String(url).endsWith("/auth/demo-accounts")) return code === "the-right-code" ? json(200, { accounts: [account] }) : json(401, { code: code ? "demo_code_invalid" : "demo_code_required" });
    return json(401, {});
  }) as unknown as typeof fetch;
  const be = new ApiBackend({ baseUrl: "http://api.test", fetchImpl, storage: null, demoGate: true });
  render(<AppProviders locale="en" theme="light" auth={be} services={createMockServices({ latencyMs: 0 })}><RouterProvider router={createMemoryRouter(routes, { initialEntries: ["/login"] })} /></AppProviders>);
  return { seen };
}

describe("demo access code on the sign-in page", () => {
  it("asks for the code first, shows no accounts until it is accepted, and never lists accounts for a wrong code", async () => {
    const { seen } = gatedApp();
    const input = await screen.findByLabelText("Access code");
    expect(input).toHaveAttribute("type", "password");
    expect(screen.queryByText("DEMO Patient")).not.toBeInTheDocument();
    expect(seen).toEqual([]); // nothing is requested before a code is entered

    await userEvent.type(input, "wrong-code");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    expect(await screen.findByText("That access code was not accepted.")).toBeInTheDocument();
    expect(screen.queryByText("DEMO Patient")).not.toBeInTheDocument();

    await userEvent.type(screen.getByLabelText("Access code"), "the-right-code");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    await waitFor(() => expect(screen.getByText("DEMO Patient")).toBeInTheDocument());
    expect(screen.queryByLabelText("Access code")).not.toBeInTheDocument();
  });
});
