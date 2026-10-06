"""Resume browser QA against an isolated temporary fixture created by release13-smoke."""
import pathlib,sys,os,subprocess,time,urllib.request
r=pathlib.Path(__file__).resolve().parents[1];w=pathlib.Path(sys.argv[1]).resolve();assert w.parent==pathlib.Path('/tmp') and w.name.startswith('localcloud-13-')
env={**os.environ,'LOCALCLOUD_CONFIG':str(w/'settings.json'),'LOCALCLOUD_TEST_URL':'http://127.0.0.1:43116','LOCALCLOUD_FIXTURES':str(w/'fixtures.json')};env.pop('LOCALCLOUD_STORAGE',None);env.pop('LOCALCLOUD_PORT',None)
with (w/'ui-debug.log').open('w') as log:
 p=subprocess.Popen([os.environ['LOCALCLOUD_DOTNET'],str(r/'artifacts/linux/LocalCloud.Server.dll')],env=env,stdout=log,stderr=log)
 try:
  for _ in range(100):
   try:urllib.request.urlopen(env['LOCALCLOUD_TEST_URL']+'/api/status');break
   except Exception:time.sleep(.1)
  result=subprocess.run(['node',str(r/'tests/release13-ui.mjs')],env=env,capture_output=True,text=True,timeout=230);print(result.stdout);print(result.stderr[-7000:]);sys.exit(result.returncode)
 finally:p.terminate();p.wait(20)
