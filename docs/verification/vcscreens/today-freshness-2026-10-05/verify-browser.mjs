import {createRequire} from 'node:module';
import {writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const {chromium}=createRequire(import.meta.url)('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const mode=process.argv[2]??'before';const out=fileURLToPath(new URL('.',import.meta.url));
const url='http://localhost:5062/dashboard?companyId=43e6a825-d1b7-429a-8608-7e668087d005&lens=company';
const browser=await chromium.launch({channel:'msedge',headless:true});
try {
 const page=await browser.newPage({viewport:{width:1985,height:837}});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(url);await page.waitForTimeout(1200);await page.locator('[data-testid=today-freshness]').waitFor();
 const initial=await page.locator('[data-testid=today-freshness]').innerText();
 await page.getByRole('button',{name:'Refresh workspace',exact:true}).first().click();
 await page.waitForTimeout(1200);await page.locator('[data-testid=today-freshness]').waitFor();
 const refreshed=await page.locator('[data-testid=today-freshness]').innerText();
 const staleBanner=await page.locator('[data-testid=today-stale]').count();
 const summary=await page.locator('.today-situation').innerText();
 const sourceWarnings=await page.locator('.today-priority').allTextContents();
 await page.screenshot({path:out+mode+'-company-today.png',fullPage:true});
 if(mode==='before' && !staleBanner)throw Error('Original stale warning not reproduced');
 if(mode==='after') {
  if(staleBanner || refreshed.includes('needs a refresh'))throw Error('Refresh still uses stale briefing');
  if(summary.includes('Daily briefing for VC'))throw Error('Stale briefing copy still used');
  if(!sourceWarnings.some(text=>text.includes('This view needs a refresh')))throw Error('Source age warnings hidden');
  await page.reload();await page.waitForTimeout(1200);await page.locator('[data-testid=today-freshness]').waitFor();
  if(await page.locator('[data-testid=today-stale]').count())throw Error('Reload restores misleading warning');
 }
 await writeFile(out+mode+'-browser.json',JSON.stringify({url,initial,refreshed,staleBanner,summary,sourceWarnings,errors,scope:'Real local Web/API, configured development identity; read-only workspace refresh/reload in headless Edge.'},null,2));
 console.log(JSON.stringify({mode,initial,refreshed,staleBanner,summary,errors}));
 if(errors.length)throw Error('Browser errors');
} finally {await browser.close();}
