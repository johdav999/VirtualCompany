const {chromium}=require('playwright'); const fs=require('node:fs');
(async()=>{ const browser=await chromium.launch({channel:'msedge',headless:true}); try {
const context=await browser.newContext({ignoreHTTPSErrors:true});const page=await context.newPage();
await context.route('**/*',r=>new URL(r.request().url()).hostname==='localhost'?r.continue():r.abort());
for(const name of ['presenter','first-test']) for(const width of [1000,360]){
await page.setViewportSize({width,height:800});
await page.setContent(fs.readFileSync('artifacts/teams-ui/'+name+'.html','utf8'),{waitUntil:'networkidle'});
await page.screenshot({path:'artifacts/teams-ui/'+name+'-'+width+'.png',fullPage:true});
const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>window.innerWidth);
if(overflow)throw Error(name+' overflows at '+width);
}
console.log('PASS: production component snapshots at desktop and narrow widths, no horizontal overflow.');
}finally{await browser.close();}})().catch(e=>{console.error(e.message);process.exitCode=1;});
