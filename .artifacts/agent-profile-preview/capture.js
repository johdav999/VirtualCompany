const { chromium } = require('playwright');
const path = require('path');

(async () => {
  const browser = await chromium.launch({
    headless: true,
    executablePath: 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe'
  });
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
  const consoleErrors = [];
  const missingResources = [];
  page.on('console', message => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', error => consoleErrors.push(error.message));
  page.on('response', response => {
    if (response.status() === 404) missingResources.push(response.url());
  });

  await page.goto('http://localhost:5063/agents', { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.waitForTimeout(1500);
  const lauraLink = page.locator('a', { hasText: 'Laura' }).first();
  const href = await lauraLink.getAttribute('href');
  if (!href) throw new Error('Laura profile link was not found on the roster.');

  await page.goto(new URL(href, 'http://localhost:5063').href, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.locator('.agent-profile-page').waitFor({ state: 'visible', timeout: 30000 });
  await page.waitForTimeout(1000);

  const desktop = await page.evaluate(() => {
    const selectors = ['.agent-profile-commandbar', '.agent-profile-kpis', '.agent-profile-operations', '.agent-profile-details'];
    const boxes = Object.fromEntries(selectors.map(selector => {
      const rect = document.querySelector(selector)?.getBoundingClientRect();
      return [selector, rect ? { top: rect.top, bottom: rect.bottom, left: rect.left, right: rect.right, height: rect.height } : null];
    }));
    return {
      title: document.title,
      viewport: { width: innerWidth, height: innerHeight },
      document: { width: document.documentElement.scrollWidth, height: document.documentElement.scrollHeight },
      horizontalOverflow: document.documentElement.scrollWidth > innerWidth,
      primaryWorkspaceBottom: boxes['.agent-profile-operations']?.bottom,
      detailTabsVisible: boxes['.agent-profile-details']?.top < innerHeight,
      rawJsonVisible: document.body.innerText.includes('{"'),
      placeholderCopyVisible: /placeholder only|future analytics|reserved surface/i.test(document.body.innerText),
      boxes
    };
  });

  await page.screenshot({ path: path.join(__dirname, 'agent-profile-desktop.png'), fullPage: true });
  await page.getByRole('button', { name: 'Access' }).click();
  await page.getByRole('heading', { name: 'Tool permissions' }).waitFor({ state: 'visible', timeout: 10000 });
  const accessVisible = await page.getByRole('heading', { name: 'Tool permissions' }).isVisible();
  await page.getByRole('button', { name: 'Responsibilities' }).click();

  await page.setViewportSize({ width: 390, height: 844 });
  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.locator('.agent-profile-page').waitFor({ state: 'visible', timeout: 30000 });
  await page.waitForTimeout(750);
  const mobile = await page.evaluate(() => ({
    viewport: { width: innerWidth, height: innerHeight },
    document: { width: document.documentElement.scrollWidth, height: document.documentElement.scrollHeight },
    horizontalOverflow: document.documentElement.scrollWidth > innerWidth,
    tabScrollWidth: document.querySelector('.agent-profile-tabs')?.scrollWidth,
    tabClientWidth: document.querySelector('.agent-profile-tabs')?.clientWidth
  }));
  await page.screenshot({ path: path.join(__dirname, 'agent-profile-mobile.png'), fullPage: true });

  process.stdout.write(JSON.stringify({ href, desktop, mobile, accessVisible, consoleErrors, missingResources }, null, 2));
  await browser.close();
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
