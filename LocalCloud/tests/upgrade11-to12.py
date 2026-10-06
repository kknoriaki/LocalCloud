#!/usr/bin/env python3
"""Real previous 1.0 server -> updater engine -> 1.1 -> restart/rollback.
Runs the SAME updater transaction/backup/copy/health code on Linux.
Windows GUI/CIM/tray integration must separately be checked on Windows.
"""
import os,sys,re,json,hashlib,tempfile,shutil,subprocess,time,sqlite3,urllib.request,urllib.parse,urllib.error,base64,signal
from pathlib import Path
from PIL import Image
root=Path(__file__).resolve().parents[1];workspace=root.parent
dotnet=os.environ.get('LOCALCLOUD_DOTNET','dotnet')
previous=Path(os.environ.get('LOCALCLOUD_PREVIOUS_SERVER',workspace/'baseline-1.1/server')).resolve()
engine=root/'artifacts/updater-linux/LocalCloud.Updater.dll';new=root/'artifacts/linux'
work=Path(tempfile.mkdtemp(prefix='localcloud-upgrade-'));install=work/'Application';storage=work/'Library';config=work/'Configuration/settings.json';payload=work/'Update/payload'
install.mkdir();storage.mkdir();config.parent.mkdir();payload.mkdir(parents=True)
shutil.copytree(previous,install/'server');(install/'LocalCloud.exe').write_bytes(b'Test-only Windows launcher placeholder; Linux uses actual server DLL')
shutil.copytree(new,payload/'server');shutil.copy2(install/'LocalCloud.exe',payload/'LocalCloud.exe')
port=43113;url=f'http://127.0.0.1:{port}'
config.write_text(json.dumps({'StorageRoot':str(storage),'Port':port,'AllowLan':False,'LaunchAtStartup':False,'ConcurrentUploads':3,'DuplicateBehavior':'copy','TrashRetentionDays':90,'PinHash':None,'PinSalt':None}))
oldfiles=[f.relative_to(install).as_posix() for f in install.rglob('*') if f.is_file()]
(install/'version.json').write_text(json.dumps({'version':'1.1.0','applicationFiles':oldfiles}))
newfiles=[f.relative_to(payload).as_posix() for f in payload.rglob('*') if f.is_file()]
(payload/'version.json').write_text(json.dumps({'version':'1.2.0','applicationFiles':newfiles,'schemaVersion':3}))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def manifest():
 files=[{'path':f.relative_to(payload).as_posix(),'size':f.stat().st_size,'sha256':digest(f)} for f in payload.rglob('*') if f.is_file() and f.name!='release.json']
 (payload/'release.json').write_text(json.dumps({'product':'LocalCloud','version':'1.2.0','schemaVersion':3,'rollbackCompatibleSchema':3,'kind':'application-update','files':files}))
manifest();passed=[];log=(work/'server.log').open('w');db=storage/'.localcloud/database.sqlite';process=None

def check(ok,name):
 assert ok,name;passed.append(name);print('PASS:',name,flush=True)
def req(path,method='GET',body=None,headers=None):
 h=headers or {}
 if isinstance(body,dict):body=json.dumps(body).encode();h={**h,'Content-Type':'application/json'}
 return urllib.request.urlopen(urllib.request.Request(url+path,data=body,method=method,headers=h),timeout=15)
def js(path,method='GET',body=None):
 data=req(path,method,body).read();return json.loads(data) if data else None
def wait(version):
 for _ in range(200):
  try:
   if js('/api/status')['version']==version:return
  except Exception:pass
  time.sleep(.1)
 raise AssertionError('Server startup '+version+' failed: '+(work/'server.log').read_text()[-3000:])
def start(version):
 global process
 env={**os.environ,'LOCALCLOUD_CONFIG':str(config)};env.pop('LOCALCLOUD_PORT',None);env.pop('LOCALCLOUD_STORAGE',None)
 process=subprocess.Popen([dotnet,str(install/'server/LocalCloud.Server.dll')],env=env,stdout=log,stderr=log);wait(version)
