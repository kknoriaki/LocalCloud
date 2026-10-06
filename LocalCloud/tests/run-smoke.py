import pathlib,subprocess,os,time,json,tempfile,urllib.request,urllib.error,sys,base64,hashlib
root=pathlib.Path(__file__).resolve().parents[1];temp=pathlib.Path(tempfile.mkdtemp(prefix='localcloud-e2e-'));port=43110
env=os.environ.copy();env.update({'LOCALCLOUD_CONFIG':str(temp/'settings.json'),'LOCALCLOUD_STORAGE':str(temp/'library'),'LOCALCLOUD_PORT':str(port),'LOCALCLOUD_TEST_URL':f'http://127.0.0.1:{port}'})
(temp/'settings.json').write_text(json.dumps({'StorageRoot':str(temp/'library'),'Port':port,'AllowLan':False,'LaunchAtStartup':False,'ConcurrentUploads':2,'DuplicateBehavior':'ask','TrashRetentionDays':30}))
log=open(root/'artifacts/smoke-server.log','w');dotnet=env.get('LOCALCLOUD_DOTNET','dotnet');proc=subprocess.Popen([dotnet,str(root/'artifacts/linux/LocalCloud.Server.dll')],env=env,stdout=log,stderr=log)
try:
 for i in range(100):
  try:
   urllib.request.urlopen(env['LOCALCLOUD_TEST_URL']+'/api/status',timeout=1);break
  except Exception:
   if proc.poll() is not None:raise RuntimeError('server exited')
   time.sleep(.1)
 subprocess.run([sys.executable,str(root/'tests/api-smoke.py')],env=env,check=True)
 # Reconcile files added without API, then restart with SQLite and original files intact.
 explorer=temp/'library/Files/Explorer-created.txt';explorer.write_text('Added directly using filesystem')
 for i in range(50):
  data=json.load(urllib.request.urlopen(env['LOCALCLOUD_TEST_URL']+'/api/files?view=all&search=Explorer-created'))
  if data['total']==1:break
  time.sleep(.2)
 assert data['total']==1,'watcher did not index external file'
 print('PASS: filesystem watcher detects external file')
 proc.terminate();proc.wait(15);proc=subprocess.Popen([dotnet,str(root/'artifacts/linux/LocalCloud.Server.dll')],env=env,stdout=log,stderr=log)
 for i in range(100):
  try:
   data=json.load(urllib.request.urlopen(env['LOCALCLOUD_TEST_URL']+'/api/files?view=all'));break
  except Exception:time.sleep(.1)
 assert data['total']>=18
 print('PASS: original library persists across server restart')
 content=bytes(range(256))*32768;chunk=content[:2*1024*1024];metadata='filename '+base64.b64encode(b'Resume-after-restart.bin').decode()+',folder '+base64.b64encode(b'Files').decode()
 def http(p,method='GET',body=None,headers=None):return urllib.request.urlopen(urllib.request.Request(env['LOCALCLOUD_TEST_URL']+p,data=body,method=method,headers=headers or {}))
 r=http('/uploads','POST',b'',{'Tus-Resumable':'1.0.0','Upload-Length':str(len(content)),'Upload-Metadata':metadata});upload_path=urllib.parse.urlparse(r.headers['Location']).path
 http(upload_path,'PATCH',chunk,{'Tus-Resumable':'1.0.0','Upload-Offset':'0','Content-Type':'application/offset+octet-stream'})
 proc.terminate();proc.wait(15);proc=subprocess.Popen([dotnet,str(root/'artifacts/linux/LocalCloud.Server.dll')],env=env,stdout=log,stderr=log)
 for i in range(100):
  try:http('/api/status');break
  except Exception:time.sleep(.1)
 r=http(upload_path,'HEAD',headers={'Tus-Resumable':'1.0.0'});assert int(r.headers['Upload-Offset'])==len(chunk)
 http(upload_path,'PATCH',content[len(chunk):],{'Tus-Resumable':'1.0.0','Upload-Offset':str(len(chunk)),'Content-Type':'application/offset+octet-stream'})
 result=json.load(http('/api/uploads/'+upload_path.split('/')[-1]+'/finalize','POST',b'{"policy":"copy"}',{'Content-Type':'application/json'}));assert hashlib.sha256(http('/api/files/'+result['resultId']+'/original').read()).digest()==hashlib.sha256(content).digest()
 print('PASS: resumable upload persists confirmed offset and exact bytes across server restart')

 subprocess.run(['node',str(root/'tests/ui-smoke.mjs')],env=env,check=True)
 # PIN requires credentials; private localhost owner is still required to authenticate.
 request=urllib.request.Request(env['LOCALCLOUD_TEST_URL']+'/api/settings',data=json.dumps({'port':port,'allowLan':False,'concurrentUploads':2,'duplicateBehavior':'ask','trashRetentionDays':30,'pin':'123456'}).encode(),headers={'Content-Type':'application/json'},method='POST');urllib.request.urlopen(request)
 try:urllib.request.urlopen(env['LOCALCLOUD_TEST_URL']+'/api/files');raise AssertionError('PIN ignored')
 except urllib.error.HTTPError as e:assert e.code==401
 print('PASS: PIN locks API')
 request=urllib.request.Request(env['LOCALCLOUD_TEST_URL']+'/api/unlock',data=b'{"pin":"123456"}',headers={'Content-Type':'application/json'},method='POST');response=urllib.request.urlopen(request);cookie=response.headers['Set-Cookie'].split(';')[0];request=urllib.request.Request(env['LOCALCLOUD_TEST_URL']+'/api/files',headers={'Cookie':cookie});assert urllib.request.urlopen(request).status==200
 print('PASS: PIN grants session')
 print('ALL SMOKE CHECKS PASSED. Isolated test files:',temp)
finally:
 proc.terminate();proc.wait(15);log.close()
