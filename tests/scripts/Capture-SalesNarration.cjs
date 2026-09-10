const fs=require('fs'), path=require('path'), http=require('http');
const {chromium}=require('playwright');
const root=process.cwd(), evidence=path.join(root,'docs/verification/browser-sales-room');
const assets=path.join(root,'src/VirtualCompany.Web/wwwroot');
const css=fs.readFileSync(path.join(root,'src/VirtualCompany.Web/obj/Debug/net9.0/scopedcss/bundle/VirtualCompany.Web.styles.css'),'utf8');
const server=http.createServer((req,res)=>{
 const url=new URL(req.url,'http://localhost');
 if(url.pathname.startsWith('/sales/narration-preview/')){
  res.setHeader('Content-Type','audio/wav');res.setHeader('Cache-Control','no-store');
  res.end(fs.readFileSync(path.join(root,'artifacts/narration-live-prompt6/en-preview.wav')));return;
 }
 if(['/draft','/ready'].includes(url.pathname)){
  const markup=fs.readFileSync(path.join(root,'artifacts/browser-human-room-uat/narration-'+url.pathname.slice(1)+'.html'),'utf8');
  res.setHeader('Content-Type','text/html');
  res.end('<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css"><link rel="stylesheet" href="/css/app.css"><style>'+css+' body{background:#f7f9fc;margin:0;font-family:Inter,Arial,sans-serif}.uat-shell{display:grid;grid-template-columns:220px minmax(0,1fr);max-width:1500px;margin:auto}.uat-nav{padding:30px 24px;border-right:1px solid #e0e6ef;min-height:100vh;background:white}.uat-nav p{margin:26px 0}.uat-main{padding:24px;min-width:0}@media(max-width:700px){.uat-shell{grid-template-columns:1fr}.uat-nav{display:none}.uat-main{padding:0}}</style></head><body><div class="uat-shell"><nav class="uat-nav"><strong>Virtual Company</strong><p>Overview</p><p>Agent team</p><p>Finance</p><p style="color:#2563eb">Sales</p><p>Support</p><p>Work</p></nav><main class="uat-main"><a href="/draft">Meeting preparation</a>'+markup+'</main></div></body></html>');return;
 }
 const file=path.resolve(assets,'.'+url.pathname);
 if(!file.startsWith(assets+path.sep)||!fs.existsSync(file)){res.writeHead(404);res.end();return;}
 res.setHeader('Content-Type',file.endsWith('.css')?'text/css':'application/octet-stream');res.end(fs.readFileSync(file));
});
(async()=>{
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 const origin='http://127.0.0.1:'+server.address().port;
 const browser=await chromium.launch({headless:true,channel:process.env.VC_UAT_BROWSER_CHANNEL||'chrome'});
 const page=await browser.newPage();const results=[];
 try{
  await page.route('**/*',r=>r.request().url().startsWith(origin)?r.continue():r.abort());
  for(const state of ['draft','ready']){
   for(const width of [1440,390]){
    await page.setViewportSize({width,height:1000});await page.goto(origin+'/'+state);
    if(!(await page.locator("textarea").first().inputValue()))throw new Error("Script is not visible");
    if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw new Error('Horizontal overflow '+state+' '+width);
    await page.screenshot({path:path.join(evidence,'prompt6-'+state+'-'+width+'.png'),fullPage:true});
    if(width===1440)await page.screenshot({path:path.join(root,'artifacts/prompt6-'+state+'-review.jpg'),type:'jpeg',quality:65});
    results.push({state,width,horizontalOverflow:false});
   }
  }
  const playback=await page.locator('audio').evaluate(async a=>{await a.play();await new Promise(r=>setTimeout(r,300));const r={duration:a.duration,currentTime:a.currentTime,paused:a.paused};a.pause();return r;});
  if(playback.currentTime<=0||playback.paused)throw new Error('Real generated preview did not play.');
  results.push({realGeneratedAudioDecodedAndPlayed:true,...playback,fixture:'Rendered production component; isolated transport response. No authenticated full-app claim.'});
  fs.writeFileSync(path.join(evidence,'prompt6-browser-checks.json'),JSON.stringify(results,null,2));
  console.log(JSON.stringify(results));
 }finally{await browser.close();await new Promise(r=>server.close(r));}
})().catch(e=>{console.error(e);server.close();process.exitCode=1;});


