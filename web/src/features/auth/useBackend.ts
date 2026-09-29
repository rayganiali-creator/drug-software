import { useEffect, useRef, useState } from "react";

/** Loads something from the auth backend with a reload trigger (ignores results of stale calls). */
export function useLoad<T>(load: () => Promise<T>, initial: T): [T, boolean, () => void] {
  const [data, setData] = useState<T>(initial);
  const [ready, setReady] = useState(false);
  const [tick, setTick] = useState(0);
  const loader = useRef(load);
  useEffect(() => { loader.current = load; });
  useEffect(() => {
    let live = true;
    loader.current().then((d) => { if (live) { setData(d); setReady(true); } }, () => { if (live) setReady(true); });
    return () => { live = false; };
  }, [tick]);
  return [data, ready, () => setTick((n) => n + 1)];
}
