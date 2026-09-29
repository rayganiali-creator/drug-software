#!/usr/bin/env node
// Signs in through the REAL login screen (no storage tricks) and captures the screens reviewers ask for, asserting the essentials.
//   node scripts/auth-shots.mjs [--base http://127.0.0.1:4173] [--out /tmp/shots] [--prefix local]
import { mkdirSync } from "node:fs";
import { join } from "node:path";
import { chromium } from "playwright-core";

const arg = (name, def) => { const i = process.argv.indexOf(name); return i > -1 ? process.argv[i + 1] : def; };
const base = arg("--base", "http://127.0.0.1:4173");
const out = arg("--out", "/tmp/shots");
const prefix = arg("--prefix", "local");
mkdirSync(out, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH ?? "/opt/pw-browsers/chromium-1194/chrome-linux/chrome";
const browser = await chromium.launch({ executablePath, args: ["--no-sandbox"] });
const fails = [];
const check = (ok, msg) => { if (!ok) fails.push(msg); console.log(ok ? "ok  " : "FAIL", msg); };

async function ctx(locale = "en", viewport = { width: 1366, height: 860 }) {
  const c = await browser.newContext({ viewport, reducedMotion: "reduce" });
  await c.addInitScript(([l]) => { localStorage.setItem("ms.locale", l); localStorage.setItem("ms.theme", "light"); }, [locale]);
  return c;
}
async function login(page, nameRe, urlRe) {
  await page.goto(base + "/login", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: nameRe }).click();
  await page.waitForURL(urlRe);
  await page.waitForLoadState("networkidle");
  await page.waitForTimeout(400);
}
const shot = (page, name) => page.screenshot({ path: join(out, `${prefix}-${name}.png`), fullPage: false });

// 1. Login screen (signed out)
{
  const c = await ctx(); const p = await c.newPage();
  await p.goto(base + "/login", { waitUntil: "networkidle" }); await p.waitForTimeout(500);
  check(await p.getByText(/DEMO ENVIRONMENT/).count() > 0, "login screen is labelled DEMO ENVIRONMENT");
  check(await p.getByRole("button", { name: /Sign in as/ }).count() > 8, "login lists the fictional demo accounts");
  check((await p.locator("input[type=password]").count()) === 0, "login has no password field");
  await shot(p, "01-login"); await c.close();
}
// 2. Patient after login
{
  const c = await ctx(); const p = await c.newPage();
  await login(p, /Sign in as DEMO Patient — Sara/, "**/app/patient");
  check(await p.getByText(/Next dose/).count() > 0, "patient lands on the patient home");
  await shot(p, "02-patient-home");
  await p.goto(base + "/app/admin", { waitUntil: "networkidle" }); await p.waitForTimeout(300);
  check(await p.getByTestId("unauthorized").count() > 0, "patient opening /app/admin sees the neutral no-access page");
  await shot(p, "05-unauthorized");
  await p.goto(base + "/app/account", { waitUntil: "networkidle" }); await p.waitForTimeout(500);
  check(await p.getByText("Data sharing consents").count() > 0, "patient account page lists consents");
  await shot(p, "06-account-patient"); await c.close();
}
// 3. Physician after login
{
  const c = await ctx(); const p = await c.newPage();
  await login(p, /Sign in as DEMO Physician — Dr\. Karimi/, "**/app/physician");
  check(await p.getByText("Patients needing attention").count() > 0, "physician lands on the physician dashboard");
  await shot(p, "03-physician-dashboard");
  await p.goto(base + "/app/physician/patients", { waitUntil: "networkidle" }); await p.waitForTimeout(500);
  check((await p.getByText("Ali Rezaei").count()) === 0, "physician list excludes the patient whose consent expired");
  await shot(p, "03b-physician-patients"); await c.close();
}
// 4. Pharmacist after login
{
  const c = await ctx(); const p = await c.newPage();
  await login(p, /Sign in as DEMO Pharmacist/, "**/app/pharmacist");
  check(await p.getByText("Work queue").count() > 0, "pharmacist lands on the work queue");
  await shot(p, "04-pharmacist-workqueue"); await c.close();
}
// 5. System admin
{
  const c = await ctx(); const p = await c.newPage();
  await login(p, /Sign in as DEMO System Admin/, "**/app/admin");
  check(await p.getByText("Administrators cannot open patient records.").count() > 0, "admin console states that patient records are unavailable");
  await shot(p, "07-admin"); await c.close();
}
// 6. Persian (RTL) login + phone login
{
  const c = await ctx("fa"); const p = await c.newPage();
  await p.goto(base + "/login", { waitUntil: "networkidle" }); await p.waitForTimeout(500);
  check((await p.evaluate(() => document.documentElement.dir)) === "rtl", "Persian login is RTL");
  await shot(p, "08-login-fa"); await c.close();
  const m = await ctx("en", { width: 390, height: 844 }); const q = await m.newPage();
  await q.goto(base + "/login", { waitUntil: "networkidle" }); await q.waitForTimeout(400);
  check(!(await q.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1)), "phone login has no horizontal overflow");
  await shot(q, "09-login-phone"); await m.close();
}
await browser.close();
process.exit(fails.length ? 1 : 0);
