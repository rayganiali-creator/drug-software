import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useAuth } from "../../auth/AuthContext";
import { AdrApi } from "../../adr/api";
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

/** Open-queue count as the server reports it (already limited to the patients this reviewer may read), with whether it is known to be complete. Never a sample number. */
export function useOpenAdrCount(): Load<{ count: number; complete: boolean }> {
  const { state } = useAdrLoad(async (a, s) => {
    const r = await a.queueWithCompleteness(["Submitted", "InReview", "InformationRequested"], s);
    return { count: r.rows.length, complete: r.complete };
  });
  return state;
}
