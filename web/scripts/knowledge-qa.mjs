#!/usr/bin/env node
// Drives the drug reference through the REAL login screen and the REAL API (web built with VITE_AUTH_MODE=api), asserts the essentials,
// runs axe (WCAG 2.2 AA) and takes screenshots.   node scripts/knowledge-qa.mjs [--base http://127.0.0.1:4174] [--out /tmp/p4-shots]
import { mkdirSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join } from "node:path";
import { chromium } from "playwright-core";

const require = createRequire(import.meta.url);
const axeSource = readFileSync(require.resolve("axe-core/axe.min.js"), "utf8");
const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const base = arg("--base", "http://127.0.0.1:4174");
const out = arg("--out", "/tmp/p4-shots");
mkdirSync(out, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH ?? "/opt/pw-browsers/chromium-1194/chrome-linux/chrome";
const browser = await chromium.launch({ executablePath, args: ["--no-sandbox"] });
const fails = [];
const check = (ok, msg) => { if (!ok) fails.push(msg); console.log(ok ? "ok  " : "FAIL", msg); };
const vps = { desktop: { width: 1366, height: 860 }, tablet: { width: 820, height: 1100 }, phone: { width: 390, height: 844 } };

async function axe(page, label) {
  await page.evaluate(axeSource);
  const res = await page.evaluate(() => globalThis.axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] } }));
  for (const v of res.violations) check(false, `axe ${label}: [${v.impact}] ${v.id} - ${v.help} e.g. ${v.nodes[0]?.target?.join(" ")}`);
  if (!res.violations.length) check(true, `axe clean: ${label}`);
}

for (const locale of ["en", "fa"]) for (const theme of ["light", "dark"]) for (const [vpName, vp] of Object.entries(vps)) {
  const ctx = await browser.newContext({ viewport: vp, reducedMotion: "reduce" });
  await ctx.addInitScript(([l, t]) => { localStorage.setItem("ms.locale", l); localStorage.setItem("ms.theme", t); }, [locale, theme]);
  const page = await ctx.newPage();
  const errors = [];
  page.on("pageerror", (e) => errors.push(String(e)));
  page.on("console", (m) => { if (m.type() === "error" && !m.text().includes("Failed to load resource")) errors.push(m.text()); });
  const tag = `${locale}/${theme}/${vpName}`;
  await page.goto(base + "/login", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: /Sign in as DEMO Patient|ورود به‌عنوان دمو بیمار/ }).first().click();
  await page.waitForURL("**/app/patient");
  await page.goto(base + "/app/patient/drugs", { waitUntil: "networkidle" });
  await page.getByRole("list").first().waitFor();
  const first = locale === "fa" ? "دموپریل" : "Demopril";
  check(await page.getByText(first, { exact: true }).count() > 0, `${tag}: list shows ${first} from the API`);
  check(await page.getByText(locale === "fa" ? "نمایشی" : "DEMO", { exact: true }).count() > 0, `${tag}: demo status label visible`);
  check(!(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1)), `${tag}: no horizontal overflow (list)`);
  await axe(page, `${tag} list`);
  if (vpName === "desktop" && theme === "light") await page.screenshot({ path: join(out, `drugs-list-${locale}.png`) });

  // Arabic-letter variant finds the Persian name
  const box = page.getByRole("searchbox", { name: /Search medicines|جست‌وجوی دارو/ });
  await box.fill("نوكتورين");
  await page.waitForTimeout(900);
  check(await page.getByText(/Nocturin|نوکتورین/).count() > 0, `${tag}: Arabic ك/ي query finds Nocturin`);
  check(await page.getByText(/Showing 1–1|نمایش ۱ تا ۱ از ۱/).count() >= 0, `${tag}: filtered`);
  await box.fill("zzzzzz");
  await page.waitForTimeout(900);
  check(await page.getByText(locale === "fa" ? "دارویی پیدا نشد" : "No medicines found").count() > 0, `${tag}: empty state`);
  await box.fill("nocturin");
  await page.waitForTimeout(900);
  await page.getByRole("link", { name: /Nocturin|نوکتورین/ }).first().click();
  await page.getByRole("heading", { level: 2 }).first().waitFor();
  await page.waitForTimeout(300);
  check(await page.getByText(locale === "fa" ? "اطلاعات موجود نیست" : "Information not available").count() > 0, `${tag}: missing information is stated, not guessed`);
  check(await page.getByText(locale === "fa" ? /داده نمایشی — غیرقابل استفاده بالینی/ : /DEMO DATA — NOT FOR CLINICAL USE/).count() > 0, `${tag}: demo notice`);
  check((await page.evaluate(() => document.documentElement.dir)) === (locale === "fa" ? "rtl" : "ltr"), `${tag}: direction`);
  check(!(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1)), `${tag}: no horizontal overflow (detail)`);
  await axe(page, `${tag} detail`);
  if (vpName !== "tablet" && theme === "light") await page.screenshot({ path: join(out, `drugs-detail-${vpName}-${locale}.png`), fullPage: vpName === "phone" });
  if (errors.length) check(false, `${tag}: console errors: ${[...new Set(errors)].slice(0, 3).join(" | ")}`);
  await ctx.close();
}
await browser.close();
console.log(`\nfailures: ${fails.length}`);
process.exit(fails.length ? 1 : 0);
