#!/usr/bin/env python3
"""Package the public Windows release; never copy user storage or redistribute FFmpeg."""
import argparse,hashlib,json,shutil,zipfile,tempfile,subprocess,os
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--output',default='artifacts/releases');p.add_argument('--delta-baseline',action='append',default=[]);p.add_argument('--nsis',default='makensis');p.add_argument('--skip-setup',action='store_true');p.add_argument('--prepare-only',action='store_true');p.add_argument('--update-only',action='store_true');a=p.parse_args()
r=Path(__file__).resolve().parents[1];out=(r/a.output).resolve();out.mkdir(parents=True,exist_ok=True)
build=r/'artifacts/win-x64';version=(r/'VERSION').read_text().strip()
for f in ['LocalCloud.exe','server/LocalCloud.Server.exe','server/wwwroot/index.html','updater/LocalCloud.Updater.exe']:
 if not (build/f).is_file():raise SystemExit('Missing publish: '+f)
if (build/'server/tools').exists():raise SystemExit('Public packaging excludes FFmpeg binaries. Use a clean application publish.')
def sha(f):
 with f.open('rb') as s:return hashlib.file_digest(s,'sha256').hexdigest()
def entries(base):return [f for f in sorted(base.rglob('*')) if f.is_file() and f.suffix!='.pdb' and not any(x.startswith('.') for x in f.relative_to(base).parts)]
for folder in ['LICENSES']:shutil.copytree(r/folder,build/folder,dirs_exist_ok=True)
for name in ['README.md','README.ru.md','LICENSE','LICENSE.ru.md','NOTICE.md','THIRD_PARTY_NOTICES.md','UPDATE-USER.txt']:shutil.copy2(r/name,build/name)
for name in ['Update-LocalCloud.ps1','Update-LocalCloud.cmd']:shutil.copy2(r/'scripts'/name,build/name)
(build/'docs').mkdir(exist_ok=True)
for name in ['BUILD.md','PUBLISHING.md','RELEASE-NOTES-1.0.md','QA-public-1.0.md']:
 if (r/'docs'/name).exists():shutil.copy2(r/'docs'/name,build/'docs'/name)
if (r/'docs/screenshots/1.0').exists():shutil.copytree(r/'docs/screenshots/1.0',build/'docs/screenshots/1.0',dirs_exist_ok=True)
(build/'scripts').mkdir(exist_ok=True);shutil.copy2(r/'scripts/firewall.ps1',build/'scripts/firewall.ps1')
managed=[f.relative_to(build).as_posix() for f in entries(build) if f.name not in {'version.json','release.json'}]
(build/'version.json').write_text(json.dumps({'product':'LocalCloud','version':version,'releaseChannel':'public','developmentBase':'1.4.0','schemaVersion':4,'rollbackCompatibleSchema':4,'applicationFiles':managed,'updateMethod':'local-package'},indent=2)+'\n')
fullfiles=[{'path':f.relative_to(build).as_posix(),'size':f.stat().st_size,'sha256':sha(f)} for f in entries(build) if f.name!='release.json']
def release(files):return {'product':'LocalCloud','version':version,'releaseChannel':'public','renumberFrom':['1.4.0'],'schemaVersion':4,'rollbackCompatibleSchema':4,'kind':'application-update','files':files}
(build/'release.json').write_text(json.dumps(release(fullfiles),indent=2)+'\n')
# Generate precise file operations for NSIS. Uninstall never recursively erases the install folder.
files=entries(build);dirs=sorted({str(f.parent.relative_to(build)) for f in files},key=lambda x:(len(Path(x).parts),x))
def nsispath(path):return str(path).replace('/','\\').replace('$','$$')
copy=['; Generated from a clean build by release-artifacts.py.']
for directory in dirs:
 suffix='' if directory=='.' else '\\'+nsispath(directory)
 copy.append('CreateDirectory "$INSTDIR'+suffix+'"')
for f in files:
 rel=nsispath(f.relative_to(build));copy.append('CopyFiles /SILENT "$PLUGINSDIR\\payload\\'+rel+'" "$INSTDIR\\'+rel+'"')
