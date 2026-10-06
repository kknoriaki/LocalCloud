"""Original procedural test images, no third-party photographs. Not included in app library."""
from PIL import Image,ImageDraw
import pathlib,subprocess,shutil
root=pathlib.Path(__file__).resolve().parents[1]/'artifacts/test-data';root.mkdir(parents=True,exist_ok=True)
colors=[((168,198,201),(78,129,157)),((235,206,169),(180,139,112)),((197,208,161),(104,149,117)),((195,187,219),(139,137,182))]
for i in range(16):
 im=Image.new('RGB',(1200,900));d=ImageDraw.Draw(im);sky,ground=colors[i%4]
 for y in range(900):d.line((0,y,1200,y),fill=tuple(round(a*(1-y/900)+b*y/900) for a,b in zip(sky,ground)))
 d.ellipse((780,110,880,210),fill=(248,240,209));d.polygon([(0,650),(300,340+i*9),(660,640),(950,380),(1200,690),(1200,900),(0,900)],fill=ground);d.polygon([(0,810),(320,710),(700,850),(1000,730),(1200,800),(1200,900),(0,900)],fill=tuple(max(0,c-25) for c in ground));im.save(root/f'IMG_{1200+i}.jpg',quality=85)
subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-y','-f','lavfi','-i','testsrc2=size=640x360:rate=24','-t','3','-c:v','libx264','-pix_fmt','yuv420p',str(root/'IMG_1200.MOV')],check=True)
# Optional HEIC fixture; skip when no encoder is available.
if shutil.which('convert'):
 with open(root/'heic-encoder.log','w') as out:subprocess.run(['convert',str(root/'IMG_1201.jpg'),str(root/'test.HEIC')],stdout=out,stderr=out)
print(root)
