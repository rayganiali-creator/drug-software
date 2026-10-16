#!/usr/bin/env node
// Phase 10 QA against the REAL local API (PostgreSQL, synthetic demonstration accounts only): pharmacy safety records are created and reviewed through the real endpoints, then the
// pages are checked in the browser for a pharmacist and a pharmacy administrator (en/fa x light/dark x desktop/phone, axe, overflow, no raw keys, no external-send control).
//   node scripts/phase10-qa.mjs [--base http://127.0.0.1:4174] [--api http://127.0.0.1:5080] [--out /tmp/p10-shots]
import { mkdirSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join } from "node:path";
import { chromium } from "playwright-core";

const require = createRequire(import.meta.url);
const axeSource = readFileSync(require.resolve("axe-core/axe.min.js"), "utf8");
const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const base = arg("--base", "http://127.0.0.1:4174");
const api = arg("--api", "http://127.0.0.1:5080");
const out = arg("--out", "/tmp/p10-shots");
mkdirSync(out, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH ?? "/opt/pw-browsers/chromium-1194/chrome-linux/chrome";
const fails = [];
const check = (ok, msg) => { if (!ok) fails.push(msg); console.log(ok ? "ok  " : "FAIL", msg); };

async function login(account) {
  const r = await fetch(`${api}/auth/login`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ credentials: { accountId: account }, device: { platform: "web", deviceName: "p10 qa" } }) });
  const j = await r.json();
  return { token: j.tokens.accessToken, id: j.user.id, orgs: j.user.organizationIds };
}
const call = async (s, method, path, body) => {
  const r = await fetch(api + path, { method, headers: { Authorization: `Bearer ${s.token}`, "Content-Type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body) });
  return { status: r.status, json: await r.json().catch(() => null) };
};
const pharmacist = await login("demo-pharmacist");
const admin = await login("demo-pharmacy-admin");
const doctor = await login("demo-physician");
const org = pharmacist.orgs[0];
const stamp = Date.now();

const ev = await call(pharmacist, "POST", `/organizations/${org}/safety-events`, { kind: "NearMiss", reachedPatient: "No", errorExamples: ["IncorrectQuantity"], description: "P10-QA: quantity noticed at the final check", occurredOn: null, medicineAsReported: null, howDetectedOrIntercepted: null, immediateActionTaken: null, clientRequestId: `p10-ev-${stamp}` });
check(ev.status === 201, "setup: a near miss is saved");
const evId = ev.json.event.id;
check(ev.json.event.externalStatus === "NotSubmitted" && ev.json.event.reachedPatient === "No", "a near miss did not reach the patient and is not submitted anywhere");
check((await call(admin, "POST", `/safety-events/${evId}/entries`, { kind: "ReviewNote", text: "P10-QA review note" })).status === 200, "setup: the pharmacy administrator adds a review note");
const cc = await call(pharmacist, "POST", `/organizations/${org}/product-concerns`, { type: "AppearanceOrPackaging", description: "P10-QA: blister packs look different", observedOn: null, productRecord: null, asReported: { productName: "Demopril", batchNumber: "P10-LOT", expiryDate: null, manufacturerName: null, gtin: null }, clientRequestId: `p10-pc-${stamp}` });
check(cc.status === 201 && cc.json.concern.linkState === "Unmatched", "setup: an unmatched product concern is saved (nothing was guessed)");
const ccId = cc.json.concern.id;
check((await call(doctor, "GET", `/product-concerns/${ccId}`)).status === 403, "a physician without pharmacy permissions is refused");
check((await call(doctor, "GET", `/safety-events/${evId}`)).status === 403, "a physician cannot read the pharmacy safety log");

const browser = await chromium.launch({ executablePath, args: ["--no-sandbox"] });
const noOverflow = (page) => page.evaluate(() => !(document.documentElement.scrollWidth > document.documentElement.clientWidth + 1 || [...document.querySelectorAll("body *")].some((e) => e.getBoundingClientRect().left < -1)));
async function axe(page, label) {
  await page.evaluate(axeSource);
  const res = await page.evaluate(() => globalThis.axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] } }));
  for (const v of res.violations) check(false, `axe ${label}: [${v.impact}] ${v.id} - ${v.help} e.g. ${v.nodes[0]?.target?.join(" ")}`);
  if (!res.violations.length) check(true, `axe clean: ${label}`);
}
const vps = { desktop: { width: 1366, height: 860 }, phone: { width: 390, height: 844 } };
const T = {
  en: { pharmacist: /DEMO Pharmacist$/, admin: /DEMO Pharmacy Admin/, notice: "This is an internal pharmacy record for review and learning. It is not a regulatory report and it is not a nationally mandated classification. Do not enter patient names or other identifying details.", orig: "As originally recorded (never edited)", unrev: "Persian wording not yet reviewed" },
  fa: { pharmacist: /دمو داروساز$/, admin: /دمو مدیر داروخانه/, notice: "این یک پروندهٔ داخلی داروخانه برای بازبینی و یادگیری است. گزارش رسمی نیست و طبقه‌بندی الزامی ملی نیست. نام بیمار یا مشخصات هویتی دیگر را وارد نکنید.", orig: "همان‌طور که در آغاز ثبت شد (هرگز ویرایش نمی‌شود)", unrev: "متن فارسی هنوز بازبینی نشده است" },
};
for (const locale of ["en", "fa"]) for (const theme of ["light", "dark"]) for (const [vpName, vp] of Object.entries(vps)) for (const who of ["pharmacist", "pharmacy"]) {
  const c = T[locale];
  const tag = `${locale}/${theme}/${vpName}/${who}`;
  const ctx = await browser.newContext({ viewport: vp, reducedMotion: "reduce" });
  await ctx.addInitScript(([l, t]) => { localStorage.setItem("ms.locale", l); localStorage.setItem("ms.theme", t); }, [locale, theme]);
  const page = await ctx.newPage();
  await page.goto(base + "/login", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: who === "pharmacist" ? c.pharmacist : c.admin }).first().click();
  await page.waitForURL(`**/app/${who}**`);
  const visit = async (path, label, waitText) => {
    await page.goto(base + path, { waitUntil: "networkidle" });
    if (waitText) await page.getByText(waitText, { exact: false }).first().waitFor({ timeout: 8000 }).catch(() => undefined);
    await page.waitForTimeout(300);
    check(await noOverflow(page), `${tag} ${label}: no overflow`);
    check(!/\bph\.[a-z]|\badr\.[a-z]/i.test(await page.locator("main").innerText()), `${tag} ${label}: no untranslated keys`);
    await axe(page, `${tag} ${label}`);
  };
  const root = `/app/${who}`;
  await visit(`${root}/safety-log`, "safety log", c.notice);
  check(await page.getByText("P10-QA: quantity noticed").count() > 0, `${tag} safety log: the near miss is listed`);
  check(await page.getByRole("button", { name: /send|submit to|report to/i }).count() === 0, `${tag} safety log: no control that sends a record outside`);
  if (theme === "light") await page.screenshot({ path: join(out, `log-${who}-${vpName}-${locale}.png`), fullPage: true });
  await visit(`${root}/safety-log/${evId}`, "safety event", c.orig);
  check((await page.locator("main").innerText()).includes("P10-QA review note"), `${tag} safety event: the review note is in the history`);
  await visit(`${root}/product-concerns`, "product concerns", "P10-QA");
  await visit(`${root}/product-concerns/${ccId}`, "product concern", c.orig === "" ? "" : undefined);
  check((await page.locator("main").innerText()).includes("P10-LOT"), `${tag} product concern: the typed batch is shown as typed`);
  if (locale === "fa") check(await page.getByText(c.unrev).count() > 0, `${tag} product concern: Persian wording banner shown`);
  if (theme === "light") await page.screenshot({ path: join(out, `concern-${who}-${vpName}-${locale}.png`), fullPage: true });
  await ctx.close();
}
await browser.close();
console.log(`\nfailures: ${fails.length}`);
process.exit(fails.length ? 1 : 0);
