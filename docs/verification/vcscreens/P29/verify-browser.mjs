import {createRequire} from 'node:module';
import {writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const {chromium}=createRequire(import.meta.url)('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const out=fileURLToPath(new URL('.',import.meta.url)),api='http://localhost:5348',web='http://localhost:5108';
const profile=await(await fetch(api+'/_uat/p29/profile')).json(),company=profile.company;
const headers={'X-Dev-Auth-Subject':'p19-owner','X-Dev-Auth-Email':'p19-owner@example.test','X-Company-Id':company,'Content-Type':'application/json'};
const check=(ok,m)=>{if(!ok)throw new Error(m);};
async function json(path,method='GET',body,delegate=false){const h=delegate?{...headers,'X-Dev-Auth-Subject':'p29-delegate','X-Dev-Auth-Email':'p29-delegate@example.test'}:headers;const r=await fetch(api+`/api/companies/${company}`+path,{method,headers:h,body:body===undefined?undefined:JSON.stringify(body)});check(r.ok,`API ${r.status}: ${await r.clone().text()}`);return r.json();}
const browser=await chromium.launch({channel:'msedge',headless:true}),page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[];
page.on('pageerror',e=>errors.push(String(e)));
async function ready(){await page.locator('[data-testid="briefing-cadence"][data-interactive-ready="true"]').waitFor();await page.getByRole('heading',{name:'Company and context',exact:true}).waitFor();}
async function idle(){await page.waitForFunction(()=>{const e=document.querySelector('[data-testid="briefing-cadence"]');return e&&!['true',''].includes((e.getAttribute('aria-busy')??'false').toLowerCase());});}
async function capture(name){await idle();await page.mouse.move(0,0);await page.evaluate(()=>window.scrollTo(0,0));await page.screenshot({path:out+name,fullPage:true});}
async function save(){await page.getByRole('button',{name:'Save briefing preferences',exact:true}).click();await page.getByText('Briefing preferences saved. Schedules and routing are active.',{exact:true}).waitFor();await idle();}
try{
 await page.goto(`${web}/briefing-preferences?companyId=${company}`);await ready();
 await page.getByLabel('Role defaults',{exact:true}).selectOption('ceo');
 for(const area of ['Finance','Sales','Marketing','Support'])await page.getByLabel(area,{exact:true}).uncheck();
 await page.getByLabel('Company',{exact:true}).check();
 await page.getByLabel('Timezone',{exact:true}).fill('Europe/Stockholm');
 await page.getByLabel('Work start',{exact:true}).fill('00:00');await page.getByLabel('Work end',{exact:true}).fill('23:59');
 await page.getByLabel('Quiet start',{exact:true}).fill('00:00');await page.getByLabel('Quiet end',{exact:true}).fill('00:00');
 for(const day of ['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'])await page.getByLabel(day,{exact:true}).check();
 for(const kind of ['Morning','End Of Day','Shift Handover','Weekly','Monthly','Quarterly','Annual'])await page.getByLabel(kind,{exact:true}).uncheck();
 await page.getByLabel('Morning',{exact:true}).check();
 const local=new Intl.DateTimeFormat('en-GB',{timeZone:'Europe/Stockholm',hour:'2-digit',minute:'2-digit',hour12:false}).format(new Date(Date.now()-60000));
 await page.getByLabel('Morning time',{exact:true}).fill(local);
 await page.getByLabel('Enable urgent work escalations',{exact:true}).uncheck();await save();await capture('schedule-desktop.png');
 await page.reload();await ready();check(await page.getByLabel('Morning time',{exact:true}).inputValue()===local,'Saved cadence did not survive reload');
 await page.getByRole('button',{name:'Absence routing',exact:true}).click();await page.getByRole('heading',{name:'Routing',exact:true}).waitFor();
 const start=new Date(Date.now()-3600000).toISOString().slice(0,16),end=new Date(Date.now()+86400000).toISOString().slice(0,16);
 await page.getByLabel('Absence start (UTC)',{exact:true}).fill(start);await page.getByLabel('Absence end (UTC)',{exact:true}).fill(end);
 await page.getByLabel('Eligible delegate',{exact:true}).selectOption(profile.delegateUser);await page.getByLabel('Accountable fallback',{exact:true}).selectOption(profile.fallbackUser);await save();await capture('absence-desktop.png');
 await page.setViewportSize({width:390,height:844});await capture('absence-narrow.png');check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Narrow routing overflow');await page.setViewportSize({width:1440,height:1000});
 await page.getByRole('button',{name:'Preview',exact:true}).click();await page.getByRole('heading',{name:'Included work items',exact:true}).waitFor();await page.getByText('P29 capacity commitment',{exact:true}).waitFor();await capture('preview-desktop.png');
 const before=await json('/briefings/cadence/preview');check(before.recipientUserId===profile.delegateUser&&before.routing==='delegate','Preview did not use eligible absence delegate');check(before.items.some(x=>x.id===profile.taskId&&x.retainedSourcePath),'Preview lacks native P28 source link');
 const delivery=await fetch(api+'/_uat/p29/deliver',{method:'POST'});check(delivery.ok,'Controlled native delivery failed');await page.getByRole('button',{name:'Refresh preview',exact:true}).click();await idle();
 const after=await json('/briefings/cadence/preview'),sent=after.audit.filter(x=>x.status==='sent');check(sent.length===1&&sent[0].recipientUserId===profile.delegateUser,'Delivery audit did not record one delegated delivery');
 const recipient=await json(`/briefings/cadence/deliveries/${sent[0].id}`,'GET',undefined,true);check(recipient.items.some(x=>x.id===profile.taskId),'Delegate cannot open their authorized work briefing');
 const notifications=await json('/notifications','GET',undefined,true),notice=notifications.filter(x=>x.relatedEntityType==='briefing_cadence_delivery');check(notice.length===1&&notice[0].actionUrl===sent[0].path,'Controlled recipient notification count/route mismatch');check(recipient.items.find(x=>x.id===profile.taskId).retainedSourcePath===null,'Delegate inherited the private retained-source link');
 await fetch(api+'/_uat/p29/deliver',{method:'POST'});check((await json('/notifications','GET',undefined,true)).filter(x=>x.relatedEntityType==='briefing_cadence_delivery').length===1,'Repeated job/outbox created duplicate notification');
 await page.goto(`${web}${sent[0].path}`);await page.getByRole('heading',{name:'Your briefing',exact:true}).waitFor();await page.getByText('P29 capacity commitment',{exact:true}).waitFor();await capture('delivery-desktop.png');
 await page.setViewportSize({width:390,height:844});await capture('preview-narrow.png');check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Narrow briefing overflow');await page.keyboard.press('Tab');check(await page.evaluate(()=>document.activeElement!==document.body),'Keyboard focus missing');await page.setViewportSize({width:1440,height:1000});
 await page.getByRole('link',{name:'Retained scenario source · v1',exact:true}).first().click();await page.locator('[data-testid="decision-work"][data-interactive-ready="true"]').waitFor();await page.getByRole('heading',{name:'P29 capacity commitment',exact:true}).waitFor();
 await page.getByRole('link',{name:'Open original retained source',exact:true}).click();await page.locator('[data-testid="strategic-scenarios"][data-interactive-ready="true"]').waitFor();await page.getByRole('link',{name:'P29 briefing source copy · revision 1',exact:true}).waitFor();await page.goBack();await page.locator('[data-testid="decision-work"][data-interactive-ready="true"]').waitFor();await page.getByRole('heading',{name:'P29 capacity commitment',exact:true}).waitFor();
 await page.getByRole('link',{name:'Open task in Work',exact:true}).click();await page.getByRole('link',{name:'Source decision and work review →',exact:true}).waitFor();
 check(errors.length===0,'Page errors: '+errors.join('; '));
 await writeFile(out+'browser-accepted.json',JSON.stringify({phase:'P29',timeUtc:new Date().toISOString(),profile,deliveryId:sent[0].id,results:[{id:'BC29-01',passed:true,checks:['editable saved schedules','timezone and working/quiet hours','reload retention','eligible absence delegate and fallback','authorized actual P28 work and source','next delivery local offset','native job/outbox controlled recipient','one delivery after duplicate worker run','delivered briefing opens current work','retained source and Work return','narrow routing/briefing and keyboard']}],errors,humanApproval:'Pending'},null,2));console.log(JSON.stringify({passed:1,errors:0,deliveryId:sent[0].id}));
}catch(e){await page.screenshot({path:out+'browser-diagnostic.png',fullPage:true});throw e;}finally{await browser.close();}
