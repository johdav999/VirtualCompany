import {createRequire} from 'node:module';
import {writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const require=createRequire(import.meta.url);
const {chromium}=require('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const out=fileURLToPath(new URL('.',import.meta.url)),api='http://localhost:5348',web='http://localhost:5108';
const profile=await(await fetch(api+'/_uat/p23/profile')).json();
const company=profile.company.company,account=profile.account;
const headers={'X-Dev-Auth-Subject':'p19-owner','X-Dev-Auth-Email':'p19-owner@example.test','X-Company-Id':company};
const root=`${api}/internal/companies/${company}/finance/planning`;
const query=`year=2026&month=9&months=4&budgetVersion=approved&currency=SEK&financeAccountId=${account}`;
const reportUrl=`${web}/finance/reports/variance?companyId=${company}&${query}`;
const browser=await chromium.launch({channel:'msedge',headless:true}),errors=[],results=[];
const check=(ok,message)=>{if(!ok)throw new Error(message);};
async function json(url,options={}){const response=await fetch(url,{headers,...options});check(response.ok,`API ${response.status}: ${await response.clone().text()}`);return response.json();}
async function ready(page,selector){await page.waitForTimeout(350);await page.waitForFunction(s=>!!document.querySelector(s)&&!Array.from(document.querySelectorAll('[role="status"]')).some(e=>/^Loading (authorized Finance|or validating Finance|saved Finance)/.test(e.innerText)),selector);}
try{
 const page=await browser.newPage({viewport:{width:1440,height:1000}});page.on('pageerror',e=>errors.push(e.message));
 await page.goto(`${web}/finance/receivables-aging?companyId=${company}`);
 await page.getByRole('link',{name:'Actual versus plan',exact:true}).waitFor({timeout:45000});await page.getByRole('link',{name:'Actual versus plan',exact:true}).click();
 await page.getByRole('heading',{name:'Actual versus plan',exact:true}).waitFor();
 await page.goto(reportUrl);await page.getByRole('button',{name:'Inspect sources',exact:true}).first().waitFor({timeout:30000});
 check((await page.locator('.planning-evidence').innerText()).includes('99.90'),'Variance did not reconcile');
 const noteText='Browser review: revenue timing explains the signed account variance. Run '+Date.now();
 await page.getByRole('button',{name:'Inspect sources',exact:true}).first().click();await page.getByLabel('Explain this variance').fill(noteText);
 await page.getByRole('button',{name:'Save explanation',exact:true}).click();await page.getByText(noteText,{exact:false}).waitFor();
 await page.locator('.source-panel').getByRole('link',{name:'Open source',exact:true}).first().click();
 await page.waitForURL(/\/finance\/accounting\/journals/);await page.locator('.journal-detail-title').waitFor({timeout:30000});
 const sourceUrl=page.url();check(new URL(sourceUrl).searchParams.get('financeReturnUrl')===new URL(reportUrl).pathname+new URL(reportUrl).search,'Journal lost exact report return context');
 await page.goto(reportUrl);await page.getByRole('button',{name:'Inspect sources',exact:true}).first().waitFor();
 await ready(page,'.planning-evidence');await page.screenshot({path:out+'variance-desktop.png',fullPage:true});await page.setViewportSize({width:390,height:844});await page.screenshot({path:out+'variance-mobile.png',fullPage:true});check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Variance body overflows narrow viewport');await page.setViewportSize({width:1440,height:1000});
 await page.getByRole('link',{name:'Rolling forecast',exact:true}).click();await page.getByRole('heading',{name:'Rolling forecast',exact:true}).waitFor();
 await page.getByLabel('Named version',{exact:true}).fill('Browser recorded outlook');await page.getByLabel('Explanation',{exact:true}).fill('Browser explicit revenue assumptions; cash timing remains excluded.');
 await page.getByLabel('Actuals through month',{exact:true}).fill('2026-10');await page.locator('.assumption-row').getByLabel('Month',{exact:true}).fill('2026-11');
 await page.locator('.assumption-row').getByLabel('Account').selectOption(account);await page.locator('.assumption-row').getByLabel('Amount',{exact:true}).fill('-1250.25');await page.locator('.assumption-row').getByLabel('Rationale',{exact:true}).fill('November signed revenue assumption');
 await page.getByRole('button',{name:'Preview assumptions',exact:true}).click();await page.getByRole('heading',{name:'Deterministic preview',exact:true}).waitFor();check((await page.locator('.preview-panel').innerText()).includes('-1,250.25'),'Initial preview differs');
 await page.locator('.assumption-row').getByLabel('Amount',{exact:true}).fill('-1400.25');await page.getByRole('button',{name:'Preview assumptions',exact:true}).click();await page.locator('.preview-panel').getByText('-1,400.25',{exact:true}).waitFor();check((await page.locator('.preview-panel').innerText()).includes('-1,400.25'),'Changed assumption did not produce deterministic preview');
 await page.getByRole('button',{name:'Save named version',exact:true}).click();await page.getByRole('button',{name:'Create a new revision',exact:true}).waitFor({timeout:30000});
 const firstUrl=page.url(),first=new URL(firstUrl).searchParams.get('version');check(first,'Saved version has no reopen identity');await page.reload();await page.getByRole('button',{name:'Create a new revision',exact:true}).waitFor();check((await page.locator('.preview-panel').innerText()).includes('-1,400.25'),'Reopened values changed');
 await ready(page,'.preview-panel');await page.screenshot({path:out+'forecast-desktop.png',fullPage:true});await page.setViewportSize({width:390,height:844});await page.screenshot({path:out+'forecast-mobile.png',fullPage:true});check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Forecast body overflows narrow viewport');await page.setViewportSize({width:1440,height:1000});
 await page.getByRole('button',{name:'Create a new revision',exact:true}).click();await page.getByLabel('Named version',{exact:true}).fill('Browser later outlook');await page.locator('.assumption-row').getByLabel('Amount',{exact:true}).fill('-1500.25');await page.getByRole('button',{name:'Preview assumptions',exact:true}).click();await page.getByRole('heading',{name:'Deterministic preview',exact:true}).waitFor();await page.getByRole('button',{name:'Save named version',exact:true}).click();await page.getByRole('button',{name:'Create a new revision',exact:true}).waitFor();const later=new URL(page.url()).searchParams.get('version');check(later&&later!==first,'Successor identity missing');
 await page.goto(`${web}/finance/reports/forecast-comparison?companyId=${company}&earlier=${first}&later=${later}&currency=SEK`);await page.getByRole('heading',{name:'Monthly account values',exact:true}).waitFor();check((await page.locator('.comparison-values').innerText()).includes('-100.00'),'Comparison change differs');
 await ready(page,'.comparison-values');await page.screenshot({path:out+'comparison-desktop.png',fullPage:true});await page.setViewportSize({width:390,height:844});await page.screenshot({path:out+'comparison-mobile.png',fullPage:true});check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Comparison body overflows narrow viewport');await page.setViewportSize({width:1440,height:1000});
 const current=await json(root+'/analysis?'+query);const original=await json(root+'/versions/'+first);check(original.preview.values.find(v=>v.monthUtc.startsWith('2026-09')).actual===-1000.10,'Closed actual changed');check(current.rows.find(v=>v.monthUtc.startsWith('2026-09')).actual===-1000.10,'Current closed actual changed');
 const csv=await json(root+'/export?'+query);check(csv.fingerprint===current.fingerprint&&csv.content.includes('"99.90"'),'Variance export differs');const ccsv=await json(root+`/compare/export?earlier=${first}&later=${later}&currency=SEK`);check(ccsv.content.includes('"-100.00"'),'Comparison export differs');
 await page.goto(`${web}/dashboard?companyId=${company}&period=month&lens=finance&year=2026&month=9`);await page.getByRole('button',{name:'Save current review',exact:true}).waitFor({timeout:30000});await page.getByRole('button',{name:'Save current review',exact:true}).click();await page.getByRole('button',{name:'Refresh as new revision',exact:true}).waitFor();const monthlyUrl=page.url(),snapshot=new URL(monthlyUrl).searchParams.get('snapshot');check(snapshot,'Monthly snapshot identity missing');await page.reload();await page.getByRole('button',{name:'Refresh as new revision',exact:true}).waitFor();await page.getByRole('link',{name:'Finance variance and rolling forecasts',exact:true}).click();await page.getByText('Original monthly review evidence; source links open current records.',{exact:false}).waitFor();check(!await page.getByRole('button',{name:'Refresh and download CSV',exact:true}).count(),'Retained snapshot exports live rows');await page.screenshot({path:out+'monthly-retained-variance.png',fullPage:true});
 await page.keyboard.press('Tab');check(await page.evaluate(()=>document.activeElement!==document.body),'Keyboard focus unavailable');check(errors.length===0,errors.join('\n'));
 results.push({id:'F23-01',passed:true,reportUrl,sourceUrl,variance:99.90,noteSaved:true},{id:'F23-02',passed:true,first,later,reopenedForecast:-1400.25,closedActual:-1000.10},{id:'F23-03',passed:true,comparisonChange:-100,exportsReconcile:true},{id:'F23-04',passed:true,monthlyUrl,snapshot,originalReportRetained:true});
 await writeFile(out+'browser-accepted.json',JSON.stringify({time:new Date().toISOString(),profile,results,errors,viewports:[[1440,1000],[390,844]],physicalDownloadObserved:false,providerAcceptance:false,humanApproval:'Pending'},null,2));
 console.log(JSON.stringify({passed:results.length,errors:errors.length,first,later,snapshot}));
}finally{await browser.close();}

