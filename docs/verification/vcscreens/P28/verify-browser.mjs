import {createRequire} from 'node:module';
import {writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const {chromium}=createRequire(import.meta.url)('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const out=fileURLToPath(new URL('.',import.meta.url)),api='http://localhost:5348',web='http://localhost:5108';
const profile=await(await fetch(api+'/_uat/p28/profile')).json(),company=profile.company;
const planning=profile.planning,annual=planning.annual,quarter=annual.quarter;
const headers={'X-Dev-Auth-Subject':'p19-owner','X-Dev-Auth-Email':'p19-owner@example.test','X-Dev-Auth-DisplayName':'Alice Admin','X-Company-Id':company,'Content-Type':'application/json'};
const check=(ok,message)=>{if(!ok)throw new Error(message);};
async function json(path,method='GET',body){const r=await fetch(api+`/api/companies/${company}`+path,{method,headers,body:body===undefined?undefined:JSON.stringify(body)});check(r.ok,`API ${r.status}: ${await r.clone().text()}`);return r.json();}
const browser=await chromium.launch({channel:'msedge',headless:true}),page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[];
page.on('pageerror',e=>errors.push(String(e)));
async function ready(){await page.locator('[data-testid="decision-work"][data-interactive-ready="true"]').waitFor();await page.locator('[data-testid="workspace-company-switcher"] select').waitFor();}
async function capture(name){await page.waitForFunction(()=>{const e=document.querySelector('[data-testid="decision-work"]');return !e||!['true',''].includes((e.getAttribute('aria-busy')??'false').toLowerCase());});await page.mouse.move(0,0);await page.evaluate(()=>window.scrollTo(0,0));await page.screenshot({path:out+name,fullPage:true});}
try{
 await page.goto(`${web}/dashboard?companyId=${company}&period=multiyear&scenario=${profile.scenarioId}`);
 await page.locator('[data-testid="strategic-scenarios"][data-interactive-ready="true"]').waitFor();
 await page.getByRole('link',{name:'Create work: Capacity review',exact:true}).click();await ready();await page.getByRole('heading',{name:'Work details',exact:true}).waitFor();
 await page.getByLabel('Work objective',{exact:true}).fill('Browser capacity follow-up');await page.getByLabel('Work accountable owner',{exact:true}).selectOption(quarter.company.owner);
 await page.getByLabel('Work due date',{exact:true}).fill('2026-11-10T09:00');await page.getByLabel('Acceptance outcome',{exact:true}).fill('Retain a reviewed capacity recommendation with original scenario evidence.');
 await page.getByLabel('Proposed constraints',{exact:true}).fill('Internal human follow-up only; no spend, publication or customer contact.');
 await page.getByRole('button',{name:'Preview work',exact:true}).click();await page.locator('[data-testid="work-preview"]').waitFor();await capture('create-work-desktop.png');
 await page.setViewportSize({width:390,height:844});await capture('create-work-narrow.png');check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Narrow creation spills');await page.keyboard.press('Tab');check(await page.evaluate(()=>document.activeElement!==document.body),'Creation keyboard focus missing');await page.setViewportSize({width:1440,height:1000});
 await page.getByRole('button',{name:'Create work',exact:true}).click();await page.waitForURL(u=>u.pathname==='/work/source'&&u.searchParams.has('taskId'));await ready();await page.getByRole('heading',{name:'Browser capacity follow-up',exact:true}).waitFor();
 const taskId=new URL(page.url()).searchParams.get('taskId'),sourceUrl=page.url(),created=await json(`/decision-work/tasks/${taskId}/open`,'POST');
 check(created.created.input.dueUtc==='2026-11-10T08:00:00Z','Company local due time did not become UTC');check(created.created.source.reference.versionId===profile.scenarioId,'Native source version mismatch');
 await page.getByRole('button',{name:'Submit for work review',exact:true}).click();await page.getByText('Waiting for human review',{exact:true}).waitFor();await capture('source-decision-desktop.png');
 const reviewed=await json(`/decision-work/tasks/${taskId}/open`,'POST');check(reviewed.approvalId&&reviewed.status==='awaiting_approval','Canonical pending review missing');
 const forbidden=await fetch(api+`/api/companies/${company}/tasks/${taskId}/status`,{method:'PATCH',headers,body:JSON.stringify({status:'completed',outputPayload:null,rationaleSummary:'Attempted unreviewed completion',confidenceScore:null})});check(forbidden.status===403,'Required review bypassed');
 await page.getByRole('link',{name:'Open task in Work',exact:true}).click();await page.getByRole('link',{name:'Source decision and work review →',exact:true}).waitFor();check(await page.getByRole('button',{name:'Complete task',exact:true}).count()===0,'Pending work exposes completion');
 await page.getByRole('link',{name:'Source decision and work review →',exact:true}).click();await ready();await page.getByRole('link',{name:'Open canonical work review',exact:true}).click();
 await page.getByRole('heading',{name:'Browser capacity follow-up',exact:true}).waitFor();await page.getByRole('button',{name:'Approve',exact:true}).waitFor();await page.getByRole('columnheader',{name:'Field',exact:true}).waitFor();check((await page.locator('body').innerText()).includes('Acceptance outcome'),'Canonical review omits the outcome');check(!(await page.locator('body').innerText()).includes('exceeded a configured approval threshold'),'Work review incorrectly claims a threshold failure');await capture('canonical-work-review-desktop.png');
 await page.getByRole('button',{name:'Approve',exact:true}).click();await page.getByText('Decision recorded. The owning workflow determines the next action.',{exact:true}).waitFor();
 const approved=await json(`/decision-work/tasks/${taskId}/open`,'POST');check(approved.status==='in_progress','Canonical decision did not start human work');
 await page.goto(sourceUrl);await ready();await page.getByRole('heading',{name:'Browser capacity follow-up',exact:true}).waitFor();
 await page.setViewportSize({width:390,height:844});await capture('source-decision-narrow.png');check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Narrow source detail spills');await page.keyboard.press('Tab');check(await page.evaluate(()=>document.activeElement!==document.body),'Keyboard focus missing');
 await page.setViewportSize({width:1440,height:1000});await page.getByRole('link',{name:'Open original retained source',exact:true}).click();await page.getByRole('link',{name:'Create work: Capacity review',exact:true}).click();await ready();await page.getByRole('heading',{name:'Work created from this review',exact:true}).waitFor();await page.getByRole('link',{name:'Browser capacity follow-up · In progress',exact:true}).waitFor();check(await page.getByRole('button',{name:'Create work',exact:true}).count()===0,'Duplicate creation exposed');
 const entries=[{period:'month',params:`lens=customers&year=2026&month=9&snapshot=${quarter.snapshots[2]}`,name:/Create work:.*volume/i},
 {period:'quarter',params:`year=2026&quarter=3&review=${annual.reviewId}`,name:'Create work'},
 {period:'year',params:`year=2026&plan=${planning.planId}`,name:'Create work from annual decision'}];
 for(const e of entries){await page.goto(`${web}/dashboard?companyId=${company}&period=${e.period}&${e.params}`);await page.getByRole('link',{name:e.name,exact:typeof e.name==='string'}).first().click();await ready();await page.getByRole('heading',{name:'Work details',exact:true}).waitFor();await page.getByRole('link',{name:'Open original retained source',exact:true}).click();await page.getByRole('link',{name:e.name,exact:typeof e.name==='string'}).first().click();await ready();await page.getByRole('heading',{name:'Work details',exact:true}).waitFor();}
 check(errors.length===0,'Page errors: '+errors.join('; '));
 await writeFile(out+'browser-accepted.json',JSON.stringify({phase:'P28',timeUtc:new Date().toISOString(),profile,results:[{id:'DW28-01',passed:true,taskId,sourceUrl,checks:['all four native source entry points','company-local due date','preview and confirmation','retained source evidence','Work to source navigation','canonical review submission waits','unreviewed completion denied','canonical approval starts human work','source-to-existing-work no duplicate form','narrow and keyboard']}],errors,humanApproval:'Pending'},null,2));console.log(JSON.stringify({passed:1,errors:0,taskId}));
}catch(e){await capture('browser-diagnostic.png');throw e;}finally{await browser.close();}
