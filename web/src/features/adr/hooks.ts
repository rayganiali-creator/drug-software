import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useAuth } from "../../auth/AuthContext";
import { AdrApi, type QueueCompleteness } from "../../adr/api";
import type { ReviewStatus, Summary } from "../../adr/types";
import { RecordsError } from "../../records/types";
import type { Load } from "../../records/hooks";

export function useAdrApi(): AdrApi {
  const { backend } = useAuth();
  return useMemo(() => new AdrApi(backend.apiFetch ? (p, i) => backend.apiFetch!(p, i) : undefined), [backend]);
}

/** Loads once, again when the dependencies change, and on demand. Late answers of an outdated request are ignored. */
export function useAdrLoad<T>(fn: (api: AdrApi, signal: AbortSignal) => Promise<T>, deps: unknown[] = []): { state: Load<T>; reload(): void } {
  const api = useAdrApi();
  const [state, setState] = useState<Load<T>>({ status: "loading" });
  const [tick, setTick] = useState(0);
  const fnRef = useRef(fn);
  useEffect(() => { fnRef.current = fn; });
  useEffect(() => {
    const ctl = new AbortController();
    fnRef.current(api, ctl.signal).then(
      (data) => { if (!ctl.signal.aborted) setState({ status: "ready", data }); },
      (error) => { if (!ctl.signal.aborted && !(error instanceof DOMException)) setState({ status: "error", error }); },
    );
    return () => ctl.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api, tick, ...deps]);
  const reload = useCallback(() => setTick((n) => n + 1), []);
  return { state, reload };
}

/** Page size asked for by the queue page (the server's default and its maximum are 50 and 100). */
export const QUEUE_PAGE = 50;
const OPEN: ReviewStatus[] = ["Submitted", "InReview", "InformationRequested"];

export interface QueueView {
  rows: Summary[];
  /** What the LAST page fetched said: only "complete" means the end of the list was reached. */
  completeness: QueueCompleteness;
  /** Present only while the server says more records can be fetched. */
  nextCursor: string | null;
  loadedAt: Date;
  busy: boolean;
  /** "failed": the next page could not be loaded (rows stay); "expired": the server no longer accepts the place in the list. */
  moreError: "failed" | "expired" | null;
  loadMore(): void;
}

type More = { rows: Summary[]; completeness: QueueCompleteness | null; nextCursor: string | null; busy: boolean; error: "failed" | "expired" | null };
const NO_MORE: More = { rows: [], completeness: null, nextCursor: null, busy: false, error: null };

/**
 * The reviewer's queue, one page at a time. The first page is loaded like any other list; "load more" follows the cursor the server sent and appends. A new first load (refresh, other
 * filter) drops everything that was appended. A refusal while paging (access ended) goes back to the normal refusal state; a failed page keeps the rows already shown.
 */
export function useAdrQueue(statuses: ReviewStatus[], key: string): { state: Load<QueueView>; reload(): void } {
  const api = useAdrApi();
  const first = useAdrLoad(async (a, s) => ({ ...(await a.queuePage(statuses, { limit: QUEUE_PAGE }, s)), loadedAt: new Date() }), [key]);
  // What was appended belongs to ONE first load (`owner`); a new first load (refresh, other filter) makes it stale without any reset step.
  const firstState = first.state;
  const [kept, setKept] = useState<{ owner: unknown; more: More }>({ owner: null, more: NO_MORE });
  const more = kept.owner === firstState ? kept.more : NO_MORE;
  const current = useRef<unknown>(firstState);
  useEffect(() => { current.current = firstState; });
  const update = useCallback((owner: unknown, fn: (m: More) => More) => setKept((k) => ({ owner, more: fn(k.owner === owner ? k.more : NO_MORE) })), []);

  const reload = first.reload;
  const loadMore = useCallback(() => {
    if (firstState.status !== "ready" || more.busy) return;
    const cursor = more.completeness === null ? firstState.data.nextCursor : more.nextCursor;
    if (!cursor) return;
    update(firstState, (m) => ({ ...m, busy: true, error: null }));
    api.queuePage(statuses, { cursor, limit: QUEUE_PAGE }).then(
      (p) => { if (current.current === firstState) update(firstState, (m) => ({ rows: [...m.rows, ...p.rows.filter((r) => !m.rows.some((x) => x.id === r.id))], completeness: p.completeness, nextCursor: p.nextCursor, busy: false, error: null })); },
      (e) => {
        if (current.current !== firstState) return;
        if (e instanceof RecordsError && e.kind === "unauthorized") { reload(); return; }
        update(firstState, (m) => ({ ...m, busy: false, error: e instanceof RecordsError && e.kind === "invalid" ? "expired" : "failed" }));
      },
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api, firstState, more, update, reload, key]);

  if (firstState.status !== "ready") return { state: firstState, reload };
  const f = firstState.data;
  const ids = new Set(f.rows.map((r) => r.id));
  const view: QueueView = {
    rows: [...f.rows, ...more.rows.filter((r) => !ids.has(r.id))],
    completeness: more.completeness ?? f.completeness,
    nextCursor: more.completeness === null ? f.nextCursor : more.nextCursor,
    loadedAt: f.loadedAt,
    busy: more.busy,
    moreError: more.error,
    loadMore,
  };
  return { state: { status: "ready", data: view }, reload };
}

/** Open-queue count as the server states it for the patients this reviewer may read. Only a plain integer from the server is a count; otherwise null (never a number made from rows in hand). */
export function useOpenAdrCount(): Load<{ count: number | null }> {
  const { state } = useAdrLoad(async (a, s) => ({ count: (await a.queuePage(OPEN, { limit: 1 }, s)).total }));
  return state;
}
