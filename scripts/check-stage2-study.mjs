// Local design-study verification through an installed headless Edge.
// Does not drive the native application or establish native WinUI behavior.
import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
const root = resolve(import.meta.dirname, '..');
const out = resolve(root, 'design/stage2/captures');
const profile = await mkdtemp(resolve(root, 'build/stage2-edge-'));
await mkdir(out, { recursive: true });
const browser = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  ['--headless=new','--no-first-run','--no-default-browser-check','--disable-extensions',
   '--disable-background-networking','--disable-component-update','--remote-debugging-port=0',
   `--user-data-dir=${profile}`,'about:blank'], { windowsHide: true, stdio: ['ignore','ignore','pipe'] });
let ws;
try {
  const endpoint = await new Promise((ok, fail) => {
    const timer=setTimeout(()=>fail(new Error('Edge endpoint timeout')),20000);
    let buffer=''; browser.stderr.on('data',chunk=>{buffer+=chunk.toString();const m=buffer.match(/DevTools listening on (ws:\/\/\S+)/);if(m){clearTimeout(timer);ok(m[1]);}});
    browser.once('error',fail);browser.once('exit',code=>fail(new Error(`Edge exited ${code}`)));
  });
  ws = new WebSocket(endpoint);
  await new Promise((ok,fail)=>{ws.onopen=ok;ws.onerror=fail;});
  let counter=0, session;const pending=new Map();const errors=[];
  ws.onmessage=e=>{const m=JSON.parse(e.data);if(m.id){const p=pending.get(m.id);if(p){clearTimeout(p.timer);pending.delete(m.id);m.error?p.fail(new Error(JSON.stringify(m.error))):p.ok(m.result);}}else if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails.text);};
  function cmd(method,params={},sid=session){return new Promise((ok,fail)=>{const id=++counter;const timer=setTimeout(()=>{pending.delete(id);fail(new Error(`Timeout ${method}`));},15000);pending.set(id,{ok,fail,timer});ws.send(JSON.stringify({id,method,params,...(sid?{sessionId:sid}:{})}));});}
  const target=await cmd('Target.createTarget',{url:'about:blank'},null);
  session=(await cmd('Target.attachToTarget',{targetId:target.targetId,flatten:true},null)).sessionId;
  await cmd('Page.enable');await cmd('Runtime.enable');
  const checks=[];
  function check(name,condition){checks.push({name,pass:!!condition});if(!condition)throw new Error(`Check failed: ${name}`);}
  async function evaluate(expression){const r=await cmd('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw new Error(r.exceptionDetails.text+': '+r.exceptionDetails.exception?.description);return r.result.value;}
  async function load(query='',width=1280,height=900){await cmd('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});await cmd('Page.navigate',{url:pathToFileURL(resolve(root,'design/stage2/index.html')).href+query});for(let i=0;i<50;i++){if(await evaluate('document.readyState === "complete" && !!document.querySelector(".file-table")'))break;await new Promise(r=>setTimeout(r,50));}await evaluate('document.fonts.ready');}
  async function click(selector){const r=await evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(selector)});e.scrollIntoView({block:'nearest'});const r=e.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()`);await cmd('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,...r});await cmd('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,...r});}
  async function key(key,code=key){await cmd('Input.dispatchKeyEvent',{type:'keyDown',key,code,windowsVirtualKeyCode:({Escape:27,F9:120,ArrowRight:39,ArrowDown:40,Insert:45})[key]||0});await cmd('Input.dispatchKeyEvent',{type:'keyUp',key,code});}
  async function capture(name){const r=await cmd('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await writeFile(resolve(out,name+'.png'),Buffer.from(r.data,'base64'));}
  await load('?theme=dark');
  check('11 synthetic rows including parent',await evaluate('document.querySelectorAll("tbody tr").length === 11'));
  const before=await evaluate('(()=>{const r=document.querySelector(".commandbar button svg").getBoundingClientRect();return [r.x,r.y,r.width,r.height];})()');
  const colorBefore=await evaluate('getComputedStyle(document.querySelector(".commandbar button svg")).color');
  const button=await evaluate('(()=>{const r=document.querySelector(".commandbar button").getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()');
  await cmd('Input.dispatchMouseEvent',{type:'mouseMoved',...button});
  const after=await evaluate('(()=>{const r=document.querySelector(".commandbar button svg").getBoundingClientRect();return [r.x,r.y,r.width,r.height];})()');
  check('toolbar glyph geometry stable on pointer move',JSON.stringify(before)===JSON.stringify(after));
  check('toolbar foreground stays legible on hover',await evaluate('getComputedStyle(document.querySelector(".commandbar button svg")).color')===colorBefore);
  await cmd('Input.dispatchMouseEvent',{type:'mouseMoved',x:1,y:1});
  check('toolbar foreground restores after pointer leaves',await evaluate('getComputedStyle(document.querySelector(".commandbar button svg")).color')===colorBefore);
  await capture('main-dark');
  await click('tbody tr:nth-child(7)');
  check('pointer selection changes full row',await evaluate('document.querySelectorAll("tr[aria-selected=true]").length===1 && document.querySelector("tr[aria-selected=true]").dataset.index==="6"'));
  check('row height 28 CSS px',await evaluate('document.querySelector("tbody tr").getBoundingClientRect().height===28'));
  await key('F9');check('F9 opens two independent panels',await evaluate('document.querySelectorAll(".panel").length===2'));
  check('two panels retain size column',await evaluate('[...document.querySelectorAll("th:last-child")].every(e=>getComputedStyle(e).display!=="none")'));
  await click('.panel:nth-child(2) tbody tr:nth-child(6)');
  check('inactive selection retained',await evaluate('document.querySelector(".panel").classList.contains("inactive") && document.querySelectorAll("tr[aria-selected=true]").length===2'));
  await capture('main-two-dark');
  await load('?theme=light');await capture('main-light');
  await click('.commandbar [data-action=add]');check('Add focus on archive path',await evaluate('document.activeElement.id==="archive-path"'));
  await capture('add-light');await click('#tab-metadata');await click('[data-override=mtime]');
  check('independent time override enables retained value',await evaluate('!document.querySelector("#mtime").disabled && document.querySelector("#mtime").checked'));
  await click('#tab-encryption');await evaluate('document.querySelector("#password-new").value="one";document.querySelector("#password-repeat").value="two"');
  await click('#sheet-footer [type=submit]');check('password mismatch shows text and focuses confirmation',await evaluate('!document.querySelector("#password-error").hidden && document.activeElement.id==="password-repeat"'));
  await key('Escape');check('Escape restores opener focus',await evaluate('!document.querySelector("#sheet").open && document.activeElement.dataset.action==="add"'));
  await load('?theme=dark&surface=add');await click('#tab-metadata');await capture('add-metadata-dark');
  await click('#tab-update');await capture('add-update-dark');
  await click('#tab-encryption');await capture('add-encryption-dark');
  await click('#tab-advanced');await capture('add-advanced-dark');
  await load('?theme=dark&surface=extract');await capture('extract-dark');
  await load('?theme=light&surface=progress');await click('#pause');check('pause / resume illustration',await evaluate('document.querySelector("#pause").textContent==="Продолжить"'));await click('#pause');await capture('progress-light');
  await click('[data-action=overwrite]');await capture('overwrite-light');await click('[data-action=skip-all]');check('conflict reply returns to operation',await evaluate('document.querySelector("#sheet-title").textContent==="Распаковка"'));
  await click('[data-action=password]');await click('[data-action=password-error]');await capture('password-error-light');
  await load('?theme=dark&surface=memory');check('memory policy skips archive, not a file',await evaluate('document.querySelector("[data-action=memory-skip]").textContent==="Пропустить архив"'));
  check('memory limit edit disabled until save selected',await evaluate('document.querySelector("#memory-limit").disabled'));await click('#memory-save');check('save limit enables numeric field',await evaluate('!document.querySelector("#memory-limit").disabled'));await capture('memory-dark');
  await load('?theme=dark&surface=result');await capture('result-dark');
  await load('?theme=light&surface=settings');await click('[data-settings=integration]');await capture('settings-integration-light');
  for(const name of ['appearance','list','folders','apps','associations','language']){await click(`[data-settings=${name}]`);await capture(`settings-${name}-light`);}
  await load('?theme=dark&surface=add&scale=2',1280,900);
  check('200% footer inside viewport',await evaluate('(()=>{const r=document.querySelector("#sheet-footer").getBoundingClientRect();return r.top>=0 && r.bottom<=innerHeight;})()'));
  check('200% dialog has scrollable content',await evaluate('document.querySelector("#sheet-content").scrollHeight>document.querySelector("#sheet-content").clientHeight'));
  await capture('add-200-dark');
  await load('?theme=light&surface=add',960,640);await capture('add-constrained-light');
  check('constrained footer visible',await evaluate('document.querySelector("#sheet-footer").getBoundingClientRect().bottom<=innerHeight'));
  await load('?theme=contrast&two=1&state=long');await capture('main-contrast-long');
  await load('?theme=light&state=error');await click('[data-action=retry]');check('error retry illustration',await evaluate('document.querySelectorAll("tbody tr").length===11'));
  await load('?theme=dark');await click('#alternative');await click('tbody tr:nth-child(7)');
  check('alternative mode focus independent of marked item',await evaluate('document.activeElement.dataset.index==="6" && document.querySelector("tr[aria-selected=true]").dataset.index==="4"'));
  await key('Insert');check('Insert toggles marking separately',await evaluate('document.querySelectorAll("tr[aria-selected=true]").length===2'));
  await click('#menubar button');await key('ArrowDown');check('menu arrow navigation',await evaluate('document.activeElement===document.querySelectorAll("#popup button")[1]'));await key('Escape');
  check('menu Escape restores invoker',await evaluate('document.activeElement===document.querySelector("#menubar button")'));
  check('no runtime exceptions',errors.length===0);
  const report={date:new Date().toISOString(),browser:await cmd('Browser.getVersion',{},null),scope:'HTML design study only; not WinUI/native parity, real DPI or accessibility validation',checks,errors};
  await writeFile(resolve(out,'checks.json'),JSON.stringify(report,null,2)+'\n');
  console.log(JSON.stringify({checks:checks.length,passed:checks.filter(x=>x.pass).length,captures:out,scope:report.scope}));
  await cmd('Browser.close',{},null).catch(()=>{});
} finally { ws?.close();browser.kill(); }
