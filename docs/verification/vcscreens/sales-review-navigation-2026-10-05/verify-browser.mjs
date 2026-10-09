import {createRequire} from 'node:module';
import {writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const {chromium}=createRequire(import.meta.url)('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const mode=process.argv[2]??'before';
const out=fileURLToPath(new URL('.',import.meta.url));
const company='43e6a825-d1b7-429a-8608-7e668087d005';
const origin=`http://localhost:5062/dashboard?companyId=${company}&lens=sales`;
const browser=await chromium.launch({channel:'msedge',headless:true});
try {
 const page=await browser.newPage({viewport:{width:1985,height:837}});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(origin);await page.waitForTimeout(1200);await page.locator('.today-priority__button').first().waitFor();
 const review=page.locator('.today-priority__button').first();
 const href=await review.getAttribute('href');
 await page.screenshot({path:out+mode+'-overview.png',fullPage:true});
 await review.click();await page.waitForURL(mode==='before'?'**/dashboard/priorities?**':'**/app/sales/deals/**');
 await page.getByRole('heading',{name:mode==='before'?'Priority details':'buying interest from farcooperation@gmail.com',exact:true}).waitFor();
 await page.waitForTimeout(1000);await page.screenshot({path:out+mode+'-review.png',fullPage:true});
 const destination=page.url();
 if(mode==='after') {
  if(new URL(destination).searchParams.get('returnUrl')!==new URL(origin).pathname+new URL(origin).search)throw Error('Return context lost');
  await page.getByRole('link',{name:'Back to originating view',exact:true}).click();await page.waitForURL(origin);
  await page.locator('.today-priority__button').first().waitFor();
  await page.setViewportSize({width:390,height:844});
  await page.screenshot({path:out+'after-overview-mobile.png',fullPage:true});
  if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw Error('Mobile overflow');
  await page.setViewportSize({width:1985,height:837});
  await page.locator('[data-testid=today-priority-evidence]').first().click();
  await page.getByRole('heading',{name:'Priority details',exact:true}).waitFor();
  await page.getByRole('link',{name:'← Back to Today',exact:true}).click();await page.waitForURL(origin);
  await page.locator('[data-testid=sales-today-section] .today-feature__action').click();
  await page.getByRole('heading',{name:'Sales review',exact:true}).waitFor();
 }
 await writeFile(out+mode+'-browser.json',JSON.stringify({origin,href,destination,errors,scope:'Existing local Web/API with configured development identity. Read-only navigation in headless Edge; in-app browser control kernel unavailable.'},null,2));
 console.log(JSON.stringify({mode,href,destination,errors}));
 if(errors.length)throw Error('Browser errors');
} finally {await browser.close();}
