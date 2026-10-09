import {createRequire} from 'node:module';
import {writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const require=createRequire(import.meta.url);
const {chromium}=require('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const out=fileURLToPath(new URL('.',import.meta.url)),api='http://localhost:5345',web='http://localhost:5105';
const profile=await(await fetch(api+'/_uat/p25/profile')).json(),company=profile.company.company;
const headers={'X-Dev-Auth-Subject':'p19-owner','X-Dev-Auth-Email':'p19-owner@example.test','X-Company-Id':company};
const root=api+`/api/companies/${company}/planning/quarters`;
const browser=await chromium.launch({channel:'msedge',headless:true}),errors=[],results=[];
const check=(x,m)=>{if(!x)throw new Error(m);};
async function json(path,method='GET',body){const r=await fetch(root+path,{method,headers:{...headers,'Content-Type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});check(r.ok,`API ${r.status}: ${await r.clone().text()}`);return r.json();}
const page=await browser.newPage({viewport:{width:1440,height:1000}});page.on('pageerror',e=>errors.push(String(e)));
try{
 await page.goto(`${web}/dashboard?companyId=${company}&period=quarter&year=2026&quarter=3&lens=company`);
 await page.getByRole('heading',{name:'No reviewed objectives yet',exact:true}).waitFor({timeout:30000});
 await page.getByRole('link',{name:'Prepare objectives and resources',exact:true}).click();
 await page.locator('[data-testid="quarter-planning"][data-interactive-ready="true"]').waitFor();await page.getByLabel('Add company objective',{exact:true}).selectOption(profile.goal);await page.getByRole('button',{name:'Add objective',exact:true}).click();
 await page.getByLabel('Quarter accountable owner',{exact:true}).selectOption(profile.company.owner);
 await page.getByLabel('Quarter target (cases)',{exact:true}).fill('100');
 for(const id of profile.snapshots){await page.getByLabel('Monthly snapshot',{exact:true}).selectOption(id);await page.getByRole('button',{name:'Link measure',exact:true}).click();}
 await page.getByRole('button',{name:'Add milestone',exact:true}).click();await page.getByLabel('Milestone title',{exact:true}).fill('Browser capacity checkpoint');await page.getByLabel('Due date (Europe/Stockholm)',{exact:true}).fill('2026-09-20');
 await page.getByLabel('Quarter handling review · proposed · 1 dependencies',{exact:true}).check();
 await page.getByRole('button',{name:'Add resource allocation',exact:true}).click();await page.getByLabel('Pool',{exact:true}).fill('Support');await page.getByLabel('Available hours',{exact:true}).fill('20');await page.getByLabel('Proposed hours',{exact:true}).fill('30');await page.getByLabel('Supporting monthly snapshot',{exact:true}).selectOption(profile.snapshots[2]);await page.getByLabel('Resource rationale',{exact:true}).fill('Explicit browser-reviewed quarter handling assumptions');
 await page.getByLabel('Outlook and decision notes',{exact:true}).fill('Browser reviewed quarterly capacity decision');await page.getByRole('button',{name:'Preview objectives and conflicts',exact:true}).click();
 await page.getByText('Proposed commitments exceed this pool', {exact:false}).waitFor();await page.screenshot({path:out+'quarter-detail-desktop.png',fullPage:true});
 check(await page.getByText('Missed target',{exact:true}).count()===1,'Quarter outcome missing');
 await page.getByRole('button',{name:'Save reviewed revision',exact:true}).click();await page.waitForURL(u=>u.pathname==='/dashboard'&&u.searchParams.has('review'));const firstUrl=page.url(),firstId=new URL(firstUrl).searchParams.get('review');
 await page.reload();await page.getByText('Browser capacity checkpoint',{exact:false}).waitFor();await page.screenshot({path:out+'quarter-overview-desktop.png',fullPage:true});
 const retained=await json(`/reviews/${firstId}/open`,'POST');check(retained.review.objectives[0].actual===profile.actual,'Quarter does not reconcile to saved monthly measures');check(retained.review.conflicts[0].shortfall===10,'Resource shortfall differs');
 await page.getByRole('link',{name:'Propose a new revision',exact:true}).click();await page.locator('[data-testid="quarter-planning"][data-interactive-ready="true"]').waitFor();await page.getByLabel('Quarter accountable owner',{exact:true}).selectOption(profile.company.manager);await page.getByLabel('Quarter target (cases)',{exact:true}).fill('90');await page.getByLabel('Milestone title',{exact:true}).fill('Revised browser checkpoint');await page.getByLabel('Proposed hours',{exact:true}).fill('15');
 await page.getByRole('button',{name:'Preview objectives and conflicts',exact:true}).click();await page.getByRole('button',{name:'Save reviewed revision',exact:true}).click();await page.waitForURL(u=>u.pathname==='/dashboard'&&u.searchParams.has('review')&&u.searchParams.get('review')!==firstId);const secondUrl=page.url();
 await page.reload();await page.getByText('Revised browser checkpoint',{exact:false}).waitFor();const revised=await json(`/reviews/${new URL(secondUrl).searchParams.get('review')}/open`,'POST');await page.getByText(`owner ${retained.review.objectives[0].owner} → ${revised.review.objectives[0].owner}.`,{exact:false}).waitFor();
 await page.setViewportSize({width:390,height:844});await page.screenshot({path:out+'quarter-overview-narrow.png',fullPage:true});check(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth+1),'Page spills horizontally');await page.keyboard.press('Tab');check(await page.evaluate(()=>document.activeElement!==document.body),'Keyboard focus missing');
 await page.goto(firstUrl);await page.getByText('Browser capacity checkpoint',{exact:false}).waitFor();check(await page.getByText('Revised browser checkpoint',{exact:false}).count()===0,'Original revision overwritten');
 const old=await json(`/reviews/${firstId}/open`,'POST');check(old.review.objectives[0].objective.target===100&&old.review.objectives[0].objective.ownerUserId===profile.company.owner,'Original owner/target changed');
 results.push({id:'QP25-01',passed:true,firstUrl,secondUrl,actual:profile.actual,shortfall:10,ownerMilestoneRevision:true,originalReadable:true});
 check(errors.length===0,errors.join('\n'));await writeFile(out+'browser-accepted.json',JSON.stringify({time:new Date().toISOString(),profile,results,errors,viewports:[[1440,1000],[390,844]],mode:'Fresh disposable headless Edge with real Web and composed fixture API',humanApproval:'Pending'},null,2));console.log(JSON.stringify({passed:results.length,errors:errors.length,firstId}));
}catch(e){console.log(String(e));console.log(await page.locator('body').innerText());await page.screenshot({path:out+'browser-diagnostic.png',fullPage:true});throw e;}finally{await browser.close();}
