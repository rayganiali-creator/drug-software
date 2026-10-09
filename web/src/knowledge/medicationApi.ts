import { KnowledgeError, type MedicationDetail, type MedicationSummary, type Paged } from "./types";

/** An authorised fetch relative to the API base URL. Supplied by the auth backend (it owns the token; this code never sees it). */
export type ApiFetch = (path: string, init?: RequestInit) => Promise<Response>;

export interface SearchParams { q?: string; limit?: number; offset?: number }

/**
 * Client of the medication reference endpoints. It holds no secret: the bearer token lives inside the auth backend's `apiFetch`.
 * When no authorised fetch exists (the in-browser demo accounts) every call fails with `notConnected` instead of pretending.
 */
export class MedicationApi {
  constructor(private readonly apiFetch: ApiFetch | undefined) {}

  async search(p: SearchParams, signal?: AbortSignal): Promise<Paged<MedicationSummary>> {
    const qs = new URLSearchParams();
    if (p.q?.trim()) qs.set("q", p.q.trim());
    qs.set("limit", String(p.limit ?? 20));
    qs.set("offset", String(p.offset ?? 0));
    return this.get<Paged<MedicationSummary>>(`/medications/search?${qs}`, signal);
  }

  async detail(id: string, signal?: AbortSignal): Promise<MedicationDetail> {
    return this.get<MedicationDetail>(`/medications/${encodeURIComponent(id)}`, signal);
  }

  private async get<T>(path: string, signal?: AbortSignal): Promise<T> {
    if (!this.apiFetch) throw new KnowledgeError("notConnected");
    let res: Response;
    try {
      res = await this.apiFetch(path, { signal });
    } catch (e) {
      if (e instanceof DOMException && e.name === "AbortError") throw e;
      throw new KnowledgeError("network");
    }
    if (res.ok) return (await res.json()) as T;
    if (res.status === 401 || res.status === 403) throw new KnowledgeError("unauthorized");
    if (res.status === 404) throw new KnowledgeError("notFound");
    if (res.status === 429) throw new KnowledgeError("rateLimited");
    if (res.status === 400) {
      const body = (await res.json().catch(() => ({}))) as { code?: string };
      throw new KnowledgeError("invalid", body.code);
    }
    throw new KnowledgeError("server");
  }
}
