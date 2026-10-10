#!/usr/bin/env node
// Phase 7 QA: real login, REAL API (in-memory or PostgreSQL; demonstration rules on), the medication safety check: run, read the structured result, labels, RTL,
// overflow, axe (WCAG 2.2 AA), screenshots. Also the physician's patient page and the rules page.
//   node scripts/safety-qa.mjs [--base http://localhost:4174] [--out /tmp/p7-shots]
import { mkdirSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join } from "node:path";
import { chromium } from "playwright-core";

const require = createRequire(import.meta.url);
const axeSource = readFileSync(require.resolve("axe-core/axe.min.js"), "utf8");
const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const base = arg("--base", "http://localhost:4174");
const out = arg("--out", "/tmp/p7-shots");
mkdirSync(out, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH ?? "/opt/pw-browsers/chromium-1194/chrome-linux/chrome";
const browser = await chromium.launch({ executablePath, args: ["--no-sandbox"] });
const fails = [];
const check = (ok, msg) => { if (!ok) fails.push(msg); console.log(ok ? "ok  " : "FAIL", msg); };
const vps = { desktop: { width: 1366, height: 860 }, phone: { width: 390, height: 844 } };
const SARA = "fa1f8b92-53cc-5ad2-a0a8-c39713c37946";

async function axe(page, label) {
  await page.evaluate(axeSource);
  const res = await page.evaluate(() => globalThis.axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] } }));
  for (const v of res.violations) check(false, `axe ${label}: [${v.impact}] ${v.id} - ${v.help} e.g. ${v.nodes[0]?.target?.join(" ")}`);
  if (!res.violations.length) check(true, `axe clean: ${label}`);
}
const noOverflow = (page) => page.evaluate(() => !(document.documentElement.scrollWidth > document.documentElement.clientWidth + 1));

const T = {
  en: { run: /^Run the check$/, again: /^Run the check again$/, none: "No check has been run yet", status: "No approved rules are in force", demo: "Demonstration findings", notSafe: "A result without findings is not a statement that anything is safe.", unrev: null,
    patient: /DEMO Patient — Sara/, doctor: /DEMO Physician — Dr/, rules: "Safety rules", coverage: "Not checked at all" },
  fa: { run: /^اجرای بررسی$/, again: /^اجرای دوبارهٔ بررسی$/, none: "هنوز بررسی‌ای انجام نشده است", status: "هیچ قاعدهٔ تأییدشده‌ای فعال نیست", demo: "یافته‌های نمایشی", notSafe: "نتیجهٔ بدون یافته به این معنی نیست که چیزی بی‌خطر است.", unrev: "متن فارسی هنوز بازبینی نشده است",
    patient: /دمو بیمار — سارا/, doctor: /دمو پزشک — دکتر/, rules: "قواعد ایمنی", coverage: "اصلاً بررسی نمی‌شود" },
};

