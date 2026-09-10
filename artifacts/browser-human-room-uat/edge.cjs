const fs=require('fs'), path=require('path');
const {chromium}=require('C:/Users/Johan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
 const browser=await chromium.launch({executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',headless:true,args:['--use-fake-device-for-media-stream']});
 const results=[];
 try {
  const context=await browser.newContext({viewport:{width:390,height:844}});
  const page=await context.newPage();
  const origin='http://127.0.0.1:5394', room='/sales/rooms/00000000-0000-0000-0000-000000000001';
  let errors=0;page.on('pageerror',()=>errors++);
  await page.route('**/*',route=>route.request().url().startsWith(origin)?route.continue():route.abort());
  await page.goto(origin+room);await page.getByText('Open the invitation link from the host').waitFor();
  results.push({missingInvitation:true,privateNavigation:await page.locator('nav').count()});
  await page.goto(origin+room+'#invite='+'x'.repeat(43));
  await page.getByRole('button',{name:'Ask to join',exact:true}).waitFor();
  await page.waitForFunction(()=>!document.querySelector('.room-join').disabled);
  if(page.url().includes('#'))throw new Error('Fragment not removed');
  await page.getByRole('button',{name:'Check devices',exact:true}).click();
  await page.getByRole('alert').filter({hasText:'permission was denied'}).waitFor({timeout:15000});
  results.push({permissionDeniedActionable:true});
  await page.screenshot({path:path.resolve('docs/verification/browser-sales-room/prompt4-live-edge-guest-denied-mobile.png'),fullPage:true});
  await context.grantPermissions(['camera','microphone'],{origin});
  await page.getByRole('button',{name:'Check devices',exact:true}).click();
  await page.getByRole('button',{name:'Mute',exact:true}).waitFor();
  await page.getByRole('button',{name:'Start camera',exact:true}).click();
  await page.getByRole('button',{name:'Stop camera',exact:true}).waitFor();
  const tracks=await page.locator('[data-preview]').evaluate(video=>video.srcObject.getTracks().map(track=>({kind:track.kind,state:track.readyState})));
  results.push({syntheticPreview:tracks});
  await page.getByRole('button',{name:'Test speaker',exact:true}).click();
  await page.keyboard.press('Tab');const focus=await page.evaluate(()=>document.activeElement.tagName);results.push({keyboardFocus:focus});
  await page.screenshot({path:path.resolve('docs/verification/browser-sales-room/prompt4-live-edge-guest-preview-mobile.png'),fullPage:true});
  await page.evaluate(async()=>{window.previewTracks=document.querySelector('[data-preview]').srcObject.getTracks();const module=await import('/js/sales-human-room.mjs');await module.disconnect();});
  const stopped=await page.evaluate(()=>window.previewTracks.every(t=>t.readyState==='ended'));if(!stopped)throw new Error('Preview tracks leaked');
  const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth);if(overflow)throw new Error('Mobile overflow');
  results.push({localTracksStopped:stopped,overflow,unhandledPageErrors:errors});if(errors)throw new Error('Unhandled browser error');
  fs.writeFileSync('docs/verification/browser-sales-room/prompt4-live-edge-browser-checks.json',JSON.stringify(results,null,2));console.log(JSON.stringify(results));
 }finally{await browser.close();}
})().catch(error=>{console.error(error.message);process.exitCode=1;});

