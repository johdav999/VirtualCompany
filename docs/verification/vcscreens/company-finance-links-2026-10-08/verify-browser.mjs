import {createRequire} from 'node:module';
import {writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const {chromium}=createRequire(import.meta.url)('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const out=fileURLToPath(new URL('.',import.meta.url)),base='http://localhost:5062',company='43e6a825-d1b7-429a-8608-7e668087d005',mode=process.argv[2]??'after';
const origin=`/dashboard?companyId=${company}&lens=company`;
const browser=await chromium.launch({channel:'msedge',headless:true}),page=await browser.newPage({viewport:{width:1857,height:837}}),results=[],errors=[];
page.setDefaultTimeout(30000);page.on('pageerror',e=>errors.push(String(e)));
const check=(ok,message)=>{if(!ok)throw new Error(message);};
async function overview(){await page.goto(base+origin);await page.locator('[data-department="finance"] .company-summary__indicators').waitFor();await page.waitForTimeout(600);}
try{
 await overview();
 const card=page.locator('[data-department="finance"]');
 await card.screenshot({path:out+`${mode}-finance-card.png`});
 if(mode==='before'){
  results.push({links:await card.locator('.company-summary__indicators a').count(),counts:await card.locator('dd').allTextContents()});
 }else{
  const cases=[['Overdue invoices','/finance/invoices'],['Supplier bills due','/finance/supplier-bills'],['Reconciliation exceptions','/finance/accounting/reconciliation']];
  for(const [label,path] of cases){
   await overview();
   const link=card.getByRole('link',{name:label,exact:true}),href=await link.getAttribute('href'),url=new URL(href,base);
   check(url.pathname===path,`${label}: wrong screen`);check(url.searchParams.get('companyId')===company,`${label}: company lost`);check(url.searchParams.get('returnUrl')===origin,`${label}: return lost`);
   await link.focus();check(await link.evaluate(e=>e===document.activeElement),`${label}: keyboard focus failed`);
   await link.press('Enter');await page.waitForURL(u=>u.pathname===path);await page.waitForTimeout(900);
   check(!(await page.getByText("Sorry, there's nothing at this address.",{exact:true}).count()),`${label}: missing destination`);
   const returnLinks=page.locator('a[href]').filter({hasText:/Overview/i});
   const matching=await returnLinks.evaluateAll((nodes,origin)=>nodes.filter(e=>e.getAttribute('href')===origin).length,origin);
   check(matching>0,`${label}: no exact Overview return link`);
   await page.screenshot({path:out+`destination-${path.split('/').at(-1)}.png`,fullPage:true});
   const returnLink=page.locator(`a[href="${origin}"]`).filter({hasText:/Overview/i}).first();
   await returnLink.click();await page.waitForURL(u=>u.pathname==='/dashboard'&&u.searchParams.get('lens')==='company');
   results.push({label,path,href,keyboardNavigation:true,overviewReturn:true,passed:true});
  }
  await overview();await page.setViewportSize({width:390,height:844});await card.screenshot({path:out+'after-finance-card-narrow.png'});
  check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Narrow dashboard overflow');
  check(await card.locator('.company-summary__indicators a').count()===3,'Narrow links lost');
  check(errors.length===0,errors.join('; '));
 }
 await writeFile(out+`${mode}-browser.json`,JSON.stringify({timeUtc:new Date().toISOString(),mode,company,environment:'Existing local company; headless Edge; read-only navigation',results,errors},null,2));console.log(JSON.stringify({mode,results,errors}));
}catch(e){await page.screenshot({path:out+`${mode}-diagnostic.png`,fullPage:true});throw e;}finally{await browser.close();}
