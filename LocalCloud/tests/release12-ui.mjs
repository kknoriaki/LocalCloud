import {chromium,expect} from '../src/LocalCloud.Web/node_modules/@playwright/test/index.mjs';
import fs from 'node:fs';import path from 'node:path';import crypto from 'node:crypto';
const root=path.resolve(import.meta.dirname,'..'),base=process.env.LOCALCLOUD_TEST_URL;
const browser=await chromium.launch({executablePath:process.env.LOCALCLOUD_CHROMIUM,headless:true,args:['--no-sandbox','--disable-dev-shm-usage','--disable-gpu']});
const page=await browser.newPage({viewport:{width:1440,height:1000}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
async function post(p,body){const r=await page.request.post(base+'/api'+p,{data:body});expect(r.ok()).toBeTruthy();return r.json();}
await page.goto(base);await expect(page.getByRole('heading',{name:'Фото',exact:true})).toBeVisible();
await page.getByRole('button',{name:'Файлы',exact:true}).first().click();await page.getByRole('button',{name:'Музыка',exact:true}).click();
await expect(page.getByRole('heading',{name:'Музыка',exact:true})).toBeVisible();await page.getByRole('button',{name:'Открыть LocalCloud-tone.mp3',exact:true}).click();
await expect(page.locator('audio')).toBeAttached();await expect.poll(()=>page.locator('audio').evaluate(e=>e.readyState)).toBeGreaterThan(0);
await page.getByRole('button',{name:'Воспроизвести',exact:true}).click();await expect.poll(()=>page.locator('audio').evaluate(e=>e.currentTime)).toBeGreaterThan(0.1);
await page.getByRole('button',{name:'Приостановить воспроизведение'}).click();expect(await page.locator('audio').evaluate(e=>e.paused)).toBeTruthy();
await page.locator('.media-timeline').fill('1.5');await expect.poll(()=>page.locator('audio').evaluate(e=>e.currentTime)).toBeGreaterThan(1);
await page.getByRole('button',{name:'Выключить звук'}).click();expect(await page.locator('audio').evaluate(e=>e.muted)).toBeTruthy();
await page.screenshot({animations:'disabled',path:root+'/artifacts/screenshots/audio-desktop.png'});await page.keyboard.press('Escape');
await page.getByRole('button',{name:'Видео',exact:true}).first().click();await page.locator('.file-open').first().click();await expect(page.locator('.video-player')).toBeVisible();await expect.poll(()=>page.locator('video').evaluate(e=>e.readyState)).toBeGreaterThan(0);
await page.getByRole('button',{name:'Воспроизвести',exact:true}).click();await expect.poll(()=>page.locator('video').evaluate(e=>e.currentTime)).toBeGreaterThan(0.1);await page.screenshot({animations:'disabled',path:root+'/artifacts/screenshots/video-player.png'});await page.keyboard.press('Escape');
// A real browser upload, more than five 8 MiB chunks. Abort one request, then let tus retry HEAD/PATCH.
await page.getByRole('button',{name:'Файлы',exact:true}).first().click();await page.getByRole('button',{name:'Загрузить',exact:true}).click();
await page.getByRole('dialog').getByRole('combobox').selectOption('Files');
const content=crypto.randomBytes(40*1024*1024+117);let patches=0,interrupted=false;
await page.route('**/uploads/*',async route=>{if(route.request().method()==='PATCH'){patches++;if(patches===2){interrupted=true;await route.abort('connectionreset');return;}}await route.continue();});
await page.locator('input[type=file]').setInputFiles({name:'Browser-multi-chunk.bin',mimeType:'application/octet-stream',buffer:content});
await expect(page.getByText('Сохранён в Files',{exact:true})).toBeVisible({timeout:90000});expect(interrupted).toBeTruthy();expect(patches).toBeGreaterThan(5);
const files=await (await page.request.get(base+'/api/files?view=all&search=Browser-multi-chunk.bin')).json();expect(files.total).toBe(1);
const response=await page.request.get(base+'/api/files/'+files.items[0].id+'/original');expect(crypto.createHash('sha256').update(await response.body()).digest('hex')).toBe(crypto.createHash('sha256').update(content).digest('hex'));
await page.unroute('**/uploads/*');await page.getByRole('button',{name:'Свернуть загрузки'}).click();await page.keyboard.press('Escape');
// A stalled request must enter retry automatically: shorten the real XHR timeout only in this test.
await page.addInitScript(()=>{window.__lcTimeout=500;const open=XMLHttpRequest.prototype.open,send=XMLHttpRequest.prototype.send;XMLHttpRequest.prototype.open=function(method,url,...args){this.__lcUrl=String(url);return open.call(this,method,url,...args);};XMLHttpRequest.prototype.send=function(body){if(this.__lcUrl?.includes('/uploads/'))this.timeout=window.__lcTimeout;return send.call(this,body);};});
await page.reload();await page.getByRole('button',{name:'Файлы',exact:true}).first().click();await page.getByRole('button',{name:'Загрузить',exact:true}).click();await page.getByRole('dialog').getByRole('combobox').selectOption('Files');
let stalled=false,timeoutPatches=0;
await page.route('**/uploads/*',async route=>{if(route.request().method()==='PATCH'){timeoutPatches++;if(!stalled){stalled=true;await new Promise(resolve=>setTimeout(resolve,1000));try{await route.continue();}catch{}return;}}await route.continue();});
await page.locator('input[type=file]').setInputFiles({name:'Timeout-auto-resume.bin',mimeType:'application/octet-stream',buffer:crypto.randomBytes(9*1024*1024)});
await expect(page.getByText('Сохранён в Files',{exact:true})).toBeVisible({timeout:30000});expect(timeoutPatches).toBeGreaterThan(2);await page.unroute('**/uploads/*');await page.getByRole('button',{name:'Свернуть загрузки'}).click();await page.keyboard.press('Escape');
// Pause with a PATCH still in flight, then resume the same URL without creating a duplicate session.
await page.evaluate(()=>{window.__lcTimeout=60000;});await page.getByRole('button',{name:'Загрузить',exact:true}).click();await page.getByRole('dialog').getByRole('combobox').selectOption('Files');let waitingPatch=false,pausedPatches=0,releasePatch;
await page.route('**/uploads/*',async route=>{if(route.request().method()==='PATCH'){pausedPatches++;if(pausedPatches===2){waitingPatch=true;await new Promise(resolve=>{releasePatch=resolve;});try{await route.continue();}catch{}return;}}await route.continue();});
await page.locator('input[type=file]').setInputFiles({name:'Pause-resume.bin',mimeType:'application/octet-stream',buffer:crypto.randomBytes(20*1024*1024)});
await expect.poll(()=>waitingPatch).toBeTruthy();await page.getByRole('button',{name:'Пауза',exact:true}).click();await expect(page.getByText('Приостановлено',{exact:true})).toBeVisible();releasePatch?.();await page.getByRole('button',{name:'Продолжить загрузку',exact:true}).click();await expect(page.getByText('Сохранён в Files',{exact:true})).toHaveCount(2,{timeout:30000});
const resumed=await (await page.request.get(base+'/api/files?view=all&search=Pause-resume.bin')).json();expect(resumed.total).toBe(1);await page.unroute('**/uploads/*');await page.getByRole('button',{name:'Свернуть загрузки'}).click();await page.keyboard.press('Escape');

// Verify unchanged sidebar and exactly +2px for representative existing content typography.
const baseline=JSON.parse(fs.readFileSync(root+'/artifacts/baseline-fonts.json','utf8'));
await page.getByRole('button',{name:/^Фото/}).first().click();
const fonts=await page.evaluate(()=>({sidebar:[...document.querySelectorAll('.sidebar *')].filter(e=>e.children.length===0&&e.textContent.trim()).map(e=>({text:e.textContent.trim(),size:getComputedStyle(e).fontSize})),content:['.page-heading h1','.subtitle','.count'].map(s=>({selector:s,size:parseFloat(getComputedStyle(document.querySelector(s)).fontSize)}))}));
expect(fonts.sidebar.map(v=>v.size)).toEqual(baseline.sidebar.map(v=>v.size));fonts.content.forEach((v,i)=>expect(v.size).toBe(baseline.content[i].size+2));
await page.getByRole('button',{name:'Настройки',exact:true}).first().click();await expect(page.getByRole('heading',{name:'Если iPhone не открывает LocalCloud'})).toBeVisible();await expect(page.getByText('Сервер принимает подключения из домашней сети')).toBeVisible();await page.locator('.network-check').scrollIntoViewIfNeeded();await page.screenshot({animations:'disabled',path:root+'/artifacts/screenshots/network-check.png',fullPage:true});
for(const width of [375,390,430,768,1024,1440,1920]){await page.setViewportSize({width,height:900});await page.waitForTimeout(50);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();}
await page.setViewportSize({width:390,height:844});await page.locator('.bottom-nav').getByRole('button',{name:'Ещё',exact:true}).click();await page.locator('.mobile-more').getByRole('button',{name:'Музыка',exact:true}).click();await page.getByRole('button',{name:'Открыть LocalCloud-tone.mp3',exact:true}).click();await expect(page.locator('.audio-player')).toBeVisible();await page.screenshot({animations:'disabled',path:root+'/artifacts/screenshots/audio-mobile.png'});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
await page.keyboard.press('Escape');await page.reload();await expect(page.locator('.bottom-nav')).toBeVisible();
await browser.close();expect(errors).toEqual([]);console.log(JSON.stringify({audioPlayback:true,videoPlayback:true,multiChunkBytes:content.length,patchRequests:patches,retryAfterDisconnect:true,automaticRetryAfterTimeout:true,pauseResumeWithoutDuplicate:true,exactOriginalHash:true,sidebarUnchanged:true,contentTextIncrease:2,viewportWidths:[375,390,430,768,1024,1440,1920],errors},null,2));
