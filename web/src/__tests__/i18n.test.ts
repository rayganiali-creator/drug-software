import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import en from "../shared/en.json";
import fa from "../shared/fa.json";
import { dirOf, translate } from "../i18n/I18nProvider";

const walk = (d: string): string[] => readdirSync(d).flatMap((f) => { const p = join(d, f); return statSync(p).isDirectory() ? walk(p) : [p]; });
const src = walk("src").filter((f) => /\.tsx?$/.test(f) && !f.includes("__tests__"));

describe("translations", () => {
  it("English and Persian have the same keys", () => {
    expect(Object.keys(fa).sort()).toEqual(Object.keys(en).sort());
  });

  it("every literal t('key') used in source exists", () => {
    const missing: string[] = [];
    for (const f of src) {
      for (const m of readFileSync(f, "utf8").matchAll(/\bt\(\s*"([^"]+)"/g)) if (!(m[1]! in en)) missing.push(`${f}: ${m[1]}`);
    }
    expect(missing).toEqual([]);
  });

  it("dynamic key families are complete", () => {
    const families: Record<string, string[]> = {
      "risk.": ["low", "medium", "high"], "severity.": ["mild", "moderate", "serious"], "sex.": ["F", "M"],
      "rx.status.": ["active", "pending-review", "dispensed"], "adr.status.": ["new", "under-review", "reviewed"],
      "adr.causality.": ["unassessed", "unlikely", "possible", "probable"], "ix.severity.": ["minor", "moderate", "major"],
      "ai.confidence.": ["high", "medium", "low"], "ind.status.": ["new", "monitoring", "closed"], "ind.strength.": ["weak", "moderate", "strong"],
      "pharm.priority.": ["high", "normal"], "pharmacy.alert.": ["low-stock", "expiry", "recall-demo"], "pharmacy.kind.": ["refill", "question", "delivery"],
      "pharmacy.stage.": ["received", "preparing", "ready", "handed"], "pharmacy.status.": ["ok", "low", "expiring"],
      "role.": ["patient", "physician", "pharmacist", "pharmacy", "industry"], "theme.": ["system", "light", "dark"],
      "lp.role.": ["patient", "pharmacist", "physician", "pharmacy"], "lp.int.": ["rx", "pharmacy", "insurance", "drugdb", "identity"],
      "nav.": ["home", "medications", "assistant", "checkin", "profile", "dashboard", "patients", "prescriptions", "adr", "reports", "workQueue", "reviews", "interactions", "adherence", "questions", "followups", "overview", "inventory", "dispensing", "requests", "alerts", "analytics", "adr-trends", "experience", "signals"],
    };
    const missing = Object.entries(families).flatMap(([p, ks]) => ks.map((k) => p + k)).filter((k) => !(k in en));
    for (const k of ["problem1", "problem2", "problem3", "sol1", "sol2", "sol3", "step1", "step2", "step3", "step4"]) for (const s of ["Title", "Body"]) if (!(`lp.${k}${s}` in en)) missing.push(`lp.${k}${s}`);
    for (const n of [1, 2, 3, 4]) if (!(`lp.ai${n}` in en)) missing.push(`lp.ai${n}`);
    for (const n of [1, 2, 3]) for (const k of ["safe", "priv"]) for (const s of ["Title", "Body"]) if (!(`lp.${k}${n}${s}` in en)) missing.push(`lp.${k}${n}${s}`);
    for (const r of ["patient", "pharmacist", "physician", "pharmacy"]) for (const n of [1, 2, 3]) if (!(`lp.role.${r}.${n}` in en)) missing.push(`lp.role.${r}.${n}`);
    expect(missing).toEqual([]);
  });

  it("interpolates parameters and falls back to the key", () => {
    expect(translate("en", "home.inMinutes", { n: 5 })).toBe("In 5 min");
    expect(translate("fa", "home.inMinutes", { n: "۵" })).toBe("۵ دقیقه دیگر");
    expect(translate("en", "does.not.exist")).toBe("does.not.exist");
  });

  it("maps locales to directions (RTL is not hard-coded)", () => {
    expect(dirOf("fa")).toBe("rtl");
    expect(dirOf("en")).toBe("ltr");
  });
});

describe("demo labelling", () => {
  it("the demo banner text says NOT FOR CLINICAL USE in both languages", () => {
    expect(en["demo.banner"]).toMatch(/NOT FOR CLINICAL USE/);
    expect(fa["demo.banner"]).toMatch(/غیرقابل استفاده بالینی/);
  });
});
