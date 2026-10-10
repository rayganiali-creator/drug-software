import { SafetyError, type Assessment, type AssessmentSummary, type RuleCoverage, type RuleSummary } from "./types";

export type ApiFetch = (path: string, init?: RequestInit) => Promise<Response>;

/**
 * Client of the clinical safety endpoints. It sends the route patient id only; who may see what (ownership, care relationship, consent) is decided by the server,
 * and a refusal is one uniform 403. Without an authorised fetch (in-browser demo accounts) every call says "not connected" instead of pretending to work.
 */
export class SafetyApi {
  constructor(private readonly apiFetch: ApiFetch | undefined, private readonly timeoutMs = 30_000) {}

  private async call<T>(method: string, path: string, outer?: AbortSignal): Promise<T> {
    if (!this.apiFetch) throw new SafetyError("notConnected");
    const ctl = new AbortController();
    const timer = setTimeout(() => ctl.abort(), this.timeoutMs);
    outer?.addEventListener("abort", () => ctl.abort(), { once: true });
    let res: Response;
    try {
      res = await this.apiFetch(path, { method, signal: ctl.signal });
    } catch (e) {
      if (e instanceof DOMException && e.name === "AbortError") { if (outer?.aborted) throw e; throw new SafetyError("network"); }
      throw new SafetyError("network");
    } finally {
      clearTimeout(timer);
    }
    if (res.ok) return (await res.json()) as T;
    const problem = (await res.json().catch(() => ({}))) as { code?: string };
    if (res.status === 401 || res.status === 403) throw new SafetyError("unauthorized");
    if (res.status === 404) throw new SafetyError("notFound", problem.code);
    if (res.status === 400) throw new SafetyError("invalid", problem.code);
    if (res.status === 429) throw new SafetyError("rateLimited");
    if (res.status === 503) throw new SafetyError("unavailable", problem.code);
    throw new SafetyError("server");
  }

  private p = (id: string, rest: string) => `/patients/${encodeURIComponent(id)}/safety/${rest}`;

  run = (id: string, locale: "fa" | "en", s?: AbortSignal) => this.call<Assessment>("POST", this.p(id, `assessments?locale=${locale}`), s);
  latest = (id: string, s?: AbortSignal) => this.call<Assessment>("GET", this.p(id, "assessments/latest"), s);
  list = (id: string, s?: AbortSignal) => this.call<AssessmentSummary[]>("GET", this.p(id, "assessments?take=10"), s);
  get = (id: string, aid: string, s?: AbortSignal) => this.call<Assessment>("GET", this.p(id, `assessments/${encodeURIComponent(aid)}`), s);
  coverage = (s?: AbortSignal) => this.call<RuleCoverage>("GET", "/safety/coverage", s);
  rules = (s?: AbortSignal) => this.call<RuleSummary[]>("GET", "/clinical-rules?take=100", s);
}
