"""Runs against an isolated server (LOCALCLOUD_TEST_URL). All assets are generated locally."""
import base64,hashlib,json,os,time,urllib.request,urllib.error,pathlib,sys
URL=os.environ.get('LOCALCLOUD_TEST_URL','http://localhost:43110'); ROOT=pathlib.Path(__file__).resolve().parents[1]; DATA=ROOT/'artifacts/test-data'
checks=[]
def req(path,method='GET',body=None,headers=None):
 if isinstance(body,dict):body=json.dumps(body).encode();headers={**(headers or {}),'Content-Type':'application/json'}
 
 try:r=urllib.request.urlopen(urllib.request.Request(URL+path,data=body,method=method,headers=headers or {}),timeout=90)
 except urllib.error.HTTPError as e:
  print('HTTP error',method,path,e.code,e.read().decode(errors='replace'));raise
 raw=r.read();return r,json.loads(raw) if 'application/json' in r.headers.get('Content-Type','') else raw
def check(c,label):
 assert c,label
 checks.append(label);print('PASS:',label)
def rejected(path,body,expected=400):
 try:req(path,'POST',body)
 except urllib.error.HTTPError as e:return e.code==expected
 return False
def upload(name,data,folder='Photos/Сочи 2014',resume=False,policy='copy'):
 metadata=','.join(k+' '+base64.b64encode(v.encode()).decode() for k,v in {'filename':name,'folder':folder}.items())
 r,_=req('/uploads','POST',b'',{'Tus-Resumable':'1.0.0','Upload-Length':str(len(data)),'Upload-Metadata':metadata});url=r.headers['Location'];path=urllib.parse.urlparse(url).path;id=path.split('/')[-1]
 offset=0;chunk=2*1024*1024
 while offset<len(data):
  part=data[offset:offset+chunk];r,_=req(path,'PATCH',part,{'Tus-Resumable':'1.0.0','Upload-Offset':str(offset),'Content-Type':'application/offset+octet-stream'});offset+=len(part)
  assert int(r.headers['Upload-Offset'])==offset
  if resume and offset==chunk:
   r,_=req(path,'HEAD',headers={'Tus-Resumable':'1.0.0'});check(int(r.headers['Upload-Offset'])==offset,'HEAD reports confirmed chunk after reconnect')
 check(req('/api/uploads/'+id+'/status')[1]['status']=='uploaded','upload complete, awaiting finalize')
 return req('/api/uploads/'+id+'/finalize','POST',{'policy':policy})[1]
status=req('/api/status')[1]
if not status['configured']:
 storage=req('/api/settings')[1]['storageRoot'];req('/api/setup','POST',{'storageRoot':storage,'allowLan':False,'launchAtStartup':False})
try:req('/api/folders','POST',{'parent':'Photos','name':'Сочи 2014'})
except urllib.error.HTTPError as e:assert e.code==400
check(rejected('/api/folders',{'parent':'../escape','name':'attack'}),'path traversal rejected')
check(rejected('/api/folders',{'parent':'Photos','name':'CON.txt'}),'Windows reserved name rejected')
results=[]
for p in sorted(DATA.glob('*.jpg')):
 result=upload(p.name,p.read_bytes());results.append(result)
photo=results[0]['resultId'];original=req('/api/files/'+photo+'/original')[1]
check(hashlib.sha256(original).hexdigest()==hashlib.sha256((DATA/'IMG_1200.jpg').read_bytes()).hexdigest(),'original bytes unchanged')
dup=upload('duplicate.jpg',original,policy='ask');check(dup['status']=='duplicate','identical bytes detected across filenames');req('/api/uploads/'+dup['id']+'/finalize','POST',{'policy':'skip'})
video=upload('video.MOV',(DATA/'IMG_1200.MOV').read_bytes()+b'\0'*(12*1024*1024),resume=True)
check(video['status']=='complete','multi-chunk video finalized')
r,data=req('/api/files/'+video['resultId']+'/original',headers={'Range':'bytes=0-99'});check(r.status==206 and len(data)==100,'video range request')
album=req('/api/albums','POST',{'name':'Лето у моря'})[1];req('/api/albums/'+album['id']+'/items','POST',{'ids':[photo]});check(req('/api/files?view=all&album='+album['id'])[1]['total']==1,'virtual album returns member')
req('/api/files/'+photo+'/operate','POST',{'action':'favorite'});check(req('/api/files/'+photo)[1]['favorite'],'favorite persisted')
req('/api/files/'+photo+'/operate','POST',{'action':'rename','target':'Побережье.jpg'});req('/api/files/'+photo+'/operate','POST',{'action':'move','target':'Files'});check(req('/api/files/'+photo)[1]['path']=='Files/Побережье.jpg','rename and move physical path')
req('/api/files/'+photo+'/operate','POST',{'action':'trash'});check(req('/api/files?view=trash')[1]['total']>=1,'trash visible');req('/api/files/'+photo+'/operate','POST',{'action':'restore'});check(req('/api/files/'+photo+'/original')[1]==original,'restore content exact')
# Wait for background thumbnails, without lying about original fallback.
for i in range(40):
 _,thumb=req('/api/files/'+photo+'/thumbnail?size=256')
 if thumb[:2]==b'\xff\xd8':break
 time.sleep(.25)
