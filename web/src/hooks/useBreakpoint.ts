import { useEffect, useState } from "react";
import { breakpoints, type Breakpoint } from "../design/tokens.g";

export function classify(width: number): Breakpoint {
  if (width >= breakpoints.large) return "large";
  if (width >= breakpoints.expanded) return "expanded";
  if (width >= breakpoints.medium) return "medium";
  return "compact";
}

/** Window size class from the shared breakpoint tokens. */
export function useBreakpoint(): Breakpoint {
  const [bp, setBp] = useState<Breakpoint>(() => classify(window.innerWidth));
  useEffect(() => {
    const onResize = () => setBp(classify(window.innerWidth));
    window.addEventListener("resize", onResize);
    return () => window.removeEventListener("resize", onResize);
  }, []);
  return bp;
}

export function useReducedMotion(): boolean {
  const [reduced, setReduced] = useState(() => window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false);
  useEffect(() => {
    const mq = window.matchMedia?.("(prefers-reduced-motion: reduce)");
    if (!mq) return;
    const on = () => setReduced(mq.matches);
    mq.addEventListener("change", on);
    return () => mq.removeEventListener("change", on);
  }, []);
  return reduced;
}
