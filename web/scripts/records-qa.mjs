#!/usr/bin/env node
// Phase 5 QA: signs in through the REAL login screen against the REAL API (web built with VITE_AUTH_MODE=api), opens every new page,
// checks content/direction/overflow, runs axe (WCAG 2.2 AA) and takes screenshots.   node scripts/records-qa.mjs [--base URL] [--out DIR]
import { mkdirSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join } from "node:path";
import { chromium } from "playwright-core";

const require = createRequire(import.meta.url);
const axeSource = readFileSync(require.resolve("axe-core/axe.min.js"), "utf8");
const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const base = arg("--base", "http://127.0.0.1:4174");
const out = arg("--out", "/tmp/p5-shots");
mkdirSync(out, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH ?? "/opt/pw-browsers/chromium-1194/chrome-linux/chrome";
const browser = await chromium.launch({ executablePath, args: ["--no-sandbox"] });
const fails = [];
const check = (ok, msg) => { if (!ok) fails.push(msg); console.log(ok ? "ok  " : "FAIL", msg); };
const vps = { desktop: { width: 1366, height: 860 }, phone: { width: 390, height: 844 } };

async function axe(page, label) {
  await page.evaluate(axeSource);
  const res = await page.evaluate(() => globalThis.axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] } }));
  for (const v of res.violations) check(false, `axe ${label}: [${v.impact}] ${v.id} - ${v.help} e.g. ${v.nodes[0]?.target?.join(" ")}`);
  if (!res.violations.length) check(true, `axe clean: ${label}`);
}

// [account button, area, [[path, text that must appear (en, fa)]]]
const plan = [
  [/DEMO Patient — Sara|دمو بیمار — سارا/, "patient", [
    ["records", /Basic profile|مشخصات پایه/], ["taking", /Current medicines|داروهای فعلی/], ["batches", /Batches and lots|بچ و لات/],
    ["myreports", /Reports to the manufacturer|گزارش به سازنده/], ["sharing", /Care relationships|روابط مراقبتی/], ["messages", /Messages for you|پیام‌ها برای شما/]]],
  [/DEMO Physician — Dr|دمو پزشک — دکتر/, "physician", [["reportreviews", /Report reviews|بازبینی گزارش‌ها/], ["care", /Care requests|درخواست‌های مراقبت/]]],
  [/DEMO System Admin|دمو مدیر سیستم/, "admin", [["queue", /Report queue|صف گزارش‌ها/]]],
];

for (const locale of ["en", "fa"]) for (const [vpName, vp] of Object.entries(vps)) for (const theme of ["light", "dark"]) {
  for (const [account, area, pages] of plan) {
    const ctx = await browser.newContext({ viewport: vp, reducedMotion: "reduce" });
    await ctx.addInitScript(([l, t]) => { localStorage.setItem("ms.locale", l); localStorage.setItem("ms.theme", t); }, [locale, theme]);
    const page = await ctx.newPage();
    const errors = [];
    page.on("pageerror", (e) => errors.push(String(e)));
    page.on("console", (m) => { if (m.type() === "error" && !m.text().includes("Failed to load resource")) errors.push(m.text()); });
    await page.goto(base + "/login", { waitUntil: "networkidle" });
    await page.getByRole("button", { name: account }).first().click();
    await page.waitForURL(`**/app/${area}**`);
    for (const [path, text] of pages) {
      const tag = `${locale}/${theme}/${vpName}/${area}/${path}`;
      await page.goto(`${base}/app/${area}/${path}`, { waitUntil: "networkidle" });
      await page.waitForTimeout(400);
      check(await page.getByText(text).count() > 0, `${tag}: content visible`);
      check(await page.getByText(/Not connected|اتصال به سرور برقرار نیست|Could not load|بارگذاری نشد/).count() === 0, `${tag}: no error state`);
      check((await page.evaluate(() => document.documentElement.dir)) === (locale === "fa" ? "rtl" : "ltr"), `${tag}: direction`);
      check(!(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1)), `${tag}: no horizontal overflow`);
      await axe(page, tag);
      if (theme === "light") await page.screenshot({ path: join(out, `${area}-${path}-${vpName}-${locale}.png`), fullPage: true });
    }
    if (errors.length) check(false, `${locale}/${theme}/${vpName}/${area}: console errors: ${[...new Set(errors)].slice(0, 3).join(" | ")}`);
    await ctx.close();
  }
}
await browser.close();
console.log(`\nfailures: ${fails.length}`);
process.exit(fails.length ? 1 : 0);
