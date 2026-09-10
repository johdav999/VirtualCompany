const fs = require('fs');
const path = require('path');
const http = require('http');
const { chromium } = require('playwright');

const root = process.cwd();
const assets = path.join(root, 'src/VirtualCompany.Web/wwwroot');
const evidence = path.join(root, 'docs/verification/browser-sales-room');
const markup = path.join(root, 'artifacts/browser-human-room-uat');
const css = fs.readFileSync(path.join(root,
  'src/VirtualCompany.Web/obj/Debug/net9.0/scopedcss/bundle/VirtualCompany.Web.styles.css'), 'utf8');
const suffix = process.env.VC_UAT_EVIDENCE_SUFFIX ? `-${process.env.VC_UAT_EVIDENCE_SUFFIX}` : '';

const server = http.createServer((request, response) => {
  const name = new URL(request.url, 'http://localhost').pathname;
  if (/^\/(host|guest)-presentation\.html$/.test(name)) {
    response.setHeader('Content-Type', 'text/html');
    response.end(`<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css"><link rel="stylesheet" href="/css/app.css"><style>${css}</style></head><body>${fs.readFileSync(path.join(markup, name.slice(1)), 'utf8')}</body></html>`);
    return;
  }
  const file = path.resolve(assets, '.' + name);
  if (!file.startsWith(assets + path.sep) || !fs.existsSync(file)) {
    response.writeHead(404); response.end(); return;
  }
  response.setHeader('Content-Type', name.endsWith('.css') ? 'text/css' : 'application/octet-stream');
  fs.createReadStream(file).pipe(response);
});

(async () => {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const origin = `http://127.0.0.1:${server.address().port}`;
  const browser = await chromium.launch({
    ...(process.env.VC_UAT_BROWSER_EXECUTABLE
      ? { executablePath: process.env.VC_UAT_BROWSER_EXECUTABLE }
      : { channel: 'chrome' }),
    headless: true
  });
  const results = [];
  try {
    const hostContext = await browser.newContext();
    const guestContext = await browser.newContext();
    const host = await hostContext.newPage();
    const guest = await guestContext.newPage();
    for (const page of [host, guest])
      await page.route('**/*', route => route.request().url().startsWith(origin) ? route.continue() : route.abort());

    await Promise.all([
      host.goto(`${origin}/host-presentation.html`),
      guest.goto(`${origin}/guest-presentation.html`)
    ]);
    const hostSlide = await host.locator('.room-presentation img').getAttribute('src');
    const guestSlide = await guest.locator('.room-presentation img').getAttribute('src');
    if (!hostSlide || hostSlide !== guestSlide) throw new Error('Host and guest did not render the same slide asset.');
    if (await guest.getByText('Confidential host note', { exact: true }).count())
      throw new Error('Private speaker notes reached the guest browser.');
    if (await guest.locator('[aria-label="Private presentation controls"]').count())
      throw new Error('Private presentation controls reached the guest browser.');
    if (!(await host.getByText('Confidential host note', { exact: true }).count()))
      throw new Error('Host speaker notes are missing.');

    for (const [page, name, widths] of [[host, 'host', [1440, 390]], [guest, 'guest', [1024, 390]]]) {
      for (const width of widths) {
        await page.setViewportSize({ width, height: 900 });
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth);
        if (overflow) throw new Error(`Horizontal overflow in ${name} presentation at ${width}px.`);
        await page.screenshot({
          path: path.join(evidence, `prompt5${suffix}-${name}-presentation-${width}.png`),
          fullPage: true
        });
        results.push({ surface: name, width, overflow });
      }
    }

    await Promise.all([host.reload(), guest.reload()]);
    const restored = await Promise.all([host.locator('.room-presentation img').count(), guest.locator('.room-presentation img').count()]);
    if (restored.some(count => count !== 1)) throw new Error('Presentation fixture did not restore after browser reload.');
    results.push({ simultaneousContexts: 2, sameCommittedSlide: true, guestPrivateControls: 0,
      guestPrivateNotes: 0, reloadRestoredBoth: true });
    fs.writeFileSync(path.join(evidence, `prompt5${suffix}-browser-checks.json`), JSON.stringify(results, null, 2));
    process.stdout.write(JSON.stringify(results));
    await hostContext.close(); await guestContext.close();
  } finally {
    await browser.close();
    await new Promise(resolve => server.close(resolve));
  }
})().catch(error => { console.error(error.message); server.close(); process.exitCode = 1; });
