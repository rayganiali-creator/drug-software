#!/usr/bin/env node
// Phase 8 QA against the REAL API (local PostgreSQL, scheduled workers on, demonstration data): sets up one synthetic approved TEST rule through the real two-person
// API flow, lets the local workers run, then checks adherence, scheduled checks, delivery labels and the rule workspace in the browser (en/fa x light/dark x desktop/phone, axe).
//   node scripts/phase8-qa.mjs [--base http://127.0.0.1:4174] [--api http://127.0.0.1:5080] [--out /tmp/p8-shots]
import { mkdirSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join } from "node:path";
import { chromium } from "playwright-core";

const require = createRequire(import.meta.url);
const axeSource = readFileSync(require.resolve("axe-core/axe.min.js"), "utf8");
const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const base = arg("--base", "http://127.0.0.1:4174");
const api = arg("--api", "http://127.0.0.1:5080");
const out = arg("--out", "/tmp/p8-shots");
mkdirSync(out, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH ?? "/opt/pw-browsers/chromium-1194/chrome-linux/chrome";
const fails = [];
const check = (ok, msg) => { if (!ok) fails.push(msg); console.log(ok ? "ok  " : "FAIL", msg); };

// ---------- API setup (the same endpoints the UI uses; no shortcuts) ----------
async function login(account) {
  const r = await fetch(`${api}/auth/login`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ credentials: { accountId: account }, device: { platform: "web", deviceName: "p8 qa" } }) });
  const j = await r.json();
  return { token: j.tokens.accessToken, id: j.user.id };
}
const call = async (s, method, path, body) => {
  const r = await fetch(api + path, { method, headers: { Authorization: `Bearer ${s.token}`, "Content-Type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body) });
  return { status: r.status, json: await r.json().catch(() => null) };
};
const sara = await login("demo-patient");
const author = await login("demo-content-manager");
const doctor = await login("demo-physician");

const defs = await call(doctor, "GET", "/clinical-rules");
const demo = (await call(doctor, "GET", "/clinical-rules/DEMO-DQ-001/versions/1")).json.record.definition;
const rule = { ...demo, ruleId: "P8-QA-DQ", evidence: [{ sourceId: "test-fixture", sourceName: "TEST FIXTURE (not a real source)", version: "1", publicationDate: null, validation: "Validated", reviewStatus: "fixture", locator: null }] };
if (!defs.json.some((r) => r.ruleId === "P8-QA-DQ")) {
  check((await call(author, "POST", "/clinical-rules", rule)).status === 201, "setup: author creates a draft");
  check((await call(author, "POST", "/clinical-rules/P8-QA-DQ/versions/1/submit")).status === 200, "setup: author submits it");
  check((await call(author, "POST", "/clinical-rules/P8-QA-DQ/versions/1/review", { decision: "Approve", note: "fixture" })).status === 403, "setup: the author cannot approve (403)");
  check((await call(doctor, "POST", "/clinical-rules/P8-QA-DQ/versions/1/review", { decision: "Approve", note: "fixture approval, local QA only" })).status === 200, "setup: a different reviewer approves");
}
await call(sara, "POST", `/patients/${sara.id}/medications`, { unregisteredName: "P8 QA herbal tonic", frequency: "AsNeeded", startDate: "2026-10-01", source: "SelfReported" });
await call(sara, "POST", "/consents", { purpose: "Monitoring", scope: ["medications", "allergies", "symptoms", "profile"], expiresAt: new Date(Date.now() + 30 * 864e5).toISOString(), version: "v1" });
check((await call(sara, "POST", `/patients/${sara.id}/safety/monitoring?locale=en`)).status === 200, "setup: the patient turns on scheduled checks");

let status;
for (let i = 0; i < 20; i++) {
  await new Promise((r) => setTimeout(r, 1500));
  status = (await call(sara, "GET", `/patients/${sara.id}/safety/monitoring`)).json;
  if (status?.lastOutcome) break;
}
check(status?.workerEnabled === true, "the local worker is switched on in this QA setup");
check(["reassessed", "unchanged"].includes(status?.lastOutcome ?? ""), `the worker ran a scheduled check (${status?.lastOutcome})`);
check(!!status?.lastSuccessAt, "last success is recorded only after a completed check");
let msgs;
for (let i = 0; i < 12; i++) {
  msgs = (await call(sara, "GET", "/guidance/messages")).json ?? [];
  if (msgs.some((m) => m.delivery?.state === "Confirmed")) break;
  await new Promise((r) => setTimeout(r, 1500));
}
const m = msgs.find((x) => x.origin?.ruleId === "P8-QA-DQ");
check(!!m, "an automated message was created for the approved rule");
check(m?.delivery?.mode === "Simulated" && m?.delivery?.state === "Confirmed", "its delivery is confirmed by the SIMULATED channel only");
check(m?.status === "Sent" && !m?.seenAt, "a confirmed simulated delivery is not 'seen'");
check(m?.provenance === "AutomatedAssessment" && m?.clinicianReviewed === false, "provenance: automated, not clinician-reviewed");

// ---------- browser ----------
const browser = await chromium.launch({ executablePath, args: ["--no-sandbox"] });
async function axe(page, label) {
  await page.evaluate(axeSource);
  const res = await page.evaluate(() => globalThis.axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] } }));
  for (const v of res.violations) check(false, `axe ${label}: [${v.impact}] ${v.id} - ${v.help} e.g. ${v.nodes[0]?.target?.join(" ")}`);
  if (!res.violations.length) check(true, `axe clean: ${label}`);
}
const noOverflow = (page) => page.evaluate(() => !(document.documentElement.scrollWidth > document.documentElement.clientWidth + 1));
const vps = { desktop: { width: 1366, height: 860 }, phone: { width: 390, height: 844 } };
const T = {
  en: { patient: /DEMO Patient — Sara/, doctor: /DEMO Physician — Dr/, author: /DEMO Content Manager|Content Manager/, adh: "Adherence, as you reported it", sim: "Simulated notification: nothing real was sent to any device.", auto: "Automated check", mon: "Scheduled checks are on", cont: /not continuous or real-time monitoring/, noneApproved: "No rule is clinically approved" },
  fa: { patient: /دمو بیمار — سارا/, doctor: /دمو پزشک — دکتر/, author: /مدیر محتوا/, adh: "پایبندی، آن‌طور که شما گزارش کرده‌اید", sim: "اعلان شبیه‌سازی‌شده: چیزی واقعی به هیچ دستگاهی فرستاده نشد.", auto: "بررسی خودکار", mon: "بررسی زمان‌بندی‌شده روشن است", cont: /پایش پیوسته یا هم‌زمان نیست/, noneApproved: "هیچ قاعده‌ای تأییدشدهٔ بالینی نیست" },
};
for (const locale of ["en", "fa"]) for (const theme of ["light", "dark"]) for (const [vpName, vp] of Object.entries(vps)) {
  const c = T[locale];
  const tag = `${locale}/${theme}/${vpName}`;
  const ctx = await browser.newContext({ viewport: vp, reducedMotion: "reduce" });
  await ctx.addInitScript(([l, t]) => { localStorage.setItem("ms.locale", l); localStorage.setItem("ms.theme", t); }, [locale, theme]);
  const page = await ctx.newPage();
  const errors = [];
  page.on("pageerror", (e) => errors.push(String(e)));
  page.on("console", (mm) => { if (mm.type() === "error" && !mm.text().includes("Failed to load resource")) errors.push(mm.text()); });
  const visit = async (path, label, waitText) => {
    await page.goto(base + path, { waitUntil: "networkidle" });
    if (waitText) await page.getByText(waitText, { exact: false }).first().waitFor({ timeout: 8000 }).catch(() => undefined);
    await page.waitForTimeout(300);
    const okW = await noOverflow(page);
    if (!okW) console.log("OVERFLOW", await page.evaluate(() => [...document.querySelectorAll("body *")].filter((e) => (e.getBoundingClientRect().right > document.documentElement.clientWidth + 1 || e.getBoundingClientRect().left < -1)).slice(0, 6).map((e) => e.tagName + "." + e.className + " " + Math.round(e.getBoundingClientRect().left) + ".." + Math.round(e.getBoundingClientRect().right))));
    check(okW, `${tag} ${label}: no overflow`);
    check(!/\b(rv|sf|adh)\.[a-z]/i.test(await page.locator("main").innerText()), `${tag} ${label}: no untranslated keys`);
    await axe(page, `${tag} ${label}`);
  };

  await page.goto(base + "/login", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: c.patient }).first().click();
  await page.waitForURL("**/app/patient**");
  await visit("/app/patient/taking", "taking", c.adh);
  check(await page.getByText(c.adh, { exact: true }).count() > 0, `${tag} taking: adherence card shown`);
  check(!/\d\s?%|compliance/i.test(await page.locator("main").innerText()), `${tag} taking: no percentage or compliance score`);
  if (theme === "light") await page.screenshot({ path: join(out, `taking-${vpName}-${locale}.png`), fullPage: true });
  await visit("/app/patient/messages", "messages", c.auto);
  check(await page.getByText(c.sim, { exact: true }).count() > 0, `${tag} messages: simulated delivery is labelled as simulated`);
  check(await page.getByText(c.auto, { exact: true }).count() > 0, `${tag} messages: provenance shown`);
  if (theme === "light") await page.screenshot({ path: join(out, `messages-${vpName}-${locale}.png`), fullPage: true });
  await visit("/app/patient/safety", "safety+monitoring", c.mon);
  check(await page.getByText(c.mon, { exact: true }).count() > 0, `${tag} safety: scheduled checks status`);
  check(await page.getByText(c.cont).count() > 0, `${tag} safety: says it is not continuous monitoring`);
  if (theme === "light") await page.screenshot({ path: join(out, `safety-${vpName}-${locale}.png`), fullPage: true });

  // professionals: new context so the session is separate
  const ctx2 = await browser.newContext({ viewport: vp, reducedMotion: "reduce" });
  await ctx2.addInitScript(([l, t]) => { localStorage.setItem("ms.locale", l); localStorage.setItem("ms.theme", t); }, [locale, theme]);
  const p2 = await ctx2.newPage();
  p2.on("pageerror", (e) => errors.push(String(e)));
  await p2.goto(base + "/login", { waitUntil: "networkidle" });
  await p2.getByRole("button", { name: c.doctor }).first().click();
  await p2.waitForURL("**/app/physician**");
  const visit2 = async (path, label, waitText) => {
    await p2.goto(base + path, { waitUntil: "networkidle" });
    if (waitText) await p2.getByText(waitText, { exact: false }).first().waitFor({ timeout: 8000 }).catch(() => undefined);
    await p2.waitForTimeout(300);
    check(await noOverflow(p2), `${tag} ${label}: no overflow`);
    await axe(p2, `${tag} ${label}`);
  };
  await visit2(`/app/physician/patients/${sara.id}/adherence`, "pro adherence", locale === "en" ? "Reported by the patient" : "گزارش‌شده توسط بیمار");
  await visit2("/app/physician/rules", "rules list", "P8-QA-DQ");
  check(await p2.locator(".sf-rules li").count() >= 5, `${tag} rules list shows demonstration and test rules`);
  await visit2("/app/physician/rules/P8-QA-DQ/1", "rule detail", c.noneApproved);
  check(await p2.getByText(c.noneApproved, { exact: true }).count() > 0, `${tag} rule detail: states that no rule is clinically approved by clicking`);
  if (theme === "light" && vpName === "desktop") await p2.screenshot({ path: join(out, `rule-detail-${locale}.png`), fullPage: true });
  await visit2("/app/physician/rules/DEMO-DQ-001/1", "demo rule detail", locale === "en" ? "Demonstration rule" : "قاعدهٔ نمایشی");
  check(await p2.getByRole("button", { name: /^(Approve|تأیید)$/ }).count() === 0, `${tag} demo rule: no approve button`);
  if (errors.length) check(false, `${tag}: console errors: ${[...new Set(errors)].slice(0, 3).join(" | ")}`);
  await ctx2.close();
  await ctx.close();
}
// the authoring editor, as the content manager (en only: same components)
{
  const ctx = await browser.newContext({ viewport: vps.desktop, reducedMotion: "reduce" });
  await ctx.addInitScript(() => { localStorage.setItem("ms.locale", "en"); localStorage.setItem("ms.theme", "light"); });
  const page = await ctx.newPage();
  await page.goto(base + "/login", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: T.en.author }).first().click();
  await page.waitForURL("**/app/**");
  await page.goto(base + "/app/workspace/rules/new", { waitUntil: "networkidle" });
  await page.getByLabel("Rule id").waitFor();
  check(await page.getByText("A draft is only a proposal. It cannot be in force until another person reviews and approves it.").count() > 0, "editor: a draft is labelled as a proposal");
  check(await page.getByRole("button", { name: /approve/i }).count() === 0, "editor: no approve control exists");
  await axe(page, "editor");
  await page.screenshot({ path: join(out, "editor-en.png"), fullPage: true });
  await ctx.close();
}
await browser.close();
console.log(`\nfailures: ${fails.length}`);
process.exit(fails.length ? 1 : 0);