for (const locale of ["en", "fa"]) for (const theme of ["light", "dark"]) for (const [vpName, vp] of Object.entries(vps)) {
  const c = T[locale];
  const tag = `${locale}/${theme}/${vpName}`;
  const ctx = await browser.newContext({ viewport: vp, reducedMotion: "reduce" });
  await ctx.addInitScript(([l, t]) => { localStorage.setItem("ms.locale", l); localStorage.setItem("ms.theme", t); }, [locale, theme]);
  const page = await ctx.newPage();
  const errors = [];
  page.on("pageerror", (e) => errors.push(String(e)));
  page.on("console", (m) => { if (m.type() === "error" && !m.text().includes("Failed to load resource")) errors.push(m.text()); });

  // ---- the patient
  await page.goto(base + "/login", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: c.patient }).first().click();
  await page.waitForURL("**/app/patient**");
  const runs = [];
  page.on("request", (r) => { if (r.method() === "POST" && r.url().includes("/safety/assessments")) runs.push(r.url()); });
  await page.goto(base + "/app/patient/safety", { waitUntil: "networkidle" });
  check((await page.evaluate(() => document.documentElement.dir)) === (locale === "fa" ? "rtl" : "ltr"), `${tag}: direction`);
  check(runs.length === 0, `${tag}: opening the page runs nothing`);
  check(await page.getByText(c.none, { exact: true }).count() > 0 || await page.locator(".sf").count() > 0, `${tag}: empty or stored state`);
  check(await noOverflow(page), `${tag}: no overflow (before run)`);
  await axe(page, `${tag} before run`);

  await Promise.all([page.waitForResponse((r) => r.url().includes("/safety/assessments") && r.request().method() === "POST", { timeout: 20000 }), page.getByRole("button", { name: /^(Run the check|Run the check again|اجرای بررسی|اجرای دوبارهٔ بررسی)$/ }).first().click()]);
  await page.locator(".sf").waitFor();
  await page.waitForTimeout(300);
  check(runs.length === 1, `${tag}: exactly one run after one click`);
  check(await page.getByText(c.status, { exact: true }).count() > 0, `${tag}: no approved rules, said plainly`);
  check(await page.getByText(c.demo, { exact: true }).count() > 0, `${tag}: demonstration findings are labelled`);
  check(await page.getByText(c.notSafe, { exact: true }).count() > 0, `${tag}: never a safety claim`);
  check(await page.getByText(c.coverage, { exact: true }).count() > 0, `${tag}: unsupported areas listed`);
  if (c.unrev) check(await page.getByText(c.unrev, { exact: true }).count() > 0, `${tag}: Persian wording flagged as not yet reviewed`);
  check(!/\bsf\.[a-z]/i.test(await page.locator("main").innerText()), `${tag}: no untranslated keys`);
  check(!/you are safe|no risk|\d\s?%/i.test(await page.locator("main").innerText()), `${tag}: no reassurance or percentage`);
  check(await noOverflow(page), `${tag}: no overflow (result)`);
  await axe(page, `${tag} result`);
  if (theme === "light") await page.screenshot({ path: join(out, `result-${vpName}-${locale}.png`), fullPage: true });
  await page.locator("details > summary").first().click().catch(() => undefined);
  await axe(page, `${tag} result with details open`);

  // ---- the physician
  await page.evaluate(() => { localStorage.removeItem("ms.session"); });
  const ctx2 = await browser.newContext({ viewport: vp, reducedMotion: "reduce" });
  await ctx2.addInitScript(([l, t]) => { localStorage.setItem("ms.locale", l); localStorage.setItem("ms.theme", t); }, [locale, theme]);
  const p2 = await ctx2.newPage();
  p2.on("pageerror", (e) => errors.push(String(e)));
  await p2.goto(base + "/login", { waitUntil: "networkidle" });
  await p2.getByRole("button", { name: c.doctor }).first().click();
  await p2.waitForURL("**/app/physician**");
  await p2.goto(base + "/app/physician/rules", { waitUntil: "networkidle" });
  await p2.getByRole("heading", { name: c.rules }).first().waitFor();
  check(await p2.locator(".sf-rules li").count() >= 5, `${tag}: rules page lists the rule versions`);
  check(await noOverflow(p2), `${tag}: rules page no overflow`);
  await axe(p2, `${tag} rules page`);
  if (theme === "light" && vpName === "desktop") await p2.screenshot({ path: join(out, `rules-${locale}.png`), fullPage: true });
  await p2.goto(base + `/app/physician/patients/${SARA}/safety`, { waitUntil: "networkidle" });
  await p2.locator(".sf-page").waitFor();
  check(await noOverflow(p2), `${tag}: physician patient page no overflow`);
  await axe(p2, `${tag} physician page`);
  if (errors.length) check(false, `${tag}: console errors: ${[...new Set(errors)].slice(0, 3).join(" | ")}`);
  await ctx2.close();
  await ctx.close();
}
await browser.close();
console.log(`\nfailures: ${fails.length}`);
process.exit(fails.length ? 1 : 0);
