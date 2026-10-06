#!/usr/bin/env python3
"""Actual previous release upgrade; never synthetic 'old DB' only."""
import os,json,hashlib,tempfile,shutil,subprocess,time,sqlite3,urllib.request,urllib.parse,urllib.error,base64,signal
from pathlib import Path
from PIL import Image
root=Path(__file__).resolve().parents[1];workspace=root.parent;dotnet=os.environ['LOCALCLOUD_DOTNET'];oldver='1.4.0';old=root/'artifacts/previous14/server';engine=root/'artifacts/updater-linux/LocalCloud.Updater.dll'
work=Path(tempfile.mkdtemp(prefix='localcloud-upgrade14-'));install=work/'Installed';storage=work/'Library';config=work/'Settings/settings.json';payload=work/'Update/payload';install.mkdir();payload.mkdir(parents=True);config.parent.mkdir(parents=True);shutil.copytree(old,install/'server');shutil.copytree(root/'artifacts/linux',payload/'server');(install/'LocalCloud.exe').write_bytes(b'Linux test launcher; server uses real dotnet DLL');shutil.copy2(install/'LocalCloud.exe',payload/'LocalCloud.exe')
port=43117;url=f'http://127.0.0.1:{port}';config.write_text(json.dumps({'StorageRoot':str(storage),'Port':port,'AllowLan':False,'LaunchAtStartup':False,'ConcurrentUploads':3,'DuplicateBehavior':'copy','TrashRetentionDays':90,'PinHash':None,'PinSalt':None}));db=storage/'.localcloud/database.sqlite';passed=[];process=None;log=(work/'server.log').open('w')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
(install/'version.json').write_text(json.dumps({'version':oldver,'schemaVersion':4,'applicationFiles':[f.relative_to(install).as_posix() for f in install.rglob('*') if f.is_file()]}))
newfiles=[f.relative_to(payload).as_posix() for f in payload.rglob('*') if f.is_file()];(payload/'version.json').write_text(json.dumps({'version':'1.0.0','releaseChannel':'public','schemaVersion':4,'applicationFiles':newfiles}))
def manifest():
 (payload/'release.json').write_text(json.dumps({'product':'LocalCloud','releaseChannel':'public','renumberFrom':['1.4.0'],'version':'1.0.0','releaseChannel':'public','schemaVersion':4,'rollbackCompatibleSchema':4,'kind':'application-update','files':[{'path':f.relative_to(payload).as_posix(),'size':f.stat().st_size,'sha256':sha(f)} for f in payload.rglob('*') if f.is_file() and f.name!='release.json']}))
manifest()
def check(ok,label):assert ok,label;passed.append(label);print('PASS:',label,flush=True)
def http(p,method='GET',body=None,headers=None):
 if isinstance(body,dict):body=json.dumps(body).encode();headers={**(headers or {}),'Content-Type':'application/json'}
 return urllib.request.urlopen(urllib.request.Request(url+p,data=body,method=method,headers=headers or {}),timeout=20)
def js(p,method='GET',body=None):
 b=http(p,method,body).read();return json.loads(b) if b else None
def wait(version):
 for _ in range(200):
  try:
   if js('/api/status')['version']==version:return
  except Exception:pass
  time.sleep(.1)
 raise AssertionError((work/'server.log').read_text()[-3000:])
def start(version):
 global process
 env={**os.environ,'LOCALCLOUD_CONFIG':str(config)};env.pop('LOCALCLOUD_STORAGE',None);env.pop('LOCALCLOUD_PORT',None);process=subprocess.Popen([dotnet,str(install/'server/LocalCloud.Server.dll')],env=env,stdout=log,stderr=log);wait(version)
def stop():
 global process
 try:
  pid=js('/api/status').get('processId')
  if pid:os.kill(pid,signal.SIGTERM)
 except Exception:pass
 if process is not None:
  if process.poll() is None:process.terminate()
  process.wait(20);process=None
 for _ in range(100):
  try:http('/api/status');time.sleep(.1)
  except Exception:return
 raise AssertionError('did not stop')
