import { createContext, useContext, useEffect, useMemo, useState, type DependencyList, type ReactNode } from "react";
import { createMockServices } from "./mock/createMockServices";
import type { Services } from "./types";

const Ctx = createContext<Services | null>(null);

/** Phase 2 always provides Mock services. A real backend implementation can be swapped in here later. */
export function ServicesProvider({ children, services }: { children: ReactNode; services?: Services }) {
  const value = useMemo(() => services ?? createMockServices(), [services]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useServices(): Services {
  const v = useContext(Ctx);
  if (!v) throw new Error("useServices must be used inside <ServicesProvider>");
  return v;
}

export type AsyncState<T> = { status: "loading" } | { status: "error"; error: unknown } | { status: "ready"; data: T };

/** Loads data from a service with loading/error/ready states and a reload function. */
export function useAsync<T>(load: (s: Services) => Promise<T>, deps: DependencyList = []): AsyncState<T> & { reload(): void; setData(d: T): void } {
  const services = useServices();
  const [tick, setTick] = useState(0);
  // A fresh object per (services, tick, deps): results for an older key are ignored, so the
  // hook shows "loading" again without calling setState synchronously inside the effect.
  const signature = JSON.stringify(deps); // deps are primitives (ids, query values)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const key = useMemo(() => ({}), [services, tick, signature]);
  const [result, setResult] = useState<{ key: object; value: AsyncState<T> } | null>(null);
  useEffect(() => {
    let cancelled = false;
    load(services).then(
      (data) => !cancelled && setResult({ key, value: { status: "ready", data } }),
      (error) => !cancelled && setResult({ key, value: { status: "error", error } }),
    );
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);
  const state: AsyncState<T> = result && result.key === key ? result.value : { status: "loading" };
  return { ...state, reload: () => setTick((n) => n + 1), setData: (data: T) => setResult({ key, value: { status: "ready", data } }) };
}
