const fs = require('fs');
const path = require('path');
const http = require('http');
const { chromium } = require('playwright');
const root = process.cwd();
const assets = path.join(root, 'src/VirtualCompany.Web/wwwroot');
const evidence = path.join(root, 'docs/verification/browser-sales-room');
const markup = path.join(root, 'artifacts/browser-human-room-uat');
const css = fs.readFileSync(path.join(root, 'src/VirtualCompany.Web/obj/Debug/net9.0/scopedcss/bundle/VirtualCompany.Web.styles.css'), 'utf8');
const server = http.createServer((req, res) => {
  const name = new URL(req.url, 'http://localhost').pathname;
  if (/^\/(guest-prejoin|guest-lobby|host-lobby|host-live)\.html$/.test(name)) {
    res.setHeader('Content-Type', 'text/html');
    res.end(`<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/css/app.css"><style>${css}</style></head><body>${fs.readFileSync(path.join(markup, name.slice(1)), 'utf8')}</body></html>`); return;
  }
  const file = path.resolve(assets, '.' + name); if (!file.startsWith(assets + path.sep) || !fs.existsSync(file)) {res.writeHead(404);res.end();return;}
  res.setHeader('Content-Type', name.endsWith('.mjs') || name.endsWith('.js') ? 'text/javascript' : 'text/css');fs.createReadStream(file).pipe(res);
});
(async () => {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const origin = 'http://127.0.0.1:' + server.address().port;
  const browser = await chromium.launch({...(process.env.VC_UAT_BROWSER_EXECUTABLE ? {executablePath:process.env.VC_UAT_BROWSER_EXECUTABLE} : {channel:'chrome'}),headless:true});
  const results=[];
  try {
    const page = await browser.newPage();
    await page.route('**/*', route => route.request().url().startsWith(origin) ? route.continue() : route.abort());
    for (const [name, width] of [['guest-prejoin',1024],['guest-prejoin',390],['guest-lobby',390],['host-lobby',1440],['host-lobby',390],['host-live',1440],['host-live',390]]) {
      await page.setViewportSize({width,height:900}); await page.goto(`${origin}/${name}.html`);
      if (name === 'host-live') await page.evaluate(async () => {
        const {HumanRoomMedia} = await import('/js/sales-human-room.mjs');
        const controller = new HumanRoomMedia(document.querySelector('.human-room'), {invokeMethodAsync:async()=>{}}, {}, window);
        const participant = identity => ({identity,trackPublications:new Map(),isMicrophoneEnabled:false,isCameraEnabled:false,isScreenShareEnabled:false});
        controller.room={localParticipant:participant('human-host-1'),remoteParticipants:new Map([['human-guest-1',participant('human-guest-1')]]),off(){},disconnect:async()=>{}};
        controller.heartbeat([{mediaIdentity:'human-host-1',displayName:'Organizer'},{mediaIdentity:'human-guest-1',displayName:'Casey Wang'}]);
        window.fixtureMedia = controller;
      });
      await page.screenshot({path:path.join(evidence,`prompt4-${name}-${width}.png`),fullPage:true});
      const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth); if(overflow) throw new Error('Horizontal overflow in '+name+' '+width);
      results.push({name,width,overflow});
    }
    await page.goto(`${origin}/guest-prejoin.html`);
    // Real browser loads the actual pinned SDK and interop. No LiveKit connection is made.
    const sdk = await page.evaluate(async () => {
      const module = await import('/js/sales-human-room.mjs');
      await module.initialize(document.querySelector('.human-room'), {invokeMethodAsync:async()=>{}});
      await module.dispose(); return {loaded:true};
    });
    results.push({sdk});
    const fragment = await page.evaluate(async () => {
      history.replaceState(null,'','/sales/rooms/00000000-0000-0000-0000-000000000001#invite='+ 'x'.repeat(43));
      const script = document.createElement('script');script.src='/js/sales-room-entry.js';document.head.append(script);await new Promise(resolve=>script.onload=resolve);
      const module=await import('/js/sales-human-room.mjs');const entry=module.entry('00000000-0000-0000-0000-000000000001');
      return {removed:location.hash==='',exchanged:entry.secret.length===43,cleared:!window.__salesRoomInvitation};
    });
    if(!fragment.removed||!fragment.exchanged||!fragment.cleared)throw new Error('Fragment bootstrap failure');results.push({fragment});
    fs.writeFileSync(path.join(evidence,'prompt4-browser-checks.json'),JSON.stringify(results,null,2)); process.stdout.write(JSON.stringify(results));
  } finally {await browser.close();await new Promise(resolve=>server.close(resolve));}
})().catch(error=>{console.error(error.message);server.close();process.exitCode=1;});



