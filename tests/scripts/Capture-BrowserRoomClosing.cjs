const fs=require('fs'),path=require('path');
const {chromium}=require('playwright');
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 const results=[];
 try {
  const page=await browser.newPage();
  await page.route('**/*',r=>r.abort());
  const css=fs.readFileSync('src/VirtualCompany.Web/obj/Debug/net9.0/scopedcss/bundle/VirtualCompany.Web.styles.css','utf8');
  for(const name of ['closing-partial','closing-draft']) for(const width of [1440,390]){
   await page.setViewportSize({width,height:1000});
   await page.setContent('<html><head><meta name="viewport" content="width=device-width,initial-scale=1"><style>body{margin:0;background:#f7f9fc}*{box-sizing:border-box}'+css+'</style></head><body>'+fs.readFileSync('artifacts/browser-human-room-uat/'+name+'.html','utf8')+'</body></html>');
   const result=await page.evaluate(()=>({overflow:document.documentElement.scrollWidth>innerWidth,
    title:document.body.innerText.includes('Review this meeting'),partial:document.body.innerText.includes('Partial capture'),
    retention:document.body.innerText.includes('Withdrawal stops future capture'),
    reviewed:document.body.innerText.includes('Reviewed for customer summary')}));
   if(name==='closing-draft' && await page.locator('textarea').first().inputValue() !== 'Send the implementation plan.')throw new Error('Draft content is not visible');
   if(result.overflow||!result.title||!result.partial||!result.retention||!result.reviewed)throw new Error(JSON.stringify(result));
   await page.screenshot({path:'docs/verification/browser-sales-room/prompt9-'+name+'-'+width+'.png',fullPage:true});
   results.push({name,width,...result});
  }
  fs.writeFileSync('docs/verification/browser-sales-room/prompt9-browser-checks.json',JSON.stringify(results,null,2));
  process.stdout.write(JSON.stringify(results));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
