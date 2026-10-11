#!/usr/bin/env node
// Phase 9 QA against the REAL local API (PostgreSQL, synthetic demonstration accounts only): a patient report is created and reviewed through the real endpoints,
// then the adverse-event pages are checked in the browser (en/fa x light/dark x desktop/phone, axe, overflow, no raw keys, no external-submission control).
//   node scripts/phase9-qa.mjs [--base http://127.0.0.1:4174] [--api http://127.0.0.1:5080] [--out /tmp/p9-shots]
import { mkdirSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join } from "node:path";
import { chromium } from "playwright-core";

const require = createRequire(import.meta.url);
const axeSource = readFileSync(require.resolve("axe-core/axe.min.js"), "utf8");
const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const base = arg("--base", "http://127.0.0.1:4174");
const api = arg("--api", "http://127.0.0.1:5080");
const out = arg("--out", "/tmp/p9-shots");
mkdirSync(out, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH ?? "/opt/pw-browsers/chromium-1194/chrome-linux/chrome";
const check = (ok, msg) => { if (!ok) fails.push(msg); console.log(ok ? "ok  " : "FAIL", msg); };
const fails = [];

async function login(account) {
  const r = await fetch(`${api}/auth/login`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ credentials: { accountId: account }, device: { platform: "web", deviceName: "p9 qa" } }) });
  const j = await r.json();
  return { token: j.tokens.accessToken, id: j.user.id };
}
const call = async (s, method, path, body) => {
  const r = await fetch(api + path, { method, headers: { Authorization: `Bearer ${s.token}`, "Content-Type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body) });
  return { status: r.status, json: await r.json().catch(() => null) };
};
const sara = await login("demo-patient");
const doctor = await login("demo-physician");
const outsider = await login("demo-physician-b");

const created = await call(sara, "POST", `/patients/${sara.id}/adverse-events`, { eventDescription: "P9-QA: itchy rash on both arms after the evening dose", onsetAt: null, resolvedAt: null, suspectedMedications: [{ patientMedicationId: null, nameAsReported: "a white tablet" }], outcomeAsStated: null, additionalDetails: null, clientRequestId: `p9-qa-${Date.now()}` });
check(created.status === 201 || created.status === 200, "setup: the patient's report is saved");
const rid = created.json.event.id;
check(created.json.event.externalStatus === "NotSubmitted" && created.json.event.status === "Submitted", "a new record is internal, unsubmitted and in 'Submitted'");
check((await call(doctor, "GET", `/adverse-events/${rid}`)).json.event.category === "Unclassified", "the professional view starts Unclassified");
check((await call(outsider, "GET", `/adverse-events/${rid}`)).status === 403, "a professional without relationship and consent is refused");
check((await call(doctor, "POST", `/adverse-events/${rid}/entries`, { kind: "ReviewNote", text: "P9-QA internal note" })).status === 201, "setup: a reviewer note is added");
check((await call(doctor, "POST", `/adverse-events/${rid}/entries`, { kind: "InformationRequest", text: "P9-QA: when did the rash start?" })).status === 201, "setup: the reviewer asks for information");

const browser = await chromium.launch({ executablePath, args: ["--no-sandbox"] });
const noOverflow = (page) => page.evaluate(() => !(document.documentElement.scrollWidth > document.documentElement.clientWidth + 1));
async function axe(page, label) {
  await page.evaluate(axeSource);
  const res = await page.evaluate(() => globalThis.axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] } }));
  for (const v of res.violations) check(false, `axe ${label}: [${v.impact}] ${v.id} - ${v.help} e.g. ${v.nodes[0]?.target?.join(" ")}`);
  if (!res.violations.length) check(true, `axe clean: ${label}`);
}
const vps = { desktop: { width: 1366, height: 860 }, phone: { width: 390, height: 844 } };
const T = {
  en: { patient: /DEMO Patient — Sara/, doctor: /DEMO Physician — Dr/, signal: "This record is a signal for human review. It is not proof that a medicine caused anything.", orig: "As originally reported (never edited)", asked: "P9-QA: when did the rash start?", note: "P9-QA internal note", cls: "Classification", unrev: "Persian wording not yet reviewed" },
  fa: { patient: /دمو بیمار — سارا/, doctor: /دمو پزشک — دکتر/, signal: "این پرونده نشانه‌ای برای بازبینی انسانی است. اثبات نمی‌کند که دارویی عاملی بوده است.", orig: "همان‌طور که در آغاز گزارش شد (هرگز ویرایش نمی‌شود)", asked: "P9-QA: when did the rash start?", note: "P9-QA internal note", cls: "طبقه‌بندی", unrev: "متن فارسی هنوز بازبینی نشده است" },
};
for (const locale of ["en", "fa"]) for (const theme of ["light", "dark"]) for (const [vpName, vp] of Object.entries(vps)) {
  const c = T[locale];
  const tag = `${locale}/${theme}/${vpName}`;
  const mk = async (account, urlGlob) => {
    const ctx = await browser.newContext({ viewport: vp, reducedMotion: "reduce" });
    await ctx.addInitScript(([l, t]) => { localStorage.setItem("ms.locale", l); localStorage.setItem("ms.theme", t); }, [locale, theme]);
    const page = await ctx.newPage();
    await page.goto(base + "/login", { waitUntil: "networkidle" });
    await page.getByRole("button", { name: account }).first().click();
    await page.waitForURL(urlGlob);
    return page;
  };
  const visit = async (page, path, label, waitText) => {
    await page.goto(base + path, { waitUntil: "networkidle" });
    if (waitText) await page.getByText(waitText, { exact: false }).first().waitFor({ timeout: 8000 }).catch(() => undefined);
    await page.waitForTimeout(300);
    const okW = await noOverflow(page);
    if (!okW) console.log("OVERFLOW", await page.evaluate(() => [...document.querySelectorAll("body *")].filter((e) => e.getBoundingClientRect().right > document.documentElement.clientWidth + 1 || e.getBoundingClientRect().left < -1).slice(0, 6).map((e) => e.tagName + "." + e.className + " " + Math.round(e.getBoundingClientRect().left) + ".." + Math.round(e.getBoundingClientRect().right))));
    check(okW, `${tag} ${label}: no overflow`);
    check(!/\badr\.[a-z]/i.test(await page.locator("main").innerText()), `${tag} ${label}: no untranslated keys`);
    await axe(page, `${tag} ${label}`);
  };

  const p = await mk(c.patient, "**/app/patient**");
  await visit(p, "/app/patient/adverse-events", "patient list", c.signal);
  check(await p.getByText(c.signal, { exact: true }).count() > 0, `${tag} patient list: signal notice shown`);
  check(await p.getByText("P9-QA: itchy rash").count() > 0, `${tag} patient list: own report listed`);
  await visit(p, `/app/patient/adverse-events/${rid}`, "patient detail", c.orig);
  const ptxt = await p.locator("main").innerText();
  check(ptxt.includes("P9-QA: when did the rash start?"), `${tag} patient detail: the question to the patient is visible`);
  check(!ptxt.includes("P9-QA internal note"), `${tag} patient detail: professional-only notes are not shown to the patient`);
  check(await p.getByText(c.cls, { exact: true }).count() === 0, `${tag} patient detail: no professional classification panel`);
  if (theme === "light") await p.screenshot({ path: join(out, `patient-detail-${vpName}-${locale}.png`), fullPage: true });

  const d = await mk(c.doctor, "**/app/physician**");
  await visit(d, "/app/physician/adverse-events", "queue", "P9-QA");
  check(await d.getByText("P9-QA: itchy rash").count() > 0, `${tag} queue: the consented patient's record is listed`);
  await visit(d, `/app/physician/adverse-events/${rid}`, "pro detail", c.orig);
  const dtxt = await d.locator("main").innerText();
  check(dtxt.includes("P9-QA internal note"), `${tag} pro detail: the reviewer note is shown to the professional`);
  check(await d.getByRole("button", { name: /send|submit to|report to/i }).count() === 0, `${tag} pro detail: no control that sends a record outside`);
  if (locale === "en") {
    await d.getByRole("button", { name: "Show a template summary" }).click();
    await d.getByText("Template summary of the record — not a conclusion").first().waitFor({ timeout: 8000 });
    check(await d.getByText(/No language model was used/).count() > 0, `${tag} pro detail: template summary says no language model`);
    await axe(d, `${tag} pro detail with summary`);
  }
  if (theme === "light") await d.screenshot({ path: join(out, `pro-detail-${vpName}-${locale}.png`), fullPage: true });
  if (locale === "fa") check(await d.getByText(c.unrev).count() > 0, `${tag} pro detail: Persian wording banner shown`);
}
await browser.close();
console.log(`\nfailures: ${fails.length}`);
process.exit(fails.length ? 1 : 0);
