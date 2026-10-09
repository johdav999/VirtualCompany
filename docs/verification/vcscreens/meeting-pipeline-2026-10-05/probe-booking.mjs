import {createRequire} from 'node:module';
import {writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const {chromium}=createRequire(import.meta.url)('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const mode=process.argv[2]??'before';const out=fileURLToPath(new URL('.',import.meta.url));
const base='http://localhost:5062',company='43e6a825-d1b7-429a-8608-7e668087d005',lead='efd8b4d2-da9d-438e-990e-d2ac719bf608';
const origin=`/dashboard?companyId=${company}&lens=sales`;
const path=route=>`${route}?companyId=${company}&returnUrl=${encodeURIComponent(origin)}`;
const browser=await chromium.launch({channel:'msedge',headless:true});
try{
 const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 await page.goto(base+path('/app/sales/leads/'+lead));await page.waitForTimeout(1600);
 await page.locator('.sales-meeting-status__row').first().waitFor();
 const bookingText=await page.locator('.sales-meeting-status__row').first().innerText();
 const bookingLinks=await page.locator('.sales-meeting-status__row a').evaluateAll(a=>a.map(x=>({text:x.innerText,href:x.getAttribute('href')})));
 await page.locator('.sales-meeting-status__row').first().screenshot({path:out+mode+'-lead-booking.png'});
 await page.goto(base+path('/app/sales/pipeline'));await page.waitForTimeout(1600);
 const card=page.locator('a.sales-deal-card').filter({hasText:'buying interest from farcooperation@gmail.com'}).first();await card.waitFor();
 const href=await card.getAttribute('href'),cardText=await card.innerText();
 const cardMeetingId=mode==='after'?await card.locator('[data-invitation-id]').getAttribute('data-invitation-id'):null;
 await card.screenshot({path:out+mode+'-pipeline-card.png'});
 if(mode==='after'&&!await card.locator('[data-testid=pipeline-meeting]').count())throw Error('Pipeline has no booking summary');
 await card.click();await page.waitForURL(base+href);await page.waitForTimeout(1600);
 const sourceHref=await page.locator(`a[href*="/app/sales/leads/${lead}"]`).first().getAttribute('href');
 const meeting=page.locator('[data-testid=deal-meetings]');let details=null;
 if(mode==='after'){
  await meeting.waitFor();details={text:await meeting.innerText(),invitations:await meeting.locator('[data-invitation-id]').evaluateAll(a=>a.map(x=>x.dataset.invitationId)),links:await meeting.locator('a').evaluateAll(a=>a.map(x=>({text:x.innerText,href:x.getAttribute('href')})))};
  if(!details.text.includes('Virtual Company demo'))throw Error('Actual lead booking missing');
  const leadMeetingIds=bookingLinks.filter(x=>x.href.includes('/meeting-invitations/')).map(x=>x.href.match(/meeting-invitations\/([^/]+)/)[1]);
  if(!details.invitations.includes('97f0c875-61e1-468f-a61b-cee129fd8c74')||!details.invitations.includes(cardMeetingId)||!leadMeetingIds.every(id=>details.invitations.includes(id)))throw Error('Pipeline and detail do not match the existing lead invitations');
  for(const link of details.links){const q=new URL(link.href,base).searchParams;if(q.get('companyId')!==company||q.get('returnUrl')!==origin||q.get('recordReturnUrl')!==href)throw Error('Booking link lost originating record context');}
  await meeting.screenshot({path:out+'after-deal-booking.png'});await page.screenshot({path:out+'after-deal-desktop.png'});
  const review=meeting.getByRole('link',{name:'Review booking'}).first();await review.click();await page.waitForTimeout(1600);await page.locator('.sales-meeting-status__row').first().waitFor();
  if(await page.locator('.sales-meeting-status__row').first().innerText()!==bookingText)throw Error('Existing booking changed during read-only verification');
  const back=page.locator(`a[href="${href}"]`).first();await back.waitFor();await back.click();await page.waitForURL(base+href);await page.waitForTimeout(1600);
  await page.setViewportSize({width:390,height:844});await page.waitForTimeout(300);await meeting.scrollIntoViewIfNeeded();await page.screenshot({path:out+'after-deal-mobile.png'});
  details.mobileOverflow=await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth}));
  if(details.mobileOverflow.scroll>details.mobileOverflow.width)throw Error('Mobile horizontal overflow');
 }
 await writeFile(out+mode+'-browser.json',JSON.stringify({mode,company,lead,href,sourceHref,bookingText,bookingLinks,cardText,cardMeetingId,details,errors},null,2));
 if(errors.length)throw Error(errors.join('; '));console.log(JSON.stringify({mode,href,cardText,details}));
}finally{await browser.close();}
