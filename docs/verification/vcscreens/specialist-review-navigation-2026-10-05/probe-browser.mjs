import {createRequire} from 'node:module';
import {writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const {chromium}=createRequire(import.meta.url)('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const mode=process.argv[2]??'fixture';const out=fileURLToPath(new URL('.',import.meta.url));
const base=mode==='live'?'http://localhost:5062':'http://localhost:5078';
const company=mode==='live'?'43e6a825-d1b7-429a-8608-7e668087d005':'11111111-1111-1111-1111-111111111111';
const browser=await chromium.launch({channel:'msedge',headless:true});const results=[];
try {
 const page=await browser.newPage({viewport:{width:1985,height:837}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
 for(const lens of mode==='live'?['finance','sales']:['finance','marketing','customers']) {
  const originPath=`/dashboard?companyId=${company}&lens=${lens}`;const origin=base+originPath;
  await page.goto(origin);await page.waitForTimeout(1200);await page.locator('[data-testid=today-workspace]').waitFor();
  const prefix={finance:'/finance/',marketing:'/marketing/',customers:'/support/cases/',sales:'/app/sales/deals/'}[lens];
  const reviews=await page.locator('.today-priority__button').evaluateAll(a=>a.map(x=>x.getAttribute('href')));
  const link=page.locator(`.today-priority__button[href^="${prefix}"]`).first();
  if(!await link.count())throw Error(lens+' has no owning record review: '+reviews.join(', '));
  const href=await link.getAttribute('href');
  if(new URL(href,base).searchParams.get('returnUrl')!==originPath)throw Error('Return context lost');
  await page.screenshot({path:out+mode+'-'+lens+'-overview.png'});
  await link.click();await page.waitForURL(base+href);await page.waitForTimeout(1200);
  const headings=await page.getByRole('heading').allTextContents();
  const controls=await page.locator('article.content button,article.content a,main button,main a').evaluateAll(a=>a.filter(x=>!x.disabled&&x.getBoundingClientRect().width>0).map(x=>({text:x.innerText,href:x.getAttribute('href')})));
  await page.screenshot({path:out+mode+'-'+lens+'-action.png'});
  const back=page.locator(`a[href="${originPath}"]`).first();
  if(!await back.count())throw Error(lens+' has no direct dashboard return');
  await back.click();await page.waitForURL(origin);await page.locator('[data-testid=today-workspace]').waitFor();
  const evidence=page.locator('[data-testid=today-priority-evidence]').first();await evidence.click();
  await page.getByRole('heading',{name:'Priority details',exact:true}).waitFor();
  results.push({lens,origin,href,headings,controls});
  console.log(JSON.stringify({lens,href,headings,buttons:controls.filter(x=>x.href===null).map(x=>x.text)}));
 }
 await writeFile(out+mode+'-browser.json',JSON.stringify({mode,results,errors},null,2));
 if(errors.length)throw Error('Browser errors: '+errors.join('; '));
} finally {await browser.close();}
