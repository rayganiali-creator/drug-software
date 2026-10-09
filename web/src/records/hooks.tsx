import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useAuth } from "../auth/AuthContext";
import { RecordsApi } from "./api";
import { RecordsError } from "./types";

export function useRecordsApi(): RecordsApi {
  const { backend } = useAuth();
  return useMemo(() => new RecordsApi(backend.apiFetch ? (p, i) => backend.apiFetch!(p, i) : undefined), [backend]);
}

/** The signed-in person's own id: the key of their patient record. */
export function useSelfId(): string {
  const { user } = useAuth();
  return user?.id ?? "";
}

export type Load<T> = { status: "loading" } | { status: "error"; error: unknown } | { status: "ready"; data: T };

/** Loads once, again when the dependencies change, and on demand (`reload`). Late answers of an outdated request are ignored. */
export function useLoad<T>(fn: (api: RecordsApi, signal: AbortSignal) => Promise<T>, deps: unknown[] = []): { state: Load<T>; reload(): void } {
  const api = useRecordsApi();
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

/** Runs a write: tracks "busy", keeps the last error for display, and calls `then` after success. */
export function useAction() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<RecordsError | null>(null);
  const run = useCallback(async (fn: () => Promise<unknown>, then?: () => void): Promise<boolean> => {
    setBusy(true);
    setError(null);
    try {
      await fn();
      then?.();
      return true;
    } catch (e) {
      setError(e instanceof RecordsError ? e : new RecordsError("server"));
      return false;
    } finally {
      setBusy(false);
    }
  }, []);
  return { busy, error, run, clear: () => setError(null) };
}
