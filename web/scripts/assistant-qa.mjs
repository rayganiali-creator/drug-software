#!/usr/bin/env node
// Phase 6 QA: real login, REAL API (Mock AI provider), the source-based assistant page: ask, read the structured answer, check labels, RTL, overflow, axe, screenshots.
//   node scripts/assistant-qa.mjs [--base http://localhost:4174] [--out /tmp/p6-shots]
import { mkdirSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join } from "node:path";
import { chromium } from "playwright-core";

const require = createRequire(import.meta.url);
const axeSource = readFileSync(require.resolve("axe-core/axe.min.js"), "utf8");
const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const base = arg("--base", "http://localhost:4174");
const out = arg("--out", "/tmp/p6-shots");
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

const cases = {
  en: { q: "Tell me about Nocturin", status: "Answer", mock: "MOCK: no language model was used", ask: /^Ask$/, refuse: "Should I stop taking Nocturin?", refused: "I can't help with that here", emergency: "I can't breathe", escalated: "This may need help right away", none: "xyzzyqq nothing like this", noneText: "Nothing found in the sources", label: /Your question/ },
  fa: { q: "درباره نوکتورین بگو", status: "پاسخ", mock: "آزمایشی (MOCK): مدل زبانی استفاده نشد", ask: /^پرسیدن$/, refuse: "آیا باید دارو را قطع کنم؟", refused: "در این‌باره نمی‌توانم کمک کنم", emergency: "نمی‌توانم نفس بکشم", escalated: "ممکن است کمک فوری لازم باشد", none: "xyzzyqq nothing like this", noneText: "در منابع چیزی پیدا نشد", label: /پرسش شما/ },
};

for (const locale of ["en", "fa"]) for (const theme of ["light", "dark"]) for (const [vpName, vp] of Object.entries(vps)) {
  const c = cases[locale];
  const ctx = await browser.newContext({ viewport: vp, reducedMotion: "reduce" });
  await ctx.addInitScript(([l, t]) => { localStorage.setItem("ms.locale", l); localStorage.setItem("ms.theme", t); }, [locale, theme]);
  const page = await ctx.newPage();
  const errors = [];
  page.on("pageerror", (e) => errors.push(String(e)));
  page.on("console", (m) => { if (m.type() === "error" && !m.text().includes("Failed to load resource")) errors.push(m.text()); });
  const tag = `${locale}/${theme}/${vpName}`;
  await page.goto(base + "/login", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: /DEMO Patient — Sara|دمو بیمار — سارا/ }).first().click();
  await page.waitForURL("**/app/patient**");
  await page.goto(base + "/app/patient/assistant", { waitUntil: "networkidle" });
  const box = page.getByLabel(c.label);
  await box.waitFor();
  check((await page.evaluate(() => document.documentElement.dir)) === (locale === "fa" ? "rtl" : "ltr"), `${tag}: direction`);
  check(!(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1)), `${tag}: no overflow (empty)`);
  await axe(page, `${tag} empty`);

  const ask = async (text) => { await box.fill(text); await Promise.all([page.waitForResponse((r) => r.url().includes("/ai/medication-assistant"), { timeout: 15000 }), page.getByRole("button", { name: c.ask }).click()]); await page.waitForTimeout(400); };

  await ask(c.q);
  check(await page.getByText(c.status, { exact: true }).count() > 0, `${tag}: answered`);
  check(await page.getByText(c.mock, { exact: true }).count() > 0, `${tag}: MOCK label`);
  check(await page.locator(".gr-evidence li").count() > 0, `${tag}: evidence list from the API`);
  check(await page.getByText("DEMO", { exact: false }).count() > 0, `${tag}: demo label`);
  check(!/confidence|\d\s?%/i.test(await page.locator("main").innerText()), `${tag}: no numeric confidence`);
  check(!(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1)), `${tag}: no overflow (answer)`);
  await axe(page, `${tag} answer`);
  if (theme === "light") await page.screenshot({ path: join(out, `answer-${vpName}-${locale}.png`), fullPage: true });

  await ask(c.refuse);
  check(await page.getByText(c.refused, { exact: true }).count() > 0, `${tag}: refusal`);
  await ask(c.emergency);
  check(await page.getByText(c.escalated, { exact: true }).count() > 0, `${tag}: escalation`);
  check(await page.getByRole("alert").count() > 0, `${tag}: escalation is an alert`);
  await axe(page, `${tag} escalation`);
  if (theme === "light" && vpName === "desktop") await page.screenshot({ path: join(out, `escalation-${locale}.png`), fullPage: true });
  await ask(c.none);
  check(await page.getByText(c.noneText, { exact: true }).count() > 0, `${tag}: nothing found`);
  if (errors.length) check(false, `${tag}: console errors: ${[...new Set(errors)].slice(0, 3).join(" | ")}`);
  await ctx.close();
}
await browser.close();
console.log(`\nfailures: ${fails.length}`);
process.exit(fails.length ? 1 : 0);
