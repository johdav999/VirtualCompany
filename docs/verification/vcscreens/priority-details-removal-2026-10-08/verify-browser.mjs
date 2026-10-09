import {createRequire} from 'node:module';
import {writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const {chromium}=createRequire(import.meta.url)('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const out=fileURLToPath(new URL('.',import.meta.url)),base='http://localhost:5062',company='43e6a825-d1b7-429a-8608-7e668087d005',mode=process.argv[2]??'after';
const browser=await chromium.launch({channel:'msedge',headless:true}),page=await browser.newPage({viewport:{width:1857,height:837}}),results=[],errors=[];
page.setDefaultTimeout(30000);page.on('pageerror',e=>errors.push(String(e)));
const check=(ok,message)=>{if(!ok)throw new Error(message);};
try{
 for(const lens of ['company','finance','sales','customers','marketing']){
  await page.goto(`${base}/dashboard?companyId=${company}&lens=${lens}`);
  await page.locator('[data-testid="today-workspace"]').waitFor();await page.waitForTimeout(700);
  const links=await page.locator('a[href*="/dashboard/priorities"]').count(),details=await page.getByRole('link',{name:/Priority details/i}).count();
  if(mode==='after'){check(links===0&&details===0,`${lens}: retired priority links remain`);}
  await page.screenshot({path:out+`${mode}-${lens}-desktop.png`,fullPage:true});
  const review=page.locator('.today-priority__button, [data-testid="health-department-review"]').first();
  let directReview=null;
  if(mode==='after'&&await review.count()){
   directReview=await review.getAttribute('href');check(!directReview.includes('/dashboard/priorities'),'Review targets retired page');
   await review.click();await page.waitForTimeout(500);check(new URL(page.url()).pathname!=='/dashboard/priorities','Review opened retired page');
   check(!(await page.getByText('Sorry, there\'s nothing at this address.',{exact:true}).count()),'Direct review destination missing');
   check(await page.locator('a[href*="/dashboard"]').count()>0,'Record lost Overview return');
  }
  results.push({lens,retiredLinks:links,priorityDetailLinks:details,directReview,passed:mode==='after'?links===0&&details===0:true});
 }
 if(mode==='after'){
  await page.goto(`${base}/dashboard?companyId=${company}&lens=company`);await page.locator('[data-testid="today-workspace"]').waitFor();await page.waitForTimeout(700);
  await page.setViewportSize({width:390,height:844});await page.screenshot({path:out+'after-company-narrow.png',fullPage:true});
  check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Narrow dashboard overflow');
  const retiredUrl=`${base}/dashboard/priorities?companyId=${company}&lens=company&key=retired`;
  const retired=await page.request.get(retiredUrl);check(retired.status()===404,'Retired detail route is still registered');
  results.push({id:'retired-route',status:retired.status(),passed:true});
  try{await page.goto(retiredUrl);}catch(e){check(String(e).includes('ERR_HTTP_RESPONSE_CODE_FAILURE'),'Unexpected retired-route failure: '+e);}
  await page.screenshot({path:out+'retired-route.png'});
  check(errors.length===0,errors.join('; '));
 }
 await writeFile(out+`${mode}-browser.json`,JSON.stringify({timeUtc:new Date().toISOString(),company,mode,environment:'Existing local company; headless Edge; read-only journeys',results,errors},null,2));console.log(JSON.stringify({mode,roles:results.length,errors:errors.length}));
}catch(e){await page.screenshot({path:out+`${mode}-diagnostic.png`,fullPage:true});throw e;}finally{await browser.close();}
