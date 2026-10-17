import { RecordsError } from "../records/types";

export type ApiFetch = (path: string, init?: RequestInit) => Promise<Response>;
export type Basis = "Real" | "SimulatedOrDemo";
export type Cell = { key: string; label: string | null; reports: number | null; suppressed: boolean };
export type Insights = {
  organizationId: string; from: string; until: string; period: string; availablePeriods: string[]; basis: Basis; linked: boolean; minCellSize: number; productsTruncated: boolean;
  byProduct: Cell[]; byIssueType: Cell[]; bySeverity: Cell[]; byMonth: Cell[];
};

/** Client of the aggregate industry endpoint. It holds no rule: membership, scope and suppression are decided by the server. Without an authorised fetch every call says "not connected". */
export class IndustryApi {
  constructor(private readonly apiFetch: ApiFetch | undefined) {}

  async insights(org: string, basis: Basis, period: string | null, signal?: AbortSignal): Promise<Insights> {
    if (!this.apiFetch) throw new RecordsError("notConnected");
    let res: Response;
    try {
      const q = new URLSearchParams({ basis, ...(period ? { period } : {}) });
      res = await this.apiFetch(`/organizations/${encodeURIComponent(org)}/industry/insights?${q}`, { method: "GET", signal });
    } catch (e) {
      if (e instanceof DOMException && e.name === "AbortError") throw e;
      throw new RecordsError("network");
    }
    if (res.ok) return (await res.json()) as Insights;
    const problem = (await res.json().catch(() => ({}))) as { code?: string; errors?: string[] };
    if (res.status === 401 || res.status === 403) throw new RecordsError("unauthorized");
    if (res.status === 400) throw new RecordsError("invalid", problem.code, problem.errors);
    if (res.status === 429) throw new RecordsError("rateLimited");
    throw new RecordsError("server");
  }
}
