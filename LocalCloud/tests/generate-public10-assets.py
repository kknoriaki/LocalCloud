"""Procedural public-release fixtures; no personal or third-party photographs/music."""
from pathlib import Path
from PIL import Image,ImageDraw
import subprocess
root=Path(__file__).resolve().parents[1]/'artifacts/public10/library'
(root/'Music').mkdir(parents=True,exist_ok=True);(root/'Photos').mkdir(exist_ok=True)
im=Image.new('RGB',(400,400),(45,74,145));d=ImageDraw.Draw(im);d.ellipse((90,90,310,310),fill=(240,170,80));im.save(root/'Photos/Обложка.jpg',quality=90)
for name,metadata in [('Русская.mp3',['-metadata','title=Русская песня','-metadata','artist=Илья','-metadata','album=Тестовый альбом']),('Без-тегов.mp3',[])]:
 subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-y','-f','lavfi','-i','sine=frequency=440:duration=45','-c:a','libmp3lame',*metadata,str(root/'Music'/name)],check=True)
print(root)
