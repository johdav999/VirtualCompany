import {createRequire} from 'node:module';
import {readFile,writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const {chromium}=createRequire(import.meta.url)('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const out=fileURLToPath(new URL('.',import.meta.url));
const fixture=JSON.parse(await readFile(out+'fixture-browser.json','utf8'));
for(const [lens,labels] of [['finance',['Save status']],['marketing',['Approve content','Request revision']],['customers',['Draft reply','Run triage','Resolve case']]]) {
 const result=fixture.results.find(x=>x.lens===lens);
 for(const label of labels)if(!result.controls.some(x=>x.href===null&&x.text===label))throw Error(lens+' missing enabled '+label);
}
const company='11111111-1111-1111-1111-111111111111';
const originPath=`/dashboard?companyId=${company}&lens=company`;const base='http://localhost:5078';
const browser=await chromium.launch({channel:'msedge',headless:true});
try {
 const page=await browser.newPage({viewport:{width:1985,height:837}});const results=[];
 for(const lens of ['finance','marketing','customers']) {
  await page.goto(base+originPath);await page.waitForTimeout(1200);await page.locator('[data-testid=company-health-summary]').waitFor();
  const card=page.locator(`[data-department="${lens}"]`);const review=card.locator('[data-testid=health-department-review]');
  const href=await review.getAttribute('href');
  const prefix={finance:'/finance/',marketing:'/marketing/',customers:'/support/cases/'}[lens];
  if(!href.startsWith(prefix))throw Error(lens+' department still routes to intermediate page');
  if(new URL(href,base).searchParams.get('returnUrl')!==originPath)throw Error('Company return lost');
  const evidence=await card.locator('[data-testid=health-department-evidence]').getAttribute('href');
  if(!evidence.startsWith('/dashboard/priorities?'))throw Error('Evidence link missing');
  await review.click();await page.waitForURL(base+href);await page.waitForTimeout(1200);
  await page.locator(`a[href="${originPath}"]`).first().click();await page.waitForURL(base+originPath);
  results.push({lens,href,evidence,returnedTo:page.url()});
 }
 await page.setViewportSize({width:390,height:844});await page.locator('[data-testid=company-health-summary]').waitFor();
 await page.screenshot({path:out+'fixture-company-mobile.png',fullPage:true});
 if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw Error('Company mobile overflow');
 await writeFile(out+'departments-browser.json',JSON.stringify({results,enabledActionControls:'passed',mobileOverflow:false},null,2));
 console.log(JSON.stringify({departmentFlows:results.length,enabledActionControls:'passed',mobileOverflow:false}));
} finally {await browser.close();}
