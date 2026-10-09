import {createRequire} from 'node:module';
import {writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const {chromium}=createRequire(import.meta.url)('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const out=fileURLToPath(new URL('.',import.meta.url)),base='http://localhost:5062',company='43e6a825-d1b7-429a-8608-7e668087d005',mode=process.argv[2]??'after';
const origin=`/dashboard?companyId=${company}&lens=company`;
const browser=await chromium.launch({channel:'msedge',headless:true}),page=await browser.newPage({viewport:{width:1857,height:837}}),results=[],errors=[];
page.setDefaultTimeout(30000);page.on('pageerror',e=>errors.push(String(e)));
const check=(ok,message)=>{if(!ok)throw new Error(message);};
async function overview(){await page.goto(base+origin);await page.locator('[data-department="sales"] .company-summary__indicators').waitFor();await page.waitForTimeout(600);}
const cases=[
 ['sales','Pipeline','/app/sales/pipeline'],
 ['sales','Deals needing attention','/app/sales#sales-deals-attention'],
 ['sales','Hot leads','/app/sales/prospects?view=leads'],
 ['marketing','Launches due','/marketing?section=Calendar'],
 ['marketing','Content due','/marketing?section=Content'],
 ['marketing','Spend exceptions','/marketing/reports/spend'],
 ['marketing','Attribution gaps','/marketing?section=Performance'],
 ['customers','Open cases','/support?view=open'],
 ['customers','SLA breached','/support?view=breached'],
 ['customers','SLA at risk','/support?view=sla-risk'],
 ['customers','Waiting cases','/support/reports?view=backlog'],
 ['customers','Awaiting approval','/support?view=approvals']
];
try{
 await overview();
 const departments=page.locator('.company-summary__departments'),briefings=page.locator('[data-testid="today-agent-briefings"]');
 await departments.screenshot({path:out+`${mode}-departments.png`});await briefings.screenshot({path:out+`${mode}-briefings.png`});
 const initial={links:await departments.locator('.company-summary__indicators a').count(),cards:await briefings.locator('.today-agent-update').count(),text:await briefings.innerText()};
 if(mode==='before'){results.push(initial);}else{
  check(initial.links===15,'Expected 15 links across all four departments');
  check(!initial.text.includes('The agent run state changed'),'Generic run fallback remains');
  check(initial.text.includes('The AI result cited evidence that was not supplied.'),'Persisted failure missing');
  check(initial.text.includes('Sales operating cadence'),'Saved Sales review hidden');
  check(await briefings.locator('.today-agent-update[data-state="recommended"]').count()>0,'Business review remains hidden behind blockers');
  results.push({flow:'briefings',visibleCards:initial.cards,persistedFailure:true,savedBusinessReviews:true,passed:true});
  for(const [lens,label,wantedPath] of (mode==='after'?cases:[])){
   await overview();
   const link=page.locator(`[data-department="${lens}"] .company-summary__indicators`).getByRole('link',{name:label,exact:true});
   const href=await link.getAttribute('href'),url=new URL(href,base),wanted=new URL(wantedPath,base);
   check(url.pathname===wanted.pathname&&url.hash===wanted.hash,`${label}: wrong target`);
   for(const [k,v] of wanted.searchParams)check(url.searchParams.get(k)===v,`${label}: filter ${k} lost`);
   check(url.searchParams.get('companyId')===company,`${label}: company lost`);check(url.searchParams.get('returnUrl')===origin,`${label}: return lost`);
   await link.focus();await link.press('Enter');await page.waitForURL(u=>u.pathname===wanted.pathname);await page.waitForTimeout(1000);
   check(!(await page.getByText("Sorry, there's nothing at this address.",{exact:true}).count()),`${label}: missing screen`);
   const headings=await page.locator('h1').allTextContents();check(headings.length>0,`${label}: destination title missing`);
   if(wanted.hash)check(await page.locator(wanted.hash).count()===1,'Sales attention section missing');
   await page.screenshot({path:out+`destination-${lens}-${label.toLowerCase().replaceAll(' ','-')}.png`,fullPage:true});
   const back=page.locator(`a[href="${origin}"]`).filter({hasText:/Overview/i}).first();
   check(await back.count()>0,`${label}: exact Overview return missing`);await back.click();await page.waitForURL(u=>u.pathname==='/dashboard'&&u.searchParams.get('lens')==='company');
   await page.locator('[data-department="sales"] .company-summary__indicators').waitFor();await page.waitForTimeout(700);
   results.push({lens,label,href,headings,keyboardNavigation:true,overviewReturn:true,passed:true});
  }
  await overview();await page.setViewportSize({width:390,height:844});await departments.screenshot({path:out+'after-departments-narrow.png'});
  const toggle=briefings.getByRole('button',{name:'Agent briefings',exact:true});
  await toggle.click();await briefings.locator('button[aria-expanded="true"]').waitFor();
  check(await briefings.locator('.today-agent-update').last().isVisible(),'Narrow business review hidden');
  await briefings.screenshot({path:out+'after-briefings-narrow.png'});
  check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Narrow dashboard overflow');
  check(errors.length===0,errors.join('; '));
 }
 await writeFile(out+`${mode}-browser.json`,JSON.stringify({timeUtc:new Date().toISOString(),mode,company,environment:'Existing local company; read-only headless Edge navigation',results,errors},null,2));console.log(JSON.stringify({mode,checks:results.length,errors}));
}catch(e){await page.screenshot({path:out+`${mode}-diagnostic.png`,fullPage:true});throw e;}finally{await browser.close();}
