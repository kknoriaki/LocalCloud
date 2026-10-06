import pathlib,tempfile,json,subprocess,os,time,urllib.request,urllib.parse,base64,sqlite3
r=pathlib.Path(__file__).resolve().parents[1];work=pathlib.Path(tempfile.mkdtemp(prefix='localcloud-12-'));dotnet=os.environ['LOCALCLOUD_DOTNET'];port=43115;base=f'http://127.0.0.1:{port}';config=work/'settings.json';library=work/'library'
config.write_text(json.dumps({'StorageRoot':str(library),'Port':port,'AllowLan':True,'LaunchAtStartup':False,'ConcurrentUploads':2,'DuplicateBehavior':'copy','TrashRetentionDays':30}))
env={**os.environ,'LOCALCLOUD_CONFIG':str(config),'LOCALCLOUD_TEST_URL':base};env.pop('LOCALCLOUD_STORAGE',None);env.pop('LOCALCLOUD_PORT',None)
log=(r/'artifacts/release12-smoke.log').open('w');server=None

def start(folder):
 global server
 server=subprocess.Popen([dotnet,str(folder/'LocalCloud.Server.dll')],env=env,stdout=log,stderr=log)
 for _ in range(150):
  try:urllib.request.urlopen(base+'/api/status',timeout=.5);return
  except Exception:
   if server.poll()!=None:raise RuntimeError('server failed: '+(r/'artifacts/release12-smoke.log').read_text()[-3000:])
   time.sleep(.1)
 raise RuntimeError('server startup timeout')
def stop():
 global server
 if server:server.terminate();server.wait(15);server=None
try:
 start(r.parent/'baseline-1.1/server')
 # Capture actual old-browser computed typography using the old server/frontend.
 capture=r/'tests/.capture-fonts.mjs';capture.write_text("""import {chromium} from '../src/LocalCloud.Web/node_modules/@playwright/test/index.mjs';import fs from 'node:fs';const b=await chromium.launch({executablePath:process.env.LOCALCLOUD_CHROMIUM,args:['--no-sandbox']});const p=await b.newPage({viewport:{width:1440,height:1000}});await p.goto(process.env.LOCALCLOUD_TEST_URL);await p.locator('.count').waitFor();await p.evaluate(()=>document.fonts.ready);const fonts=await p.evaluate(()=>({sidebar:[...document.querySelectorAll('.sidebar *')].filter(e=>e.children.length===0&&e.textContent.trim()).map(e=>({text:e.textContent.trim(),size:getComputedStyle(e).fontSize})),content:['.page-heading h1','.subtitle','.count'].map(s=>({selector:s,size:parseFloat(getComputedStyle(document.querySelector(s)).fontSize)}))}));fs.writeFileSync(process.env.FONTS_OUTPUT,JSON.stringify(fonts));await b.close();""")
 # Old library must contain an item for the count toolbar (empty state has none).
 from PIL import Image
 im=library/'Photos/test.jpg';Image.new('RGB',(120,90),(33,77,130)).save(im)
 for _ in range(80):
  if json.load(urllib.request.urlopen(base+'/api/files?view=photos'))['total']:break
  time.sleep(.1)
 subprocess.run(['node',str(capture)],env={**env,'FONTS_OUTPUT':str(r/'artifacts/baseline-fonts.json')},check=True,timeout=40);capture.unlink();stop()
 start(r/'artifacts/linux');(library/'Music').mkdir(exist_ok=True)
 subprocess.run(['ffmpeg','-loglevel','error','-f','lavfi','-i','sine=frequency=440:duration=4','-metadata','title=LocalCloud sound','-metadata','artist=Generated test tone','-c:a','libmp3lame',str(library/'Music/LocalCloud-tone.mp3')],check=True)
 subprocess.run(['ffmpeg','-loglevel','error','-f','lavfi','-i','testsrc2=size=320x180:rate=25:duration=4','-c:v','libx264','-pix_fmt','yuv420p',str(library/'Videos/LocalCloud-video.mp4')],check=True)
 for _ in range(150):
  audio=json.load(urllib.request.urlopen(base+'/api/files?view=audio'))
  if audio['total'] and audio['items'][0]['duration']:break
  time.sleep(.1)
 assert audio['items'][0]['duration']>3 and 'Generated test tone' in audio['items'][0]['metadata']
 aid=audio['items'][0]['id'];response=urllib.request.urlopen(urllib.request.Request(base+'/api/files/'+aid+'/original',headers={'Range':'bytes=0-100'}));assert response.status==206 and response.headers['Content-Type']=='audio/mpeg' and len(response.read())==101
 diagnostic=json.load(urllib.request.urlopen(base+'/api/network'));assert diagnostic['listeningLan'] and diagnostic['port']==port
 # Connect via a real non-loopback private address: this uses the LAN listener and access boundary.
 addresses=json.load(urllib.request.urlopen(base+'/api/settings'))['addresses'];assert addresses
 lan=urllib.request.build_opener(urllib.request.ProxyHandler({}));lan_verified=False
 try:
  assert json.load(lan.open(addresses[0]+'/api/status',timeout=3))['online'];lan_verified=True
  try:lan.open(addresses[0]+'/api/network');raise AssertionError('remote diagnostics must be forbidden')
  except urllib.error.HTTPError as e:assert e.code==403
 except urllib.error.URLError as e:
  if getattr(e.reason,'errno',None)!=101:raise
  print('SKIP: physical LAN connection is unavailable in this isolated execution network')
 result=subprocess.run(['node',str(r/'tests/release12-ui.mjs')],env=env,timeout=160,capture_output=True,text=True);print(result.stdout);print(result.stderr);assert result.returncode==0; (r/'docs/release12-test-result.json').write_text(result.stdout)
 stop();start(r/'artifacts/linux');assert json.load(urllib.request.urlopen(base+'/api/files/'+aid))['kind']=='audio';print('PASS: audio metadata, range playback, LAN binding diagnostics, restart persistence; physical LAN verified:',lan_verified)
finally:stop();log.close()
