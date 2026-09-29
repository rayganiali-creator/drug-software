export type HealthState = "Healthy" | "Degraded" | "Unhealthy";

export interface HealthReport {
  status: HealthState;
  checks: Record<string, HealthState>;
}

export const apiBaseUrl: string = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080";

/**
 * Reads the API readiness probe. A 503 still carries a valid report (which dependency is down),
 * so it is returned rather than thrown; only network/shape errors reject.
 */
export async function fetchReadiness(
  baseUrl: string = apiBaseUrl,
  fetchImpl: typeof fetch = fetch,
): Promise<HealthReport> {
  const res = await fetchImpl(`${baseUrl}/health/ready`, { headers: { Accept: "application/json" } });
  const body: unknown = await res.json();
  if (!isHealthReport(body)) {
    throw new Error("Unexpected health response");
  }
  return body;
}

function isHealthReport(v: unknown): v is HealthReport {
  if (typeof v !== "object" || v === null) return false;
  const r = v as Record<string, unknown>;
  return typeof r.status === "string" && typeof r.checks === "object" && r.checks !== null;
}