(r/'installer/InstallFiles.nsh').write_text('\n'.join(copy)+'\n')
delete=['; Known files only; user data and unknown files are retained.']+['Delete "$INSTDIR\\'+nsispath(f.relative_to(build))+'"' for f in files]
for directory in reversed(dirs):
 if directory!='.':delete.append('RMDir "$INSTDIR\\'+nsispath(directory)+'"')
(r/'installer/UninstallFiles.nsh').write_text('\n'.join(delete)+'\n')
if a.prepare_only:
 print("Manifest and NSIS file lists prepared");raise SystemExit(0)
results=[]
def archive(name,base,filter=lambda f:True,prefix='',source=False):
 target=out/name
 with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED,compresslevel=6,allowZip64=True) as z:
  for f in sorted(base.rglob('*')):
   if f.is_file() and filter(f):z.write(f,prefix+f.relative_to(base).as_posix())
 with zipfile.ZipFile(target) as z:
  bad=z.testzip()
  if bad:raise RuntimeError('Archive corrupted: '+bad)
 results.append({'file':name,'size':target.stat().st_size,'sha256':sha(target)})
if not a.update_only:
 if not a.skip_setup:
  exe=shutil.which(a.nsis) or (a.nsis if Path(a.nsis).is_file() else None)
  if not exe:raise SystemExit('NSIS 3 required. Use --nsis PATH or --skip-setup for archives only.')
  flag='/D' if os.name=='nt' else '-D'
  subprocess.run([exe,flag+'BUILD_DIR='+str(build),flag+'RELEASE_DIR='+str(out),flag+'VERSION='+version,'LocalCloud.nsi'],cwd=r/'installer',check=True)
  setup=out/f'LocalCloud-{version}-Setup-x64.exe';results.append({'file':setup.name,'size':setup.stat().st_size,'sha256':sha(setup)})
 archive(f'LocalCloud-{version}-Windows-x64-Core.zip',build,lambda f:f.suffix!='.pdb')
baselines=[]
for baseline in a.delta_baseline:
 data=json.loads(Path(baseline).read_text());baselines.append({f['path']:f['sha256'] for f in data['files']} if 'files' in data else data)
with tempfile.TemporaryDirectory(prefix='public-update-',dir=out) as work:
 update=Path(work);payload=update/'payload';payload.mkdir();releasefiles=[]
 for f in entries(build):
  name=f.relative_to(build).as_posix()
  if name=='release.json':continue
  digest=sha(f);retained=bool(baselines) and name not in {'LocalCloud.exe','server/LocalCloud.Server.dll','updater/LocalCloud.Updater.exe','updater/LocalCloud.Updater.dll','version.json'} and all(b.get(name)==digest for b in baselines)
  releasefiles.append({'path':name,'size':f.stat().st_size,'sha256':digest,**({'existing':True} if retained else {})})
  if not retained:
   dest=payload/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(f,dest)
 (payload/'release.json').write_text(json.dumps(release(releasefiles),indent=2)+'\n')
 for name in ['Update-LocalCloud.ps1','Update-LocalCloud.cmd','UPDATE-USER.txt']:shutil.copy2(build/name,update/name)
 archive(f'LocalCloud-{version}-Windows-x64-Update.zip',update)
skip={'bin','obj','artifacts','node_modules','tools','.git','__pycache__'}
def sourcefile(f):
 parts=f.relative_to(r).parts
 if parts[0]=='docs':
  allowed={'BUILD.md','PUBLISHING.md','RELEASE-NOTES-1.0.md','QA-public-1.0.md','public10-api-result.json','public10-ui-result.json','upgrade14-to-public10-result.json','package-public10-result.json'}
  if parts[1]=='screenshots':
   if len(parts)<3 or parts[2]!='1.0':return False
  elif parts[1] not in allowed:return False
 return not any(x in skip for x in parts) and parts[:3]!=('src','LocalCloud.Server','wwwroot') and f.suffix not in {'.pdb','.tsbuildinfo','.log','.sqlite'}
archive(f'LocalCloud-{version}-Source.zip',r,sourcefile,'LocalCloud/',True)
(out/f'SHA256SUMS-{version}.txt').write_text('\n'.join(x['sha256']+'  '+x['file'] for x in results)+'\n')
print(json.dumps(results,indent=2))
