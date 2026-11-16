// Logs in as each demo user once at the mock provider of scripts/preview.sh, and saves the session.
import { chromium } from 'playwright-core';
import { tmpdir } from 'node:os';

// Usage: node login.mjs. The tenant id is the one that scripts/preview.sh sets.
const tenant = '11111111-1111-1111-1111-111111111111';
const dir = tmpdir() + '/headroom-';
const base = 'http://localhost:5002';
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM ?? '/usr/bin/chromium' });

for (const [email, name] of [['planner@example.test', 'planner'], ['person@example.test', 'person']]) {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 }, colorScheme: 'light' });
  const page = await ctx.newPage();
  await page.goto(base + '/login');
  await page.click('button');
  await page.fill('input[name=username]', email);
  await page.fill('textarea[name=claims]', JSON.stringify({ tid: tenant, oid: name, preferred_username: email, name }));
  await page.click('input[type=submit]');
  await page.waitForLoadState('networkidle');
  console.log(name, page.url());
  await ctx.storageState({ path: `${dir}${name}.json` });
  await ctx.close();
}
await browser.close();
