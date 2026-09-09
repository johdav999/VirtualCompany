const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
(async () => {
  const app = process.env.VC_TEAMS_WEB_URL || 'https://localhost:5298';
  const stage = app + '/teams/meetings/11111111-1111-1111-1111-111111111111/stage';
  const frameOrigin = 'https://virtualcompany-frame.example';
  const frameStage = frameOrigin + new URL(stage).pathname;
  const output = path.resolve('artifacts/teams-browser');
  fs.mkdirSync(output, { recursive: true });
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1100, height: 780 } });
    const routeHandler = async route => {
      const url = new URL(route.request().url());
      if (url.origin === new URL(app).origin) return route.continue();
      // Use actual server responses under a public-shaped test origin to avoid browser localhost network restrictions.
      if (url.origin === frameOrigin) {
        try {
          const response = await route.fetch({ url: app + url.pathname + url.search, timeout: 10000 });
          return await route.fulfill({ response });
        } catch { return route.abort(); }
      }
      if (url.pathname === '/vc-framing-test' && ['teams.microsoft.com','teams.cloud.microsoft','unrelated.example'].includes(url.hostname))
        return route.fulfill({ contentType: 'text/html', body: '<!doctype html><html><body><h1>Isolated framing verification</h1><iframe title="Actual Teams stage" style="width:100%;height:650px" src="' + frameStage + '"></iframe></body></html>' });
      return route.abort(); // No external Teams/API traffic in this local harness.
    };
    await context.route('**/*', routeHandler);
    let page = await context.newPage();
    page.on('console', message => { if (/frame|network|access|blocked/i.test(message.text())) console.log(message.text().slice(0,400)); });
    let blazorSocket = false, blazorFrame = false;
    page.on('websocket', socket => {
      if (new URL(socket.url()).pathname === '/_blazor') {
        blazorSocket = true;
        socket.on('framereceived', () => { blazorFrame = true; });
      }
    });
    const response = await page.goto(stage, { waitUntil: 'networkidle' });
    const headers = response.headers();
    if (response.status() !== 200 || headers['x-frame-options']) throw Error('Unexpected stage status or conflicting X-Frame-Options.');
    if (headers['content-security-policy'] !== "frame-ancestors 'self' https://teams.microsoft.com https://*.teams.microsoft.com https://*.cloud.microsoft")
      throw Error('Unexpected frame policy.');
    const text = await page.locator('body').innerText();
    if (!text.includes('stage')) throw Error('Actual stage fallback did not render.');
    await page.screenshot({ path: path.join(output, 'stage-direct.png'), fullPage: true });
    await page.close();
    // Framing isolates CSP from interactive runtime; the direct-page check above covers the real circuit.
    const frameContext = await browser.newContext({ ignoreHTTPSErrors: true, javaScriptEnabled: false, viewport: { width: 1100, height: 780 } });
    await frameContext.route('**/*', routeHandler);
    page = await frameContext.newPage();
    for (const host of ['teams.microsoft.com','teams.cloud.microsoft']) {
      await page.goto('https://' + host + '/vc-framing-test', { waitUntil: 'networkidle' });
      await page.frameLocator('iframe').locator('body').filter({ hasText: /stage/i }).waitFor({ timeout: 15000 });
      const frameText = await page.frameLocator('iframe').locator('body').innerText();
      if (!frameText.includes('stage')) throw Error('Allowed Teams host could not frame the stage: ' + host + ' body=' + frameText.slice(0,120));
      await page.screenshot({ path: path.join(output, host + '.png'), fullPage: true });
    }
    const denied = page.waitForEvent('console', { predicate: message => /frame-ancestors|Refused to frame|Framing.*violates/i.test(message.text()), timeout: 10000 });
    await page.goto('https://unrelated.example/vc-framing-test', { waitUntil: 'domcontentloaded' });
    await denied;
    await page.screenshot({ path: path.join(output, 'unrelated-denied.png'), fullPage: true });
    if (!blazorSocket || !blazorFrame) throw Error('Blazor WebSocket circuit was not observed.');
    const result = { stageStatus: response.status(), csp: headers['content-security-policy'], xFrameOptions: null,
      teamsHostAllowed: true, cloudHostAllowed: true, unrelatedHostDenied: true, blazorWebSocket: true,
      scope: 'Real server responses proxied to controlled framing origins; direct local Blazor WebSocket verified. No tenant/SSO/live media verification.' };
    fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify(result, null, 2));
    console.log(JSON.stringify(result));
  } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
