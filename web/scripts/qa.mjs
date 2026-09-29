#!/usr/bin/env node
// Visual + accessibility QA against a running preview server (npm run build && npm run preview).
//   node scripts/qa.mjs [--out <dir>] [--base http://127.0.0.1:4173] [--shots]
// Uses playwright-core with a locally installed Chromium (CHROMIUM_PATH) and axe-core for WCAG checks.
import { mkdirSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join } from "node:path";
import { chromium } from "playwright-core";

const require = createRequire(import.meta.url);
const axeSource = readFileSync(require.resolve("axe-core/axe.min.js"), "utf8");
const arg = (name, def) => { const i = process.argv.indexOf(name); return i > -1 ? process.argv[i + 1] : def; };
const base = arg("--base", "http://127.0.0.1:4173");
const out = arg("--out", "qa-out");
const shots = process.argv.includes("--shots");
const executablePath = process.env.CHROMIUM_PATH ?? "/opt/pw-browsers/chromium-1194/chrome-linux/chrome";
mkdirSync(out, { recursive: true });

const pages = [
  "/", "/design-system",
  "/app/patient", "/app/patient/medications", "/app/patient/medications/drug-demopril", "/app/patient/assistant", "/app/patient/checkin", "/app/patient/profile",
  "/app/physician", "/app/physician/patients", "/app/physician/patients/pt-sara", "/app/physician/prescriptions", "/app/physician/adr", "/app/physician/reports",
  "/app/pharmacist", "/app/pharmacist/reviews", "/app/pharmacist/interactions", "/app/pharmacist/adr", "/app/pharmacist/adherence", "/app/pharmacist/questions", "/app/pharmacist/followups",
  "/app/pharmacy", "/app/pharmacy/inventory", "/app/pharmacy/prescriptions", "/app/pharmacy/dispensing", "/app/pharmacy/requests", "/app/pharmacy/alerts",
  "/app/industry", "/app/industry/adr-trends", "/app/industry/experience", "/app/industry/signals", "/app/industry/reports",
  // Phase 3: sign-in, account & security, administration, workspace and the neutral no-access page ("@account" = fictional demo account to sign in as)
  "/login@", "/app/account@demo-patient", "/app/account@demo-physician", "/app/admin@demo-system-admin", "/app/workspace@demo-content-manager", "/app/admin@demo-patient",
  "/app/physician/patients/pt-1@demo-physician",
];
const areaAccount = { patient: "demo-patient", physician: "demo-physician", pharmacist: "demo-pharmacist", pharmacy: "demo-pharmacy-admin", industry: "demo-industry", admin: "demo-system-admin", workspace: "demo-content-manager" };
const SESSION_KEY = "ms.auth.demo-session";
/** Path + the demo account to be signed in as (explicit "@account" or the owner of the /app/<area>). */
const split = (entry) => {
  const [path, explicit] = entry.split("@");
  const area = /^\/app\/(\w+)/.exec(path)?.[1];
  return { path, account: explicit !== undefined ? explicit : (area ? areaAccount[area] : "") };
};
const viewports = { desktop: { width: 1366, height: 860 }, tablet: { width: 820, height: 1100 }, phone: { width: 390, height: 844 } };
const combos = [
  { theme: "light", locale: "fa", vp: "desktop" }, { theme: "dark", locale: "en", vp: "desktop" },
  { theme: "light", locale: "fa", vp: "phone" }, { theme: "dark", locale: "fa", vp: "phone" }, { theme: "light", locale: "en", vp: "tablet" },
];

const browser = await chromium.launch({ executablePath, args: ["--no-sandbox"] });
let violationsTotal = 0;
const problems = [];
for (const c of combos) {
  const ctx = await browser.newContext({ viewport: viewports[c.vp], reducedMotion: "reduce", deviceScaleFactor: 1 });
  await ctx.addInitScript(([theme, locale]) => { localStorage.setItem("ms.theme", theme); localStorage.setItem("ms.locale", locale); }, [c.theme, c.locale]);
  const page = await ctx.newPage();
  const errors = [];
  page.on("pageerror", (e) => errors.push(String(e)));
  page.on("console", (m) => { if (m.type() === "error" && !m.text().includes("Failed to load resource")) errors.push(m.text()); });
  await page.goto(base + "/design-system", { waitUntil: "networkidle" }); // establish the origin so localStorage can be set
  for (const entry of pages) {
    const { path, account } = split(entry);
    await page.evaluate(([k, acct]) => { if (acct) localStorage.setItem(k, JSON.stringify({ accountId: acct, sessionId: "qa", expiresAt: Date.now() + 86_400_000 })); else localStorage.removeItem(k); }, [SESSION_KEY, account]);
    await page.goto(base + path, { waitUntil: "networkidle" });
    await page.waitForTimeout(350);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1);
    if (overflow) problems.push(`${c.vp}/${c.locale}/${c.theme} ${path}: horizontal overflow`);
    const dir = await page.evaluate(() => document.documentElement.dir);
    if (dir !== (c.locale === "fa" ? "rtl" : "ltr")) problems.push(`${path}: wrong dir ${dir}`);
    if (path.startsWith("/app") && account && (new URL(page.url()).pathname === "/login")) problems.push(`${path} as ${account}: unexpectedly redirected to /login`);
    await page.evaluate(axeSource);
    const res = await page.evaluate(() => globalThis.axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] } }));
    for (const v of res.violations) {
      violationsTotal += 1;
      problems.push(`${c.vp}/${c.locale}/${c.theme} ${path}: [${v.impact}] ${v.id} - ${v.help} (${v.nodes.length}) e.g. ${v.nodes[0]?.target?.join(" ")} :: ${(v.nodes[0]?.any[0]?.message ?? "").slice(0, 120)}`);
    }
    if (shots) await page.screenshot({ path: join(out, `${c.vp}-${c.locale}-${c.theme}${(path.replace(/\//g, "_") || "_root") + (account ? "@" + account : "")}.png`), fullPage: true });
  }
  if (errors.length) problems.push(`${c.vp}/${c.locale}/${c.theme}: console errors: ${[...new Set(errors)].slice(0, 5).join(" | ")}`);
  await ctx.close();
}
await browser.close();
console.log(problems.length ? problems.join("\n") : "QA: no problems found");
console.log(`\naxe violations: ${violationsTotal}; pages x combos: ${pages.length * combos.length}`);
process.exit(problems.length ? 1 : 0);