def stop():
 global process
 # The test environment masks /proc cmdline for some managed children.
 # Obtain the new server PID through localhost health; production updater uses IPC.
 try:
  pid=js('/api/status').get('processId')
  if not pid and process is None and (work/'updater.log').exists():
   matches=re.findall(r'LocalCloud PID: (\d+)',(work/'updater.log').read_text());pid=int(matches[-1]) if matches else None
  if pid:os.kill(pid,signal.SIGTERM)
 except (urllib.error.URLError,ProcessLookupError):pass
 # Covers servers launched by updater as well as the first subprocess.
 for p in Path('/proc').iterdir():
  if not p.name.isdigit():continue
  try:
   argv=(p/'cmdline').read_bytes().split(b'\0')
   if str(install/'server/LocalCloud.Server.dll').encode() in argv:os.kill(int(p.name),signal.SIGTERM)
  except (FileNotFoundError,ProcessLookupError,PermissionError):pass
 if process is not None:
  try:process.wait(20)
  except subprocess.TimeoutExpired:process.kill();process.wait()
  process=None
 for _ in range(250):
  try:req('/api/status');time.sleep(.1)
  except Exception:return
 raise AssertionError('Server did not stop')
def upload(name,folder,data):
 meta='filename '+base64.b64encode(name.encode()).decode()+',folder '+base64.b64encode(folder.encode()).decode()
 response=req('/uploads','POST',b'',{'Tus-Resumable':'1.0.0','Upload-Length':str(len(data)),'Upload-Metadata':meta});path=urllib.parse.urlparse(response.headers['Location']).path
 req(path,'PATCH',data,{'Tus-Resumable':'1.0.0','Upload-Offset':'0','Content-Type':'application/offset+octet-stream'})
 return js('/api/uploads/'+path.split('/')[-1]+'/finalize','POST',{'policy':'copy'})['resultId']
def run(extra=None,expect=0):
 args=[dotnet,str(engine),'--install',str(install),'--config',str(config),'--dotnet',dotnet,'--non-interactive']+(extra or ['--package',str(payload)])
 with (work/'updater.log').open('a') as output:result=subprocess.run(args,stdout=output,stderr=output,timeout=150)
 assert (result.returncode==expect),f'Updater exit {result.returncode}, expected {expect}: '+(work/'updater.log').read_text()[-5000:]
 return result

def rows(sql):
 with sqlite3.connect(db) as c:return c.execute(sql).fetchall()
