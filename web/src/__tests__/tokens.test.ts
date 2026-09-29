import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { breakpoints, colorNames, themeColors } from "../design/tokens.g";

const walk = (d: string): string[] => readdirSync(d).flatMap((f) => { const p = join(d, f); return statSync(p).isDirectory() ? walk(p) : [p]; });
const css = walk("src").filter((f) => f.endsWith(".css") && !f.endsWith("tokens.css"));
const tsx = walk("src").filter((f) => /\.tsx$/.test(f) && !f.includes("__tests__"));

describe("design tokens", () => {
  it("defines every semantic colour for light and dark", () => {
    for (const mode of ["light", "dark"] as const) for (const n of colorNames) expect(themeColors[mode][n]).toMatch(/^#[0-9A-F]{6}$/);
  });

  it("generated CSS exposes tokens for both themes and reduced motion", () => {
    const tokens = readFileSync("src/design/tokens.css", "utf8");
    for (const v of ["--color-primary", "--color-accent", "--space-4", "--radius-lg", "--shadow-2", "--duration-base", "--control-md", "--icon-md", "--text-body-size"]) expect(tokens).toContain(v);
    expect(tokens).toContain('[data-theme="dark"]');
    expect(tokens).toContain("prefers-color-scheme: dark");
    expect(tokens).toContain("prefers-reduced-motion");
  });

  it("uses the shared breakpoints", () => {
    expect(breakpoints).toEqual({ compact: 0, medium: 600, expanded: 1024, large: 1440 });
  });
});

describe("RTL/LTR safety", () => {
  it("component CSS uses logical properties only (no left/right)", () => {
    const bad: string[] = [];
    for (const f of css) {
      readFileSync(f, "utf8").split("\n").forEach((line, i) => {
        if (/(^|[\s;{])(margin|padding|border)-(left|right)\b|(^|[\s;{])(left|right)\s*:|text-align:\s*(left|right)|border-(top|bottom)-(left|right)-radius/.test(line)) bad.push(`${f}:${i + 1}: ${line.trim()}`);
      });
    }
    expect(bad).toEqual([]);
  });

  it("no hard-coded hex colours in components or pages", () => {
    const bad: string[] = [];
    for (const f of [...css, ...tsx]) {
      readFileSync(f, "utf8").split("\n").forEach((line, i) => {
        if (/#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b/.test(line) && !/&#|href=|url\(#|\/\//.test(line)) bad.push(`${f}:${i + 1}: ${line.trim().slice(0, 90)}`);
      });
    }
    expect(bad).toEqual([]);
  });
});