check(thumb[:2]==b'\xff\xd8','generated JPEG thumbnail')
check(req('/api/files?view=all&limit=3')[1]['limit']==3,'bounded pagination')
import io,zipfile
_,bundle=req('/api/downloads?ids='+photo+','+video['resultId'])
with zipfile.ZipFile(io.BytesIO(bundle)) as zip:
 check(zip.read('Files/Побережье.jpg')==original,'streaming ZIP contains exact original')
empty=upload('empty.txt',b'',folder='Files');check(empty['status']=='complete','zero-byte file finalized')
req('/api/folders','POST',{'parent':'Files','name':'Nested-test'})
small=upload('notes.txt',b'folder copy/trash verification',folder='Files/Nested-test')
req('/api/folders/operate','POST',{'path':'Files/Nested-test','action':'copy','target':'Photos'})
check(req('/api/files?view=all&folder=Photos%2FNested-test')[1]['total']==1,'physical folder copy indexed')
trash_group=req('/api/folders/operate','POST',{'path':'Files/Nested-test','action':'trash'})[1]
check(len(trash_group['ids'])==1,'folder trash retains recoverable items')
req('/api/files/'+small['resultId']+'/operate','POST',{'action':'restore'})
check(req('/api/files/'+small['resultId']+'/original')[1]==b'folder copy/trash verification','folder file restore recreates nested directory')

if (DATA/'test.HEIC').exists():
 heic=upload('test.HEIC',(DATA/'test.HEIC').read_bytes())
 for i in range(60):
  _,preview=req('/api/files/'+heic['resultId']+'/thumbnail?size=512')
  if preview[:2]==b'\xff\xd8':break
  time.sleep(.25)
 check(preview[:2]==b'\xff\xd8','HEIC decoded to cached JPEG without replacing original')
 check(req('/api/files/'+heic['resultId']+'/original')[1]==(DATA/'test.HEIC').read_bytes(),'HEIC original preserved')
proxy=req('/api/files/'+video['resultId']+'/proxy','POST',{})[1]
check(req(proxy['url'].removeprefix('/api'))[0].status==200,'compatible H264 video proxy')

# Failed offset must not overwrite confirmed bytes.
r,_=req('/uploads','POST',b'',{'Tus-Resumable':'1.0.0','Upload-Length':'10','Upload-Metadata':'filename '+base64.b64encode(b'a.txt').decode()+',folder '+base64.b64encode(b'Files').decode()});p=urllib.parse.urlparse(r.headers['Location']).path
try:req(p,'PATCH',b'123',{'Tus-Resumable':'1.0.0','Upload-Offset':'9','Content-Type':'application/offset+octet-stream'});raise AssertionError('wrong offset accepted')
except urllib.error.HTTPError as e:check(e.code==409,'incorrect offset rejected')
try:req('/api/folders','POST',{'parent':'Files','name':'csrf'}, {'Origin':'https://evil.example'});raise AssertionError('cross origin accepted')
except urllib.error.HTTPError as e:check(e.code==403,'cross-origin mutation blocked')
(ROOT/'artifacts/api-smoke-result.json').write_text(json.dumps({'passed':len(checks),'checks':checks},ensure_ascii=False,indent=2))
print('\n',len(checks),'API checks passed')
