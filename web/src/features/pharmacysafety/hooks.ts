import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useAuth, useUser } from "../../auth/AuthContext";
import { PharmacyApi } from "../../pharmacy/api";
import type { Load } from "../../records/hooks";

export function usePharmacyApi(): PharmacyApi {
  const { backend } = useAuth();
  return useMemo(() => new PharmacyApi(backend.apiFetch ? (p, i) => backend.apiFetch!(p, i) : undefined), [backend]);
}

/** The pharmacy organization of the signed-in professional (the first one they belong to). The server still checks membership on every call. */
export function useOrganizationId(): string | undefined {
  return useUser().organizationIds[0];
}

export function usePharmacyLoad<T>(fn: (api: PharmacyApi, signal: AbortSignal) => Promise<T>, deps: unknown[] = []): { state: Load<T>; reload(): void } {
  const api = usePharmacyApi();
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
