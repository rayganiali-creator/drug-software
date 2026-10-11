import { RecordsError } from "../records/types";
import type { Assistance, CreateInput, Definitions, Detail, EntryKind, EventCategory, ReviewStatus, Summary } from "./types";

export type ApiFetch = (path: string, init?: RequestInit) => Promise<Response>;

/**
 * Client of the adverse-event endpoints. It holds no business rule: who may see or do what (role, relationship, consent, the record's own subject) is decided by
 * the server, and every refusal is one uniform 403. Without an authorised fetch (the in-browser demo accounts) every call says "not connected".
 */
export class AdrApi {
  constructor(private readonly apiFetch: ApiFetch | undefined) {}

  private async call<T>(method: string, path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
    if (!this.apiFetch) throw new RecordsError("notConnected");
    let res: Response;
    try {
      res = await this.apiFetch(path, { method, signal, ...(body === undefined ? {} : { body: JSON.stringify(body), headers: { "Content-Type": "application/json" } }) });
    } catch (e) {
      if (e instanceof DOMException && e.name === "AbortError") throw e;
      throw new RecordsError("network");
    }
    if (res.ok) return (await res.json()) as T;
    const problem = (await res.json().catch(() => ({}))) as { code?: string; errors?: string[] };
    if (res.status === 401 || res.status === 403) throw new RecordsError("unauthorized");
    if (res.status === 409) throw new RecordsError("conflict", problem.code, problem.errors);
    if (res.status === 400) throw new RecordsError("invalid", problem.code, problem.errors);
    if (res.status === 429) throw new RecordsError("rateLimited");
    throw new RecordsError("server");
  }

  private r = (rid: string, rest = "") => `/adverse-events/${encodeURIComponent(rid)}${rest}`;

  definitions = (s?: AbortSignal) => this.call<Definitions>("GET", "/adverse-events/definitions", undefined, s);
  queue = (statuses: ReviewStatus[], s?: AbortSignal) => this.call<Summary[]>("GET", `/adverse-events/queue?status=${statuses.join(",")}`, undefined, s);
  listFor = (subject: string, s?: AbortSignal) => this.call<Summary[]>("GET", `/patients/${encodeURIComponent(subject)}/adverse-events`, undefined, s);
  create = (subject: string, body: CreateInput) => this.call<Detail>("POST", `/patients/${encodeURIComponent(subject)}/adverse-events`, body);
  get = (rid: string, s?: AbortSignal) => this.call<Detail>("GET", this.r(rid), undefined, s);
  followUp = (rid: string, text: string) => this.call<Detail>("POST", this.r(rid, "/follow-ups"), { text });
  supplement = (rid: string, body: { onsetAt: string | null; resolvedAt: string | null; outcomeAsStated: string | null; note: string | null }) => this.call<Detail>("POST", this.r(rid, "/supplements"), body);
  addEntry = (rid: string, kind: EntryKind, text: string) => this.call<Detail>("POST", this.r(rid, "/entries"), { kind, text });
  classify = (rid: string, category: EventCategory, reason: string, expectedVersion: number) => this.call<Detail>("POST", this.r(rid, "/classification"), { category, reason, expectedVersion });
  setStatus = (rid: string, status: ReviewStatus, note: string | null, expectedVersion: number) => this.call<Detail>("POST", this.r(rid, "/status"), { status, note, expectedVersion });
  assist = (rid: string) => this.call<Assistance>("POST", this.r(rid, "/assistance"));
}
