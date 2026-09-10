const fs = require('fs');
const path = require('path');
const http = require('http');
const { chromium } = require('playwright');

const root = process.cwd();
const evidence = path.join(root, 'docs/verification/browser-sales-room');
const markup = path.join(root, 'artifacts/browser-human-room-uat/host-agent.html');
const css = fs.readFileSync(path.join(root,
  'src/VirtualCompany.Web/obj/Debug/net9.0/scopedcss/bundle/VirtualCompany.Web.styles.css'), 'utf8');
if (!fs.existsSync(markup)) throw new Error('Run SalesHumanRoomTests with VC_BROWSER_UAT_DIRECTORY first.');

const server = http.createServer((request, response) => {
  if (new URL(request.url, 'http://localhost').pathname !== '/host-floor.html') {
    response.writeHead(404); response.end(); return;
  }
  response.setHeader('Content-Type', 'text/html; charset=utf-8');
  response.end(`<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}</style></head><body>${fs.readFileSync(markup, 'utf8')}</body></html>`);
});

(async () => {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const origin = `http://127.0.0.1:${server.address().port}`;
  const browser = await chromium.launch({
    ...(process.env.VC_UAT_BROWSER_EXECUTABLE
      ? { executablePath: process.env.VC_UAT_BROWSER_EXECUTABLE }
      : { channel: 'chrome' }), headless: true
  });
  const results = [];
  try {
    const page = await browser.newPage();
    await page.route('**/*', route => route.request().url().startsWith(origin) ? route.continue() : route.abort());
    for (const width of [1440, 390]) {
      await page.setViewportSize({ width, height: width === 390 ? 844 : 1100 });
      await page.goto(`${origin}/host-floor.html`);
      const result = await page.evaluate(() => ({
        overflow: document.documentElement.scrollWidth > innerWidth,
        floorOwner: document.body.innerText.includes('Floor: Casey Wang'),
        modes: ['Manual', 'Assisted', 'Autonomous'].every(x => document.body.innerText.includes(x)),
        resume: document.body.innerText.includes('Resume from current point'),
        takeover: document.body.innerText.includes('Take over'),
        pending: document.body.innerText.includes('Needs host confirmation'),
        approval: document.body.innerText.includes('Approve answer'),
        receipt: document.body.innerText.includes('1 of 2 clients acknowledged'),
        readiness: document.body.innerText.includes('Audience readiness')
      }));
      if (result.overflow || Object.entries(result).some(([key, value]) => key !== 'overflow' && !value))
        throw new Error(`Prompt 8 browser assertion failed at ${width}px: ${JSON.stringify(result)}`);
      await page.screenshot({ path: path.join(evidence, `prompt8-host-floor-${width}.png`), fullPage: true });
      results.push({ width, ...result });
    }
    fs.writeFileSync(path.join(evidence, 'prompt8-browser-checks.json'), JSON.stringify(results, null, 2));
    process.stdout.write(JSON.stringify(results));
  } finally {
    await browser.close();
    await new Promise(resolve => server.close(resolve));
  }
})().catch(error => { console.error(error.message); server.close(); process.exitCode = 1; });
