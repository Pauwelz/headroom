// Logs in as each demo user once, saves the session, and shoots the two login steps.
import { chromium } from 'playwright-core';
import { readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';

// Usage: node login.mjs <app log file>
const appLog = process.argv[2];
const out = new URL('../user-guide/images', import.meta.url).pathname;
const dir = tmpdir() + '/headroom-';
const base = 'http://localhost:5002';
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM ?? '/usr/bin/chromium' });

const codeFor = (email) => {
  const log = readFileSync(appLog, 'utf8');
  const hits = [...log.matchAll(new RegExp(`Login code for ${email.replace('.', '\\.')} \\(.*?\\): (\\d{6})`, 'g'))];
  return hits.at(-1)?.[1];
};

for (const [email, name, shoot] of [['planner@example.test', 'planner', true], ['person@example.test', 'person', false]]) {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 }, colorScheme: 'light' });
  const page = await ctx.newPage();
  await page.goto(base + '/login');
  await page.fill('input[name=email]', email);
  if (shoot) await page.screenshot({ path: `${out}/login-email.png` });
  await page.click('button');
  await page.waitForSelector('form[action="/login/verify"]');
  await page.waitForTimeout(500);
  const code = codeFor(email);
  if (!code) throw new Error('no code for ' + email);
  await page.locator('form[action="/login/verify"] input:visible').first().fill(code);
  if (shoot) await page.screenshot({ path: `${out}/login-code.png` });
  await page.click('form[action="/login/verify"] button');
  await page.waitForLoadState('networkidle');
  console.log(name, page.url());
  await ctx.storageState({ path: `${dir}${name}.json` });
  await ctx.close();
}
await browser.close();
