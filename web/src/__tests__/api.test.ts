import { describe, expect, it, vi } from "vitest";
import { fetchReadiness } from "../api";

describe("fetchReadiness (Phase 1 API probe)", () => {
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
