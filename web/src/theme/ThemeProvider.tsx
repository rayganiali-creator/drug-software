import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";

export type ThemeMode = "system" | "light" | "dark";
const STORAGE_KEY = "ms.theme";

interface ThemeValue {
  mode: ThemeMode;
  setMode(m: ThemeMode): void;
}
const Ctx = createContext<ThemeValue | null>(null);

function initialMode(): ThemeMode {
  try {
    const s = localStorage.getItem(STORAGE_KEY);
    if (s === "light" || s === "dark" || s === "system") return s;
  } catch {
    /* ignore */
  }
  return "system";
}

export function ThemeProvider({ children, initial }: { children: ReactNode; initial?: ThemeMode }) {
  const [mode, setModeState] = useState<ThemeMode>(initial ?? initialMode());

  useEffect(() => {
    const root = document.documentElement;
    if (mode === "system") root.removeAttribute("data-theme");
    else root.setAttribute("data-theme", mode);
  }, [mode]);

  const setMode = useCallback((m: ThemeMode) => {
    setModeState(m);
    try {
      localStorage.setItem(STORAGE_KEY, m);
    } catch {
      /* ignore */
    }
  }, []);

  const value = useMemo(() => ({ mode, setMode }), [mode, setMode]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useTheme(): ThemeValue {
  const v = useContext(Ctx);
  if (!v) throw new Error("useTheme must be used inside <ThemeProvider>");
  return v;
}
