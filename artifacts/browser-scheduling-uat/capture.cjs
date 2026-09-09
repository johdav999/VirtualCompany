const { chromium } = require('playwright');
const path = require('path');
(async()=>{
 const browser=await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});
 try {
  const page=await browser.newPage({viewport:{width:1024,height:640}});
  await page.route('**/*',route=>route.request().url().startsWith('http://127.0.0.1:8769/')?route.continue():route.abort());
  const results=[];
  for(const [name,width,height] of [['picker-desktop',1024,640],['picker-mobile',390,650],['preparation-desktop',1440,1000]]){
   await page.setViewportSize({width,height});await page.goto('http://127.0.0.1:8769/'+(name.startsWith('picker')?'picker':'preparation')+'.html');
   const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth);
   await page.screenshot({path:path.resolve('docs/verification/browser-sales-room/prompt3-'+name+'.png'),fullPage:true});
   results.push({name,overflow,title:await page.title()});
  }
  process.stdout.write(JSON.stringify(results));
 }finally{await browser.close();}
})().catch(e=>{process.stderr.write(e.message);process.exitCode=1});
