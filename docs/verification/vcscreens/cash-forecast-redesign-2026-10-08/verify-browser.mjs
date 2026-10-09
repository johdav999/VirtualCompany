import {createRequire} from 'node:module';
import {writeFile,readFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const {chromium}=createRequire(import.meta.url)('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const out=fileURLToPath(new URL('.',import.meta.url)),base='http://localhost:5062',company='43e6a825-d1b7-429a-8608-7e668087d005',mode=process.argv[2]??'after';
const origin=`/dashboard?companyId=${company}&lens=finance`,url=`${base}/finance/cash-forecast?companyId=${company}&action=open&source=dashboard&returnUrl=${encodeURIComponent(origin)}`;
const browser=await chromium.launch({channel:'msedge',headless:true}),page=await browser.newPage({viewport:{width:1857,height:837},acceptDownloads:true}),results=[],errors=[];
page.setDefaultTimeout(30000);page.on('pageerror',e=>errors.push(String(e)));
const check=(ok,message)=>{if(!ok)throw new Error(message);};
async function open(target=url){await page.goto(target);await page.locator('[data-testid="finance-operational-report"] table tbody tr').first().waitFor();await page.waitForTimeout(600);}
try{
 await open();
 await page.screenshot({path:out+`${mode}-desktop.png`,fullPage:true});
 if(mode==='before'){
  results.push({summary:await page.locator('.finance-report-totals').innerText(),records:await page.locator('table tbody tr').count()});
 }else{
  const baseline=JSON.parse(await readFile(out+'before-browser.json','utf8')).results[0];
  const amounts=await page.locator('.finance-report-metric dd').allTextContents();
  check(amounts.length===4,'Expected four cash summary cards');
  for(const amount of amounts)check(baseline.summary.includes(amount),'Financial value changed from baseline: '+amount);
  check(await page.locator('table tbody tr').count()===baseline.records,'Included records changed');
  check(await page.locator('.finance-report-flow--in').count()===baseline.records,'Invoice inflow labels missing');
  check(await page.locator('.finance-report-filters').evaluate(e=>parseFloat(getComputedStyle(e).paddingTop)>=18),'Filter panel spacing missing');
  check(await page.locator('.finance-module-shell--report').evaluate(e=>getComputedStyle(e).backgroundImage==='none'),'Report gradient remains');
  const details=page.locator('.finance-report-basis details');check(!(await details.getAttribute('open')),'Methodology should start collapsed');
  await details.locator('summary').focus();await details.locator('summary').press('Enter');check(await details.evaluate(e=>e.open),'Keyboard cannot expand calculation details');
  check(await details.innerText()!=='Calculation details','Calculation details empty');
  results.push({flow:'desktop-summary',amounts,records:baseline.records,keyboardDetails:true,passed:true});
  const coverage=page.locator('.finance-report-coverage details');await coverage.locator('summary').focus();await coverage.locator('summary').press('Enter');check(await coverage.evaluate(e=>e.open),'Coverage notes cannot expand');check(await coverage.locator('li').count()===18,'Coverage evidence omitted');await coverage.locator('summary').press('Enter');
  const sources=page.locator('.finance-report-source details');await sources.locator('summary').focus();await sources.locator('summary').press('Enter');check(await sources.evaluate(e=>e.open),'Full account evidence cannot expand');check(await sources.locator('li').count()===18,'Cash sources omitted');await sources.locator('summary').press('Enter');
  results.push({flow:'source-disclosures',accounts:18,coverageNotes:18,keyboard:true,passed:true});
  const downloading=page.waitForEvent('download');await page.getByTestId('finance-report-download').click();const download=await downloading;
  await download.saveAs(out+'forecast.csv');const csv=await readFile(out+'forecast.csv','utf8');check(csv.includes('Expected inflows')&&csv.includes('63262'),'CSV missing real forecast values');
  results.push({flow:'CSV',file:download.suggestedFilename(),saved:true,passed:true});
  await page.getByLabel('Currency code',{exact:true}).fill('SEK');await page.getByLabel('Horizon (days)',{exact:true}).fill('7');await page.getByRole('button',{name:'Apply filters',exact:true}).click();
  await page.waitForURL(u=>u.searchParams.get('horizonDays')==='7'&&u.searchParams.get('currency')==='SEK');await page.locator('.finance-report-metric').first().waitFor();await page.waitForTimeout(500);
  await page.reload();await page.locator('.finance-report-metric').first().waitFor();
  check(await page.getByLabel('Horizon (days)',{exact:true}).inputValue()==='7','Horizon lost on reload');check(await page.getByLabel('Currency code',{exact:true}).inputValue()==='SEK','Currency lost on reload');
  const filtered=page.url();results.push({flow:'filters-reload',url:filtered,passed:true});
  const review=page.locator('.finance-report-review').first(),href=await review.getAttribute('href');check(new URL(href,base).searchParams.get('companyId')===company,'Record lost company');
  await review.click();await page.waitForTimeout(700);const back=page.getByTestId('finance-journey-return'),backURL=new URL(await back.getAttribute('href'),base),filteredURL=new URL(filtered);
  check(backURL.pathname===filteredURL.pathname&&[...filteredURL.searchParams].every(([k,v])=>backURL.searchParams.get(k)===v)&&backURL.searchParams.size===filteredURL.searchParams.size,'Record lost filtered forecast return');
  await back.click();await page.locator('.finance-report-metric').first().waitFor();await page.waitForTimeout(700);results.push({flow:'record-return',passed:true});
  await page.getByLabel('Currency code',{exact:true}).fill('JPY');await page.getByRole('button',{name:'Apply filters',exact:true}).click();await page.waitForURL(u=>u.searchParams.get('currency')==='JPY');
  await page.getByRole('heading',{name:'No obligations match these filters',exact:true}).waitFor();
  for(const id of ['forecast-starting-cash','forecast-projected-cash'])check(await page.getByTestId(id).innerText()==='Unavailable','Empty currency invents a known cash position');
  await page.screenshot({path:out+'after-empty.png',fullPage:true});results.push({flow:'empty-currency',noInventedTotals:true,passed:true});
  await open();await page.setViewportSize({width:390,height:844});await page.screenshot({path:out+'after-narrow.png',fullPage:true});
  check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Narrow page overflow');
  const table=page.locator('.finance-report-scroll');await table.focus();check(await table.evaluate(e=>e.scrollWidth>e.clientWidth),'Mobile table should scroll inside its panel');
  await table.press('ArrowRight');await page.waitForTimeout(400);check(await table.evaluate(e=>e.scrollLeft)>0,'Mobile table cannot scroll with keyboard');results.push({flow:'narrow',tableScrollable:true,keyboardScroll:true,passed:true});
  await page.setViewportSize({width:1857,height:837});
  for(const path of ['/finance/receivables-aging','/finance/payables-aging']){
   await page.goto(`${base}${path}?companyId=${company}&returnUrl=${encodeURIComponent(origin)}`);await page.locator('.finance-report-table').waitFor();await page.waitForTimeout(500);
   check(await page.getByRole('heading',{name:path.includes('receivables')?'Receivables aging':'Payables aging',exact:true}).count()===1,'Shared aging route regressed');results.push({flow:path,passed:true});
  }
  check(errors.length===0,errors.join('; '));
 }
 await writeFile(out+`${mode}-browser.json`,JSON.stringify({timeUtc:new Date().toISOString(),mode,company,environment:'Existing local company, read-only Edge plus CSV download',results,errors},null,2));console.log(JSON.stringify({mode,checks:results.length,errors}));
}catch(e){await page.screenshot({path:out+`${mode}-diagnostic.png`,fullPage:true});throw e;}finally{await browser.close();}
