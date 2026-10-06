import subprocess,os,json,time,urllib.request,hashlib,shutil,tempfile
from pathlib import Path
root=Path(__file__).resolve().parents[1];scratch=Path(tempfile.mkdtemp(prefix='localcloud-api14-'));library=scratch/'library';(library/'Music').mkdir(parents=True,exist_ok=True);(library/'Photos').mkdir(parents=True,exist_ok=True)
source=root/'artifacts/test14/library'
for p in source.rglob('*'):
 if p.is_file() and '.localcloud' not in p.parts:
  dest=library/p.relative_to(source);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,dest)
config=scratch/'settings.json';config.write_text(json.dumps({'StorageRoot':str(library),'Port':43145,'AllowLan':True,'LaunchAtStartup':False}))
env=os.environ.copy();env['LOCALCLOUD_CONFIG']=str(config);exe=Path(os.environ.get('LOCALCLOUD_DOTNET') or shutil.which('dotnet') or str(root.parents[1]/'runtime/dotnet/dotnet'));log=(scratch/'server.log').open('w');p=subprocess.Popen([str(exe),str(root/'src/LocalCloud.Server/bin/Release/net10.0/LocalCloud.Server.dll')],env=env,stdout=log,stderr=log)
http=urllib.request.build_opener(urllib.request.ProxyHandler({}));base='http://localhost:43145';checks=[]
def req(path,body=None,method=None):
 data=json.dumps(body,ensure_ascii=False).encode() if body is not None else None
 r=urllib.request.Request(base+'/api'+path,data,headers={'Content-Type':'application/json'},method=method or ('POST' if data is not None else 'GET'))
 with http.open(r,timeout=30) as response:return json.load(response)
def check(ok,name):
 assert ok,name
 checks.append(name);print(name,flush=True)
def poll(fun):
 for _ in range(120):
  try:
   x=fun()
   if x:return x
  except (OSError,ValueError):pass
  time.sleep(.25)
 print('BACKGROUND',req('/background'),flush=True); print('ALL',req('/files?view=all'),flush=True); raise AssertionError('poll timed out')
try:
 poll(lambda:req('/status'));check(req('/status')['version']=='1.4.0','version 1.4.0')
 files=poll(lambda:(a if len(a:=req('/files?view=audio')['items'])==2 and all(f['metadata'] for f in a) else None));f=next(f for f in files if 'Русская' in f['name']);cover=req('/files?view=photos')['items'][0]
 check(req('/files?view=audio&search='+urllib.parse.quote('ИЛЬЯ'))['total']==1,'Cyrillic artist search')
 check(req('/files?view=audio&missingTags=true')['total']==1,'missing embedded tags filter')
 check(len(req('/files?view=audio&sort=artist')['items'])==2,'artist sort query')
 source=library/f['path'];before=source.read_bytes();beforeHash=hashlib.sha256(before).hexdigest()
 values={'title':'Новая русская песня','artist':'Исполнитель Илья','album':'Тестовый альбом','coverId':cover['id']}
 plan=req('/files/'+f['id']+'/audio-tags/preview',values);check(source.read_bytes()==before,'preview does not change original')
 receipt=req('/files/'+f['id']+'/audio-tags/write',{'token':plan['token'],'confirmed':True})
 check(receipt['audioVerified'] and receipt['originalHash'].lower()==beforeHash,'MP3 audio SHA-256 preserved')
 check((library/receipt['backup']).read_bytes()==before,'backup byte-for-byte original')
 probe=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_format','-show_streams','-of','json',str(source)]));check(probe['format']['tags']['title']==values['title'] and probe['format']['tags']['artist']==values['artist'],'UTF-16 ID3 Cyrillic written to physical MP3');check(any(s.get('disposition',{}).get('attached_pic') for s in probe['streams']),'custom cover embedded')
 for endpoint in ['/content-search','/content-search/models']:
  try:req(endpoint);raise AssertionError('removed endpoint still exists')
  except urllib.error.HTTPError as e:check(e.code==404,'removed endpoint '+endpoint)
 req('/background',{'paused':True});check(req('/background')['paused'],'background pause');req('/background',{'paused':False});time.sleep(4);initial=req('/background')['fullScans']
 external=library/'Music/new.txt';external.write_text('new file');poll(lambda:req('/files?view=all&search=new.txt')['total']==1);check(req('/background')['fullScans']==initial,'filesystem file creation uses incremental indexing')
 external.write_text('changed outside the app');poll(lambda:req('/files?view=all&search=new.txt')['items'][0]['size']==len(external.read_bytes()));check(req('/background')['fullScans']==initial,'filesystem modification avoids full scan')
 external.unlink();poll(lambda:req('/files?view=all&search=new.txt')['total']==0);check(req('/background')['fullScans']==initial,'filesystem deletion uses incremental index')
 network=req('/network');check(network['listeningLan'] and len(network['probes'])==min(8,len(network['addresses'])) and all(isinstance(p['connected'],bool) for p in network['probes']),'LAN diagnostics report measured probe status')
 with http.open(urllib.request.Request(base+'/api/files/'+f['id']+'/original',headers={'Range':'bytes=0-99'})) as r:check(r.status==206 and r.read()==source.read_bytes()[:100],'media byte range streaming')
 plan=req('/files/'+f['id']+'/audio-tags/preview',values);source.write_bytes(source.read_bytes()+b'changed')
 try:req('/files/'+f['id']+'/audio-tags/write',{'token':plan['token'],'confirmed':True});raise AssertionError('stale preview accepted')
 except urllib.error.HTTPError as e:check(e.code==400,'stale preview rejects changed original')
 result={'checks':checks,'passed':len(checks)};(root/'docs/release14-api-result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2));print(json.dumps(result,ensure_ascii=False,indent=2))
finally:
 p.terminate()
 try:p.wait(timeout=10)
 except subprocess.TimeoutExpired:p.kill();p.wait()
 log.close()
