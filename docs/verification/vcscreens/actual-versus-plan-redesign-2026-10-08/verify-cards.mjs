import {createRequire} from 'node:module';
import {writeFile,readFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const {chromium}=createRequire(import.meta.url)('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const out=fileURLToPath(new URL('.',import.meta.url)),base='http://localhost:5062',company='43e6a825-d1b7-429a-8608-7e668087d005',period='8f058962-a56a-4a94-9aa9-fd9971ae7ca0',mode=process.argv[2]??'after';
const origin=`/dashboard?companyId=${company}&lens=finance`,context=`companyId=${company}&action=open&source=dashboard&returnUrl=${encodeURIComponent(origin)}`,journals=`/finance/accounting/journals?periodId=${period}&${context}`,parent=`/finance/accounting/reconciliation?${context}&periodId=${period}&accountingReturnUrl=${encodeURIComponent(journals)}`,url=`${base}/finance/reports/variance?${context}&financeReturnUrl=${encodeURIComponent(parent)}`;
const browser=await chromium.launch({channel:'msedge',headless:true}),page=await browser.newPage({viewport:{width:1857,height:837}}),results=[],errors=[];
const check=(ok,why)=>{if(!ok)throw new Error(why);};
page.on('pageerror',e=>errors.push(String(e)));page.setDefaultTimeout(20000);
try{
 await page.goto(url);await page.getByRole('heading',{name:'Comparison basis',exact:true}).waitFor();await page.waitForTimeout(1200);await page.getByRole('button',{name:'Refresh',exact:true}).waitFor();
 const context={summaries:await page.locator('.variance-summary dd').allTextContents(),rows:await page.locator('table tbody tr').count(),basis:await page.locator('.variance-method').innerText()};
 await page.screenshot({path:out+`cards-${mode}-desktop.png`,fullPage:true});await page.locator('.variance-method').screenshot({path:out+`cards-${mode}-basis.png`});
 if(mode==='before'){results.push(context);}
 else{
  const prior=JSON.parse(await readFile(out+'cards-before-browser.json','utf8')).results[0];check(JSON.stringify(prior.summaries)===JSON.stringify(context.summaries)&&prior.rows===context.rows,'Financial report context changed');
  const icons=page.locator('.variance-summary .variance-icon,.variance-rail h2 .variance-icon');check(await icons.count()===6,'Expected three summary and three rail icons');
  for(const icon of await icons.all()){check(await icon.getAttribute('aria-hidden')==='true'&&await icon.getAttribute('focusable')==='false','Decorative icon accessibility');check(await icon.evaluate(e=>getComputedStyle(e).stroke==='rgb(37, 99, 235)'&&getComputedStyle(e).fill==='none'),'Reference blue outline missing');const box=await icon.boundingBox();check(box.width>=22&&box.height>=22,'Icon is not visible');}
  results.push({flow:'Reference icons and preserved report',icons:6,...context});
  check(JSON.stringify(await page.locator('.variance-basis-list dt').allTextContents())===JSON.stringify(['Actuals','Variance','Scope']),'Structured comparison terms missing');
  check(await page.locator('.variance-method').innerText().then(t=>t.includes('Actual minus the selected budget')),'Variance explanation missing');const disclosure=page.locator('.variance-basis-details');await disclosure.locator('summary').focus();await page.keyboard.press('Enter');check(await disclosure.getAttribute('open')!==null,'Keyboard limits disclosure failed');
  const limits=await disclosure.innerText();check(limits.includes('does not prove a zero')&&limits.includes('budget is zero or missing')&&limits.includes('Opening balances'),'Calculation limits lost');await page.locator('.variance-method').screenshot({path:out+'cards-after-basis-expanded.png'});results.push({flow:'Comparison meaning and keyboard limits',limits});
  for(const width of [1366,1024,390]){await page.setViewportSize({width,height:844});check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Page overflows at '+width);for(const icon of await icons.all()){const box=await icon.boundingBox();check(box.x>=0&&box.x+box.width<=width,'Icon clipped at '+width);}if(width===390){await page.screenshot({path:out+'cards-after-narrow.png',fullPage:true});}results.push({flow:'Responsive cards',width,contained:true});}
  await page.setViewportSize({width:1857,height:837});const back=await page.getByRole('link',{name:'← Back to previous view',exact:true}).getAttribute('href'),actual=new URL(back,base),expected=new URL(parent,base);check(actual.pathname===expected.pathname,'Reconciliation return route lost');for(const [key,value]of expected.searchParams)check(actual.searchParams.get(key)===value,'Return query lost '+key);
  await page.getByRole('link',{name:'← Back to previous view',exact:true}).click();await page.waitForURL(u=>u.pathname===expected.pathname);await page.waitForTimeout(700);check(new URL(page.url()).searchParams.get('periodId')===period,'Return accounting period lost');results.push({flow:'Original reconciliation return',url:page.url()});
  check(errors.length===0,'Runtime errors: '+errors.join(';'));
 }
 await writeFile(out+`cards-${mode}-browser.json`,JSON.stringify({url,results,errors},null,2));console.log(JSON.stringify({mode,groups:results.length,errors}));
}catch(error){await writeFile(out+'cards-failure.json',JSON.stringify({error:String(error),url:page.url(),results,errors},null,2));throw error;}finally{await browser.close();}
