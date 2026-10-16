import { RecordsError } from "../records/types";
import type { Concern, ConcernDetail, CreateConcern, CreateEvent, EntryKind, EventDetail, SafetyStatus, Summary } from "./types";

export type ApiFetch = (path: string, init?: RequestInit) => Promise<Response>;

/**
 * Client of the internal pharmacy safety endpoints. It holds no business rule: organization membership and permissions are decided by the server and every refusal is one uniform
 * 403. Without an authorised fetch (the in-browser demo accounts) every call says "not connected".
 */
export class PharmacyApi {
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

  private org = (o: string, rest: string) => `/organizations/${encodeURIComponent(o)}/${rest}`;

  events = (org: string, s?: AbortSignal) => this.call<Summary[]>("GET", this.org(org, "safety-events"), undefined, s);
  createEvent = (org: string, b: CreateEvent) => this.call<EventDetail>("POST", this.org(org, "safety-events"), b);
  event = (id: string, s?: AbortSignal) => this.call<EventDetail>("GET", `/safety-events/${encodeURIComponent(id)}`, undefined, s);
  eventEntry = (id: string, kind: EntryKind, text: string) => this.call<EventDetail>("POST", `/safety-events/${encodeURIComponent(id)}/entries`, { kind, text });
  eventStatus = (id: string, status: SafetyStatus, note: string | null, expectedVersion: number) => this.call<EventDetail>("POST", `/safety-events/${encodeURIComponent(id)}/status`, { status, note, expectedVersion });

  concerns = (org: string, s?: AbortSignal) => this.call<Summary[]>("GET", this.org(org, "product-concerns"), undefined, s);
  createConcern = (org: string, b: CreateConcern) => this.call<ConcernDetail>("POST", this.org(org, "product-concerns"), b);
  concern = (id: string, s?: AbortSignal) => this.call<ConcernDetail>("GET", `/product-concerns/${encodeURIComponent(id)}`, undefined, s);
  concernEntry = (id: string, kind: EntryKind, text: string) => this.call<ConcernDetail>("POST", `/product-concerns/${encodeURIComponent(id)}/entries`, { kind, text });
  concernStatus = (id: string, status: SafetyStatus, note: string | null, expectedVersion: number) => this.call<ConcernDetail>("POST", `/product-concerns/${encodeURIComponent(id)}/status`, { status, note, expectedVersion });
  propose = (id: string, note: string, expectedVersion: number) => this.call<ConcernDetail>("POST", `/product-concerns/${encodeURIComponent(id)}/manufacturer-report-proposal`, { note, expectedVersion });
}

export type { Concern };
