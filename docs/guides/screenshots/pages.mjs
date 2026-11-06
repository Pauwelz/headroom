// Shoots the app pages with the saved sessions. Run login.mjs first.
// Usage: node pages.mjs [only-shot-name]
import { chromium } from 'playwright-core';
import { tmpdir } from 'node:os';

const dir = tmpdir() + '/headroom-';
const out = new URL('../user-guide/images', import.meta.url).pathname;
const only = process.argv[2];
const base = 'http://localhost:5002';
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM ?? '/usr/bin/chromium' });

async function open(user, path) {
  const ctx = await browser.newContext({
    viewport: { width: 1280, height: 800 }, colorScheme: 'light', storageState: `${dir}${user}.json`,
  });
  const page = await ctx.newPage();
  await page.goto(base + path);
  // Blazor WASM prerenders first; wait for the app to load its data.
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(1500);
  return page;
}

const settle = (page) => page.waitForTimeout(600);
// Expand the first People only: with the full demo data, more rows do not fit the screenshot.
const expandFirst = async (page, count) => {
  // An expanded button renames itself to "Collapse", so always take the first "Expand".
  const expand = page.getByRole('button', { name: /^Expand / });
  for (let i = 0; i < count && await expand.count(); i++) { await expand.first().click(); await settle(page); }
};

// Each shot returns the page; a shot that sets `full` takes the whole page.
const shots = {
  'schedule-planner': async () => {
    const page = await open('planner', '/schedule');
    await expandFirst(page, 2);
    return page;
  },
  'schedule-bar-menu': async () => {
    const page = await open('planner', '/schedule');
    await expandFirst(page, 2);
    await page.locator('.bar.alloc').last().click({ button: 'right', position: { x: 300, y: 10 } });
    await settle(page);
    return page;
  },
  'schedule-person': async () => {
    const page = await open('person', '/schedule');
    await expandFirst(page, 2);
    return page;
  },
  'projects': async () => {
    const page = await open('planner', '/projects');
    await page.getByText('Website', { exact: true }).click();
    await settle(page);
    return page;
  },
  'utilisation': async () => {
    const page = await open('planner', '/utilisation');
    await page.locator('td', { hasText: '75%' }).filter({ hasText: 'incl.' }).last().click();
    await settle(page);
    return page;
  },
  'utilisation-table': async () => {
    const page = await open('planner', '/utilisation');
    await page.getByRole('tab', { name: 'Table' }).click();
    await settle(page);
    return page;
  },
  'people': async () => {
    const page = await open('planner', '/people');
    await page.getByText('Pia Person', { exact: true }).click();
    await settle(page);
    return page;
  },
  'people-end-date': async () => {
    const page = await open('planner', '/people');
    await page.getByText('Pia Person', { exact: true }).click();
    await settle(page);
    await page.getByRole('button', { name: 'Set end date' }).click();
    await settle(page);
    return page;
  },
  'settings': async () => {
    const page = await open('planner', '/settings');
    page.full = true;
    return page;
  },
};

for (const [name, run] of Object.entries(shots)) {
  if (only && only !== name) continue;
  const page = await run();
  if (!name.includes('menu')) await page.mouse.move(1270, 790); // no hover marks
  if (!name.includes('menu') && !name.includes('end-date')) await page.evaluate(() => document.activeElement?.blur());
  await page.screenshot({ path: `${out}/${name}.png`, fullPage: !!page.full });
  await page.context().close();
  console.log('shot', name);
}
await browser.close();
