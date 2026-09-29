import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import en from "../shared/en.json";
import fa from "../shared/fa.json";

export type Locale = "fa" | "en";
export type Dir = "rtl" | "ltr";
export interface Localized {
  en: string;
  fa: string;
}

const dictionaries: Record<Locale, Record<string, string>> = { en, fa };
const STORAGE_KEY = "ms.locale";
const FA_DIGITS = "۰۱۲۳۴۵۶۷۸۹";

export const dirOf = (locale: Locale): Dir => (locale === "fa" ? "rtl" : "ltr");

/** Pure translation helper (also used by tests and non-React code). */
export function translate(locale: Locale, key: string, params?: Record<string, string | number>): string {
  const raw = dictionaries[locale][key] ?? dictionaries.en[key] ?? key;
  return params ? raw.replace(/\{(\w+)\}/g, (_, k: string) => String(params[k] ?? `{${k}}`)) : raw;
}

export interface Formatters {
  number(n: number, opts?: Intl.NumberFormatOptions): string;
  percent(n: number): string;
  /** "08:30" -> "۰۸:۳۰" in Persian. */
  time(hhmm: string): string;
  digits(s: string): string;
  date(d: Date, style?: "short" | "long"): string;
  weekday(d: Date): string;
  monthShort(d: Date): string;
  relativeMinutes(minutesAgo: number): string;
}

function makeFormatters(locale: Locale, t: (k: string, p?: Record<string, string | number>) => string): Formatters {
  const tag = locale === "fa" ? "fa-IR-u-ca-persian-nu-arabext" : "en-US";
  const numTag = locale === "fa" ? "fa-IR" : "en-US";
  const digits = (s: string) => (locale === "fa" ? s.replace(/\d/g, (d) => FA_DIGITS[Number(d)] ?? d) : s);
  return {
    number: (n, opts) => new Intl.NumberFormat(numTag, opts).format(n),
    percent: (n) => `${new Intl.NumberFormat(numTag).format(n)}${locale === "fa" ? "٪" : "%"}`,
    time: digits,
    digits,
    date: (d, style = "long") =>
      new Intl.DateTimeFormat(tag, style === "long" ? { day: "numeric", month: "long", year: "numeric" } : { day: "numeric", month: "short" }).format(d),
    weekday: (d) => new Intl.DateTimeFormat(tag, { weekday: "long" }).format(d),
    monthShort: (d) => new Intl.DateTimeFormat(tag, { month: "short" }).format(d),
    relativeMinutes: (m) => {
      if (m < 1) return t("time.justNow");
      if (m < 60) return t("time.minutesAgo", { n: digits(String(Math.round(m))) });
      if (m < 1440) return t("time.hoursAgo", { n: digits(String(Math.round(m / 60))) });
      return t("time.daysAgo", { n: digits(String(Math.round(m / 1440))) });
    },
  };
}

interface I18nValue {
  locale: Locale;
  dir: Dir;
  setLocale(l: Locale): void;
  t(key: string, params?: Record<string, string | number>): string;
  /** Picks the current-language string of a Localized value. */
  loc(v: Localized): string;
  fmt: Formatters;
}

const Ctx = createContext<I18nValue | null>(null);

function initialLocale(): Locale {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved === "fa" || saved === "en") return saved;
  } catch {
    /* storage unavailable */
  }
  return "fa";
}

export function I18nProvider({ children, initial }: { children: ReactNode; initial?: Locale }) {
  const [locale, setLocaleState] = useState<Locale>(initial ?? initialLocale());
  const dir = dirOf(locale);

  useEffect(() => {
    document.documentElement.lang = locale;
    document.documentElement.dir = dir;
  }, [locale, dir]);

  const setLocale = useCallback((l: Locale) => {
    setLocaleState(l);
    try {
      localStorage.setItem(STORAGE_KEY, l);
    } catch {
      /* ignore */
    }
  }, []);

  const value = useMemo<I18nValue>(() => {
    const t = (key: string, params?: Record<string, string | number>) => translate(locale, key, params);
    return { locale, dir, setLocale, t, loc: (v) => v[locale], fmt: makeFormatters(locale, t) };
  }, [locale, dir, setLocale]);

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useI18n(): I18nValue {
  const v = useContext(Ctx);
  if (!v) throw new Error("useI18n must be used inside <I18nProvider>");
  return v;
}
