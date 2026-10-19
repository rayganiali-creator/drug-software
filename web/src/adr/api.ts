import { RecordsError } from "../records/types";
import type { Assistance, CreateInput, Definitions, Detail, EntryKind, EventCategory, ReviewStatus, Summary } from "./types";

export type ApiFetch = (path: string, init?: RequestInit) => Promise<Response>;

/**
 * Client of the adverse-event endpoints. It holds no business rule: who may see or do what (role, relationship, consent, the record's own subject) is decided by
 * the server, and every refusal is one uniform 403. Without an authorised fetch (the in-browser demo accounts) every call says "not connected".
 */
/** What the server said about the queue: "truncated" (header exactly "true"), "complete" (exactly "false"), or "unknown" (missing or any other value: nothing is claimed). */
export type QueueCompleteness = "complete" | "truncated" | "unknown";
export const queueCompleteness = (header: string | null): QueueCompleteness => (header === "true" ? "truncated" : header === "false" ? "complete" : "unknown");

/** An exact count only when the server sent a plain non-negative integer; anything else is "no count" (never a number made up from the rows in hand). */
export const queueTotal = (header: string | null): number | null => (header !== null && /^\d{1,9}$/.test(header) ? Number(header) : null);

export interface QueuePage { rows: Summary[]; completeness: QueueCompleteness; complete: boolean; nextCursor: string | null; total: number | null }

export class AdrApi {
  constructor(private readonly apiFetch: ApiFetch | undefined) {}

  private async send(method: string, path: string, body?: unknown, signal?: AbortSignal): Promise<Response> {
    if (!this.apiFetch) throw new RecordsError("notConnected");
    let res: Response;
    try {
      res = await this.apiFetch(path, { method, signal, ...(body === undefined ? {} : { body: JSON.stringify(body), headers: { "Content-Type": "application/json" } }) });
    } catch (e) {
      if (e instanceof DOMException && e.name === "AbortError") throw e;
      throw new RecordsError("network");
    }
    if (res.ok) return res;
    const problem = (await res.json().catch(() => ({}))) as { code?: string; errors?: string[] };
    if (res.status === 401 || res.status === 403) throw new RecordsError("unauthorized");
    if (res.status === 409) throw new RecordsError("conflict", problem.code, problem.errors);
    if (res.status === 400) throw new RecordsError("invalid", problem.code, problem.errors);
    if (res.status === 429) throw new RecordsError("rateLimited");
    throw new RecordsError("server");
  }

  private async call<T>(method: string, path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
    return (await (await this.send(method, path, body, signal)).json()) as T;
  }

  private r = (rid: string, rest = "") => `/adverse-events/${encodeURIComponent(rid)}${rest}`;

  definitions = (s?: AbortSignal) => this.call<Definitions>("GET", "/adverse-events/definitions", undefined, s);
  /**
   * One page of the reviewer's queue. The server decides which patients may be read and pages their records; this only reads what it says in the headers:
   * `X-Result-Truncated` ("true" = more records can be fetched, "false" = this is the end, anything else = not known), `X-Next-Cursor` (opaque; only present when there is more),
   * and `X-Total-Count` (first page only; a non-negative integer, anything else = no count). Only an exact "false" means complete.
   */
  queuePage = async (statuses: ReviewStatus[], opts: { cursor?: string | null; limit?: number } = {}, s?: AbortSignal): Promise<QueuePage> => {
    const query = `status=${statuses.join(",")}${opts.limit === undefined ? "" : `&limit=${opts.limit}`}${opts.cursor ? `&cursor=${encodeURIComponent(opts.cursor)}` : ""}`;
    const res = await this.send("GET", `/adverse-events/queue?${query}`, undefined, s);
    const rows = (await res.json()) as Summary[];
    const completeness = queueCompleteness(res.headers.get("X-Result-Truncated"));
    const cursor = completeness === "truncated" ? res.headers.get("X-Next-Cursor") : null;
    return { rows, completeness, complete: completeness === "complete", nextCursor: cursor && cursor.length > 0 ? cursor : null, total: queueTotal(res.headers.get("X-Total-Count")) };
  };
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
