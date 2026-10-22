// Uji beban dengan browser sungguhan: setiap user virtual login, lalu berulang kali membuka Inquiry Status Debitur,
// mencari debitur, membuka detail, berpindah tab, dan kembali. Yang diukur adalah waktu dari klik sampai layar
// berubah (sirkuit Blazor + query), bukan hanya HTTP.
//
//   npm install            (sekali, di folder ini; tidak mengunduh browser)
//   node flow.mjs --url http://localhost:5122 --first 701 --users 100 --seconds 120 --think 3000 --password <pwd>
//
// User uji LT0001..LT1000 dibuat oleh `Panin.LoadTest seed`. Rentang --first/--users jangan bertabrakan dengan
// uji `circuits` yang berjalan bersamaan (aturan satu sesi per user).
import { chromium } from "playwright-core";
import { existsSync, readdirSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";

const arg = (name, fallback) => {
  const i = process.argv.indexOf(`--${name}`);
  return i > 0 ? process.argv[i + 1] : fallback;
};
const url = arg("url", "http://localhost:5122");
const first = Number(arg("first", 701));
const users = Number(arg("users", 50));
const seconds = Number(arg("seconds", 60));
const think = Number(arg("think", 3000));
const password = arg("password", process.env.LOADTEST_PASSWORD);
const rampMs = Number(arg("ramp", 20000));
if (!password) {
  console.error("Isi --password atau LOADTEST_PASSWORD");
  process.exit(2);
}

function findBrowser() {
  const root = join(homedir(), "AppData", "Local", "ms-playwright");
  if (!existsSync(root)) return undefined;
  for (const dir of readdirSync(root).filter((d) => d.startsWith("chromium_headless_shell")).sort().reverse()) {
    const exe = join(root, dir, "chrome-headless-shell-win64", "chrome-headless-shell.exe");
    if (existsSync(exe)) return exe;
  }
  return undefined;
}

const prefixes = ["RONAL", "PERMA", "DEDE", "JOYCE", "ROSMI", "ESTIZ", "PURWA", "DEDDY", "VIDA", "DENNY", "LENNY", "SUSI"];
const samples = new Map(); // aksi -> [ms]
const errors = new Map(); // pesan -> jumlah
const record = (action, ms) => { if (!samples.has(action)) samples.set(action, []); samples.get(action).push(ms); };
const fail = (action, e) => { const k = `${action}: ${String(e.message ?? e).split("\n")[0].slice(0, 120)}`; errors.set(k, (errors.get(k) ?? 0) + 1); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const jitter = () => think * (0.5 + Math.random());

async function timed(action, fn) {
  const t = performance.now();
  try {
    await fn();
    record(action, performance.now() - t);
    return true;
  } catch (e) {
    fail(action, e);
    return false;
  }
}

async function virtualUser(browser, index, deadline) {
  const id = `LT${String(index).padStart(4, "0")}`;
  const context = await browser.newContext();
  const page = await context.newPage();
  page.setDefaultTimeout(20000);
  try {
    if (!(await timed("login", async () => {
      await page.goto(`${url}/login`);
      await page.fill("#userid", id);
      await page.fill("#password", password);
      await Promise.all([page.waitForURL((u) => !u.pathname.startsWith("/login")), page.click("button[type=submit]")]);
      await page.waitForSelector("aside, nav");
    }))) return;

    const nav = page.locator("aside, nav");
    let n = index;
    while (performance.now() < deadline) {
      await timed("menu", async () => {
        const leaf = nav.getByText("Inquiry Status Debitur", { exact: true });
        if (!(await leaf.isVisible())) await nav.getByText("Inquiry", { exact: true }).first().click();
        await leaf.click();
        await page.locator("h1:visible", { hasText: "Inquiry Status Debitur" }).waitFor();
      });
      await sleep(jitter());

      const prefix = prefixes[n++ % prefixes.length];
      let found = false;
      await timed("search", async () => {
        const visibleText = () => [...document.querySelectorAll("section")].filter((e) => e.offsetParent).map((e) => e.innerText).join("|");
        const before = await page.evaluate(visibleText);
        await page.locator("#name:visible").fill(prefix);
        await page.locator("#acc:visible").fill("");
        await page.locator("button:visible", { hasText: /^CARI$/ }).click();
        await page.waitForFunction(
          ([fn, prev]) => { const t = new Function(`return (${fn})()`)(); return t !== prev && /\d+ baris|tidak ditemukan/.test(t); },
          [visibleText.toString(), before]);
        found = (await page.locator("main button:visible", { hasText: /^Detail$/ }).count()) > 0;
      });
      await sleep(jitter());

      if (found) {
        if (await timed("detail", async () => {
          await page.locator("main button:visible", { hasText: /^Detail$/ }).first().click();
          await page.locator("h1:visible", { hasText: "Monitoring Collection" }).waitFor();
        })) {
          for (const tab of ["Pekerjaan", "Kontak"]) {
            await sleep(jitter());
            await timed("tab", async () => {
              await page.locator("[role=tab]:visible", { hasText: new RegExp(`^${tab}$`) }).click();
              await page.waitForFunction((t) => [...document.querySelectorAll('[role=tab]')].some((e) => e.offsetParent && e.hasAttribute('aria-selected') && e.innerText.trim() === t), tab);
            });
          }
          await sleep(jitter());
          await timed("back", async () => {
            await page.locator("button:visible", { hasText: /Kembali/ }).first().click();
            await page.locator("h1:visible", { hasText: "Monitoring Collection" }).waitFor({ state: "hidden" });
          });
        }
      }
      await sleep(jitter());
    }
    await timed("logout", async () => {
      await Promise.all([page.waitForURL(/\/login/), page.getByRole("button", { name: "Logout" }).click()]);
    });
  } finally {
    await context.close().catch(() => {});
  }
}

const pct = (arr, p) => { const s = [...arr].sort((a, b) => a - b); return s[Math.min(s.length - 1, Math.floor((p / 100) * s.length))]; };

const browser = await chromium.launch({ executablePath: findBrowser(), headless: true });
const start = performance.now();
const deadline = start + (rampMs + seconds * 1000);
console.log(`browser: ${users} user (${first}..${first + users - 1}), ${seconds}s setelah ramp ${rampMs / 1000}s, think ~${think} ms`);
await Promise.all(Array.from({ length: users }, async (_, i) => {
  await sleep((rampMs * i) / users);
  await virtualUser(browser, first + i, deadline);
}));
await browser.close();

console.log(`\nselesai dalam ${((performance.now() - start) / 1000).toFixed(0)} s`);
console.log("aksi        n     p50    p95    p99    max (ms)");
for (const [action, arr] of [...samples.entries()].sort()) {
  console.log(`${action.padEnd(9)} ${String(arr.length).padStart(5)} ${[50, 95, 99].map((p) => String(Math.round(pct(arr, p))).padStart(6)).join(" ")} ${String(Math.round(Math.max(...arr))).padStart(6)}`);
}
if (errors.size) {
  console.log("\nGALAT:");
  for (const [k, v] of errors) console.log(`  ${v}x ${k}`);
} else {
  console.log("\ntanpa galat");
}
