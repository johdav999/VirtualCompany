import {createRequire} from 'node:module';
import {writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const require=createRequire(import.meta.url);
const {chromium}=require('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const out=fileURLToPath(new URL('.',import.meta.url));
const mode=process.argv[2]??'before';
const company='43e6a825-d1b7-429a-8608-7e668087d005';
const weekly=`http://localhost:5062/dashboard?companyId=${company}&period=week&lens=company&week=2026-09-28`;
const monthly=`http://localhost:5062/dashboard?companyId=${company}&period=month&lens=company&year=2026&month=10`;
const browser=await chromium.launch({channel:'msedge',headless:true});
const results=[];
try {
 const page=await browser.newPage({viewport:{width:1996,height:881}});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 for(const [period,url] of [['weekly',weekly],['monthly',monthly]]) {
  await page.goto(url);await page.waitForTimeout(1200);
  await page.locator(`[data-testid=${period}-workspace]`).waitFor({timeout:30000});
  await page.locator(period==='weekly'?'.weekly-role':'.monthly-results').first().waitFor({timeout:30000});
  await page.screenshot({path:out+`${mode}-${period}-desktop.png`,fullPage:true});
  await page.screenshot({path:out+`${mode}-${period}-viewport.png`});
  const summary=page.locator('[data-testid=company-period-overview]');
  const overview=await summary.count() ? await summary.innerText() : null;
  if(mode==='after') {
   if(!overview)throw new Error(period+' has no Company overview');
   if(!overview.includes('Business figures for VC'))throw new Error(period+' company heading is not bound to the actual company');
   for(const area of ['Finance','Sales','Marketing','Customers','Company commitments']) if(!overview.includes(area))throw new Error(period+' missing '+area);
   const box=await summary.boundingBox();if(box.y>520)throw new Error(period+' business overview buried below fold');
   if(!overview.includes('109750 SEK'))throw new Error(period+' cash does not reconcile with source');
   if(period==='monthly' && (!overview.includes('263262 SEK')||!overview.includes('94585 SEK')||!overview.includes('1000 USD')))throw new Error('Monthly business balances/pipeline missing');
   await summary.locator('a').first().focus();const focus=await page.evaluate(()=>document.activeElement?.tagName==='A');if(!focus)throw new Error('Summary source not keyboard-focusable');
   const sourceHref=await summary.locator('a').first().getAttribute('href');if(!sourceHref.includes(company))throw new Error('Company context lost');
   if(period==='weekly') {
    await summary.locator('dt a').first().click();await page.locator('[data-testid=weekly-detail]').waitFor();
    if(!(await page.locator('.weekly-detail-value').innerText()).includes('109750 SEK'))throw new Error('Weekly source drill-down does not reconcile');
    await page.locator('.weekly-back').click();await summary.waitFor();
    if(!page.url().includes('week=2026-09-28')||!page.url().includes(company))throw new Error('Weekly source return lost context');
    await page.getByRole('button',{name:'Monthly',exact:true}).click();await page.locator('[data-testid=monthly-workspace]').waitFor();
    if(!page.url().includes('period=month')||!page.url().includes(company))throw new Error('Period navigation lost company');
    await page.goto(url);await page.waitForTimeout(1200);await page.locator('.weekly-role').first().waitFor();
   }
  }
  results.push({period,url,overview});
  await page.setViewportSize({width:390,height:844});await page.waitForTimeout(300);
  await page.screenshot({path:out+`${mode}-${period}-mobile.png`,fullPage:true});
  await page.screenshot({path:out+`${mode}-${period}-mobile-viewport.png`});
  if(mode==='after' && await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw new Error(period+' mobile overflow');
  await page.setViewportSize({width:1996,height:881});
 }
 await writeFile(out+`${mode}-browser.json`,JSON.stringify({scope:'Real running local Web/API, configured development identity, read-only report/navigation checks in fresh headless Edge. User IAB control unavailable because CUA ACL setup fails.',results,errors},null,2));
 console.log(JSON.stringify({mode,results:results.map(x=>({period:x.period,hasOverview:!!x.overview})),errors}));
 if(errors.length)process.exitCode=1;
} finally {await browser.close();}