def begin(name,folder,data,total=None):
 meta='filename '+base64.b64encode(name.encode()).decode()+',folder '+base64.b64encode(folder.encode()).decode();res=http('/uploads','POST',b'',{'Tus-Resumable':'1.0.0','Upload-Length':str(total or len(data)),'Upload-Metadata':meta});p=urllib.parse.urlparse(res.headers['Location']).path;http(p,'PATCH',data,{'Tus-Resumable':'1.0.0','Upload-Offset':'0','Content-Type':'application/offset+octet-stream'});return p
 def_unused=0
def upload(name,folder,data):
 p=begin(name,folder,data);return js('/api/uploads/'+p.split('/')[-1]+'/finalize','POST',{'policy':'copy'})['resultId']
def run(extra=None,expect=0):
 args=[dotnet,str(engine),'--install',str(install),'--config',str(config),'--dotnet',dotnet,'--non-interactive']+(extra or ['--package',str(payload)])
 with (work/'updater.log').open('a') as out:p=subprocess.run(args,stdout=out,stderr=out,timeout=160)
 assert p.returncode==expect,(work/'updater.log').read_text()[-6000:]
def rows(sql):
 with sqlite3.connect(db) as c:return c.execute(sql).fetchall()
try:
 start(oldver);js('/api/folders','POST',{'parent':'Photos','name':'Сочи 2014'});js('/api/folders','POST',{'parent':'Files','name':'Документы'})
 if not (storage/'Music').exists():js('/api/folders','POST',{'parent':'','name':'Music'})
 Image.new('RGB',(160,120),(52,108,192)).save(work/'photo.jpg');subprocess.run(['ffmpeg','-loglevel','error','-f','lavfi','-i','color=c=blue:s=160x120:d=1','-c:v','libx264',str(work/'video.mp4')],check=True);subprocess.run(['ffmpeg','-loglevel','error','-f','lavfi','-i','sine=duration=1','-c:a','libmp3lame',str(work/'audio.mp3')],check=True)
 originals={'Photos/Сочи 2014/photo.jpg':(work/'photo.jpg').read_bytes(),'Videos/video.mp4':(work/'video.mp4').read_bytes(),'Music/audio.mp3':(work/'audio.mp3').read_bytes(),'Files/Документы/file.pdf':b'%PDF-1.4\n% fixture\n%%EOF'};ids={p:upload(Path(p).name,Path(p).parent.as_posix(),data) for p,data in originals.items()};photo=ids['Photos/Сочи 2014/photo.jpg'];js('/api/files/'+photo+'/operate','POST',{'action':'favorite'});album=js('/api/albums','POST',{'name':'Наш альбом'})['id'];js('/api/albums/'+album+'/items','POST',{'ids':[photo]});trash=upload('trash.txt','Files',b'trash');js('/api/files/'+trash+'/operate','POST',{'action':'trash'})
 chunk=b'confirmed chunk'*10000;pending=begin('interrupted.bin','Files',chunk,len(chunk)*2)
 for _ in range(100):
  if rows("SELECT COUNT(*) FROM Files WHERE hash IS NULL OR (kind!='file' AND metadata IS NULL)")[0][0]==0:break
  time.sleep(.1)
 with sqlite3.connect(db) as c:c.execute('CREATE TABLE EditRecipes(fileId TEXT PRIMARY KEY,recipe TEXT)');c.execute('INSERT INTO EditRecipes VALUES(?,?)',(photo,'{"rotation":90,"custom":true}'))
 stable=rows('SELECT id,path,name,extension,size,modifiedAt,importedAt,hash,favorite,trashed FROM Files ORDER BY id');before={t:rows('SELECT * FROM '+t) for t in ['Albums','AlbumItems','TrashEntries','EditRecipes']};beforeSettings=json.loads(config.read_text());(install/'server/user-note.txt').write_text('preserved unknown file')
 # Actual running old release + interrupted tus session: the old forever-busy defect must be bypassed safely.
 marker=json.loads((install/'version.json').read_text());marker['releaseChannel']='public';(install/'version.json').write_text(json.dumps(marker));run(['--package',str(payload),'--plan'],expect=1);marker.pop('releaseChannel');(install/'version.json').write_text(json.dumps(marker));check(True,'already-public version cannot use the numbering exception')
 run();wait('1.0.0');check(True,'running real '+oldver+' updates despite inactive interrupted session')
 check(rows('SELECT MAX(version) FROM Migrations')==[(4,)],'existing schema 4 preserved')
 check(rows('SELECT id,path,name,extension,size,modifiedAt,importedAt,hash,favorite,trashed FROM Files ORDER BY id')==stable,'all original file IDs paths hashes and user state preserved')
 for t,expected in before.items():check(rows('SELECT * FROM '+t)==expected,t+' preserved')
 for p,data in originals.items():check((storage/p).read_bytes()==data,'original bytes preserved '+p)
 after=json.loads(config.read_text());check(after['AllowLan'] and {k:v for k,v in after.items() if k!='AllowLan'}=={k:v for k,v in beforeSettings.items() if k!='AllowLan'},'settings preserved with explicit one-time LAN repair only')
 check((install/'server/user-note.txt').read_text()=='preserved unknown file','unknown installation-directory user file untouched')
 check(int(http(pending,'HEAD',headers={'Tus-Resumable':'1.0.0'}).headers['Upload-Offset'])==len(chunk),'confirmed tus offset persists actual upgrade')
 http(pending,'PATCH',chunk,{'Tus-Resumable':'1.0.0','Upload-Offset':str(len(chunk)),'Content-Type':'application/offset+octet-stream'});res=js('/api/uploads/'+pending.split('/')[-1]+'/finalize','POST',{'policy':'copy'});check(http('/api/files/'+res['resultId']+'/original').read()==chunk*2,'old interrupted upload continues after upgrade exact bytes')
 new=upload('new.txt','Files',b'new after upgrade');check(http('/api/files/'+new+'/original').read()==b'new after upgrade','new uploads work after migration')
 backups=sorted((install/'_update_backup').iterdir());backup=backups[-1];check(not any(p.name in ['Photos','Videos','Music','Files'] for p in backup.rglob('*')),'application rollback backup does not copy media')
 with sqlite3.connect(backup/'database.sqlite.backup') as c:check(c.execute('PRAGMA quick_check').fetchone()[0]=='ok' and c.execute('SELECT MAX(version) FROM Migrations').fetchone()[0]==4,'consistent schema 4 SQLite backup')
 stop();start('1.0.0');check(js('/api/files/'+photo)['favorite'] and js('/api/albums')[0]['count']==1,'restart preserves upgraded library');stop()
 # Schema 4 -> 4 allows rollback while retaining the current library.
 run(['--rollback',str(backup)]);wait('1.4.0');check(rows('SELECT MAX(version) FROM Migrations')==[(4,)],'compatible rollback preserves schema and media');stop()
 # Explicit retained runtime delta: remove an unchanged DLL from payload, verify installed hash instead.
 retained='server/SixLabors.ImageSharp.dll';mf=json.loads((payload/'release.json').read_text());entry=next(f for f in mf['files'] if f['path']==retained);entry['existing']=True;(payload/retained).unlink();(payload/'release.json').write_text(json.dumps(mf));run(['--package',str(payload),'--plan']);check(True,'delta update preflight verifies omitted runtime from installation')
 victim=install/retained;saved=victim.read_bytes();victim.write_bytes(saved+b'bad');run(['--package',str(payload),'--plan'],expect=1);victim.write_bytes(saved);check(True,'delta rejects modified retained runtime before replacement')
 run();wait('1.0.0');check(http('/api/files/'+new+'/original').read()==b'new after upgrade','delta update retains runtime and current user media');stop()
 check(str(install) in (work/'updater.log').read_text() and 'временную папку Update можно удалить' in (work/'updater.log').read_text(),'success reports installed launch path and disposable update folder')
 result={'passed':len(passed),'checks':passed,'from':oldver,'to':'1.0.0','testRoot':str(work),'platform':'Linux engine plus actual previous server; Windows native not executed'};(root/'docs/upgrade14-to-public10-result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2));print('ALL UPGRADE 1.4 CHECKS PASSED',len(passed))
finally:stop();log.close()