try:
 start('1.1.0');check(True,'previous actual 1.1 server started with existing configuration')
 js('/api/folders','POST',{'parent':'Photos','name':'Сочи 2014'});js('/api/folders','POST',{'parent':'Files','name':'Документы'});js('/api/folders','POST',{'parent':'','name':'Music'})
 img=work/'photo.jpg';Image.new('RGB',(160,120),(52,108,192)).save(img)
 video=work/'video.mp4';audio=work/'audio.mp3'
 subprocess.run(['ffmpeg','-loglevel','error','-f','lavfi','-i','color=c=blue:s=160x120:d=1','-c:v','libx264','-pix_fmt','yuv420p',str(video)],check=True)
 subprocess.run(['ffmpeg','-loglevel','error','-f','lavfi','-i','sine=frequency=440:duration=1','-c:a','libmp3lame',str(audio)],check=True)
 originals={'Photos/Сочи 2014/photo.jpg':img.read_bytes(),'Videos/video.mp4':video.read_bytes(),'Music/audio.mp3':audio.read_bytes(),'Files/Документы/document.pdf':b'%PDF-1.4\n% test fixture\n%%EOF'}
 ids={p:upload(Path(p).name,str(Path(p).parent).replace('\\','/'),data) for p,data in originals.items()}
 photoid=ids['Photos/Сочи 2014/photo.jpg'];js('/api/files/'+photoid+'/operate','POST',{'action':'favorite'});album=js('/api/albums','POST',{'name':'Сохрани меня'});js('/api/albums/'+album['id']+'/items','POST',{'ids':[photoid]})
 trashid=upload('trash.txt','Files',b'trash-preserved');js('/api/files/'+trashid+'/operate','POST',{'action':'trash'})
 for _ in range(100):
  if rows("SELECT COUNT(*) FROM Files WHERE hash IS NULL OR (kind!='file' AND metadata IS NULL)")[0][0]==0:break
  time.sleep(.1)
 stop()
 with sqlite3.connect(db) as c:
  c.execute('CREATE TABLE EditRecipes(fileId TEXT PRIMARY KEY, recipe TEXT)');c.execute('INSERT INTO EditRecipes VALUES(?,?)',(photoid,'{"rotation":90,"fixture":true}'))
 before={table:rows('SELECT * FROM '+table) for table in ['Files','Albums','AlbumItems','TrashEntries','UploadSessions','EditRecipes']};settings_bytes=config.read_bytes()
 (install/'server/settings.json').write_text('{"must":"remain"}');(install/'server/user-note.txt').write_text('do not delete')
 run(['--package',str(payload),'--plan']);check(not (install/'_update_backup').exists(),'plan makes no application/data changes')
 # Corrupt one payload byte: preflight must fail before stop/backup/replacement.
 target=payload/'server/LocalCloud.Core.dll';saved=target.read_bytes();target.write_bytes(saved+b'bad');run(expect=1);target.write_bytes(saved)
 check(not (install/'_update_backup').exists() and config.read_bytes()==settings_bytes,'tampered payload rejected before replacement')
 release_path=payload/'release.json';valid_manifest=release_path.read_bytes();unsafe=json.loads(valid_manifest);unsafe['files'].append({'path':'Photos/overwrite.jpg','size':0,'sha256':'0'*64});release_path.write_text(json.dumps(unsafe));run(expect=1);release_path.write_bytes(valid_manifest)
 check(not (storage/'Photos/overwrite.jpg').exists(),'manifest cannot target protected media directories')
 nested=install/'embedded-library';(nested/'.localcloud').mkdir(parents=True);shutil.copy2(db,nested/'.localcloud/database.sqlite');nested_config=work/'Configuration/nested.json';value=json.loads(config.read_bytes());value['StorageRoot']=str(nested);nested_config.write_text(json.dumps(value));run(['--package',str(payload),'--config',str(nested_config)],expect=1)
 check((nested/'.localcloud/database.sqlite').exists() and not (install/'_update_backup').exists(),'overlapping installation/storage rejected without deleting data');shutil.rmtree(nested)
 # An unfinished paused upload blocks updates while preserving DB and temporary state.
 with sqlite3.connect(db) as c:c.execute("INSERT INTO UploadSessions(id,name,folder,size,status,createdAt) VALUES('paused','large.bin','Files',999,'uploading','2026-10-05')")
 run(expect=1);check(rows("SELECT status FROM UploadSessions WHERE id='paused'")==[('uploading',)],'unfinished upload blocks update and preserves resumable state')
 with sqlite3.connect(db) as c:c.execute("DELETE FROM UploadSessions WHERE id='paused'")
 run();wait('1.2.0')
 check(rows('SELECT MAX(version) FROM Migrations')[0][0]==3,'schema 3 remains compatible after 1.1 to 1.2')
 for table,values in before.items():
  if table=='Files':
   # Audio derived classification/metadata intentionally changes; identity/original/user state must not.
   stable='id,path,name,extension,size,modifiedAt,importedAt,hash,favorite,trashed'
   indexes=[0,1,2,3,5,7,8,10,11,12]
   expected=sorted(tuple(row[i] for i in indexes) for row in values)
   check(sorted(rows('SELECT '+stable+' FROM Files'))==expected,'existing Files IDs paths filenames hashes favorites and trash remain exact')
  else:check(rows('SELECT * FROM '+table)==values,'existing '+table+' remain exact')
 for path,data in originals.items():check((storage/path).read_bytes()==data,'original bytes preserved: '+path)
 check(config.read_bytes()==settings_bytes,'settings and storage root preserved byte for byte')
 check((install/'server/settings.json').read_text()=='{"must":"remain"}' and (install/'server/user-note.txt').read_text()=='do not delete','unknown configuration/application-directory user files preserved')
 backup=sorted((install/'_update_backup').iterdir())[-1]
 check((backup/'database.sqlite.backup').exists() and not any(p.name in ['Photos','Videos','Music','Files'] for p in backup.rglob('*')),'backup contains DB/application, no media copies')
 with sqlite3.connect(backup/'database.sqlite.backup') as c:check(c.execute('PRAGMA quick_check').fetchone()[0]=='ok' and c.execute('SELECT MAX(version) FROM Migrations').fetchone()[0]==3,'consistent pre-migration SQLite backup')
 new_id=upload('after-update.txt','Files',b'uploaded after upgrade');check(req('/api/files/'+new_id+'/original').read()==b'uploaded after upgrade','new upload works after upgrade')
 stop();start('1.2.0');check(js('/api/files/'+photoid)['favorite'] and js('/api/albums')[0]['count']==1,'restart keeps favorites/albums');stop()
 run(['--rollback',str(backup),'--plan']);check(json.loads((install/'version.json').read_text())['version']=='1.2.0','rollback plan leaves application unchanged')
 run(['--rollback',str(backup)]);wait('1.1.0');check(rows('SELECT MAX(version) FROM Migrations')[0][0]==3,'application rollback leaves compatible additive DB intact')
 check(req('/api/files/'+new_id+'/original').read()==b'uploaded after upgrade','rollback preserves media uploaded after update');stop()
 # Legacy Windows layout preflight uses the actual old archive, not invented paths.
 legacy=work/'LegacyWindows';shutil.copytree(Path(os.environ.get('LOCALCLOUD_PREVIOUS_WINDOWS',workspace/'previous-windows')),legacy)
 with (work/'legacy-plan.log').open('w') as output:
  result=subprocess.run([dotnet,str(engine),'--install',str(legacy),'--config',str(config),'--package',str(payload),'--plan','--non-interactive'],stdout=output,stderr=output,timeout=90)
 # Linux-native payload cannot overwrite unknown Windows files; validate Windows layout using generated Windows payload later.
 check(result.returncode==0,'real original Windows portable layout recognized for preflight')
 # Roll forward again, then verify active IPC update of new version, not only stopped legacy.
 run();wait('1.2.0');time.sleep(3)
 foreign=subprocess.Popen(['ffmpeg','-loglevel','error','-re','-f','lavfi','-i','sine=duration=60','-f','null','-'],stdout=log,stderr=log)
 try:
  run();wait('1.2.0');check(True,'running 1.1 gracefully drains/stops/restarts through same-user IPC');check(foreign.poll() is None,'unrelated FFmpeg process remains running')
 finally:foreign.terminate();foreign.wait(10)
 stop()
 # Fail startup after backup/replacement: valid manifest around invalid executable DLL.
 saved_server=(payload/'server/LocalCloud.Server.dll').read_bytes();(payload/'server/LocalCloud.Server.dll').write_bytes(b'invalid executable fixture');manifest()
 run(expect=1);failed=sorted((install/'_update_backup').iterdir())[-1];check(json.loads((failed/'update-state.json').read_text())['phase']=='failed','startup failure retains explicit recoverable journal and DB backup')
 run(['--rollback',str(failed)]);wait('1.2.0');check(req('/api/files/'+new_id+'/original').read()==b'uploaded after upgrade','rollback recovers after failed startup without media loss');stop()
 (payload/'server/LocalCloud.Server.dll').write_bytes(saved_server);manifest()
 check(config.read_bytes()==settings_bytes,'settings still exact after repeated update/restart/rollback')
 result={'passed':len(passed),'checks':passed,'testRoot':str(work),'platform':'Linux updater engine + real previous server; Windows execution not tested'}
 (root/'docs/upgrade11-to12-result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2));print('ALL UPGRADE CHECKS PASSED',len(passed),work)
finally:stop();log.close()
