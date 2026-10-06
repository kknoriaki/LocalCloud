#!/usr/bin/env python3
"""Retain dependency metadata and shipped license/notice texts from restored packages."""
from pathlib import Path
import json,shutil,re,xml.etree.ElementTree as ET
r=Path(__file__).resolve().parents[1];out=r/'LICENSES/dependencies';out.mkdir(parents=True,exist_ok=True);rows=[];seen=set()
def save_files(kind,key,directory):
 dest=out/kind/re.sub(r'[^A-Za-z0-9_.-]','_',key)
 for f in directory.rglob('*'):
  if not f.is_file() or 'node_modules' in f.relative_to(directory).parts:continue
  n=f.name.lower()
  if (('license' in n or 'licence' in n or 'copyright' in n or 'notice' in n or n.endswith('.nuspec')) and f.stat().st_size<2000000 and f.suffix.lower() not in {'.dll','.so','.exe','.nupkg','.zip'}):
   target=dest/f.relative_to(directory);target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(f,target)
for asset in r.glob('src/*/obj/project.assets.json'):
 data=json.loads(asset.read_text());folders=[Path(p) for p in data['packageFolders']]
 for key,lib in data['libraries'].items():
  if lib.get('type')!='package' or key in seen:continue
  seen.add(key);directory=next((p/lib['path'] for p in folders if (p/lib['path']).is_dir()),None)
  if not directory:continue
  spec=next(directory.glob('*.nuspec'),None);license='See shipped notices';project=''
  if spec:
   tree=ET.parse(spec)
   for node in tree.iter():
    name=node.tag.split('}')[-1]
    if name=='license':license=(node.text or '').strip()
    if name=='projectUrl':project=(node.text or '').strip()
    if name=='repository' and not project:project=node.attrib.get('url','')
  save_files('nuget',key,directory);rows.append(('NuGet',key,license,project))
web=r/'src/LocalCloud.Web';lock=json.loads((web/'package-lock.json').read_text())
for path,info in lock['packages'].items():
 if not path.startswith('node_modules/'):continue
 directory=web/path
 if not directory.is_dir():continue
 name=path.split('node_modules/')[-1];key=name+'@'+info.get('version','unknown');save_files('npm',key,directory)
 pkg=directory/'package.json';meta=json.loads(pkg.read_text()) if pkg.exists() else {}
 license=info.get('license') or meta.get('license') or 'See shipped notices';repo=meta.get('repository','')
 if isinstance(repo,dict):repo=repo.get('url','')
 rows.append(('npm',key,str(license),str(repo)))
lines=['# Dependency versions and licenses','','Generated from restored NuGet packages and the committed npm lockfile.','The first-party LocalCloud license does not replace these independent terms.','Included upstream copyright/license/notice texts are in dependencies/.','','| Ecosystem | Package | License | Upstream |','| --- | --- | --- | --- |']
for kind,key,license,url in sorted(set(rows)):
 lines.append('| '+ ' | '.join([kind,key,license.replace('|','/'),url.replace('|','/')])+' |')
(r/'LICENSES/DEPENDENCIES.md').write_text('\n'.join(lines)+'\n');print('Dependency notices:',len(rows))
