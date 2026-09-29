import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { App } from "./App";
import { fetchReadiness } from "./api";

describe("App", () => {
  it("shows dependency statuses", async () => {
    const load = vi.fn().mockResolvedValue({ status: "Unhealthy", checks: { postgres: "Healthy", kafka: "Unhealthy" } });
    render(<App load={load} />);
    expect(await screen.findByText("Unhealthy", { selector: "strong" })).toBeInTheDocument();
    expect(screen.getByText("kafka: Unhealthy")).toBeInTheDocument();
  });

  it("shows an alert when the API is unreachable", async () => {
    render(<App load={() => Promise.reject(new Error("network"))} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("unreachable");
  });
});

describe("fetchReadiness", () => {
  it("returns the report even on HTTP 503", async () => {
    const fetchImpl = vi.fn().mockResolvedValue({ status: 503, json: async () => ({ status: "Unhealthy", checks: {} }) });
    await expect(fetchReadiness("http://x", fetchImpl as unknown as typeof fetch)).resolves.toEqual({ status: "Unhealthy", checks: {} });
    expect(fetchImpl).toHaveBeenCalledWith("http://x/health/ready", expect.anything());
  });

  it("rejects malformed bodies", async () => {
    const fetchImpl = vi.fn().mockResolvedValue({ json: async () => ({ nope: true }) });
    await expect(fetchReadiness("http://x", fetchImpl as unknown as typeof fetch)).rejects.toThrow();
  });
});
