"""Original decorative desktop triptychs, no live data or third-party interface copies."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import hashlib,json,math
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[2];FONT=ROOT/'client/Assets/Resources/UI/Fonts/Manrope-Semibold.ttf';OUT=HERE/'Textures';OUT.mkdir(exist_ok=True)
W,H=512,296;INK=(38,57,54);CREAM=(226,216,191);GOLD=(158,124,77);MUTED=(111,133,121)
THEMES=[('DRAFTS','A quieter timeline','KEEP','BUILDING','notes'),('READING','Bookmarks for later','LOW','NOISE','reading'),('MAKING','One small thing','SHIP','SOMETHING','build'),('FOCUS','Coffee, then code','DEEP','WORK','focus'),('NOTES','Ideas worth keeping','OPEN','NOTES','notes'),('AFTER HOURS','The tabs can wait','LOG','OFF','focus'),('EDITING','Make it simpler','LESS','BUT BETTER','build'),('WEEKEND','Nothing urgent','TOUCH','GRASS','reading')]
def font(s):return ImageFont.truetype(str(FONT),s)
def panel(title,seed,dark=False):
 bg=INK if dark else CREAM;fg=CREAM if dark else INK;im=Image.new('RGB',(W,H),bg);d=ImageDraw.Draw(im);d.text((28,20),title,font=font(12),fill=MUTED if dark else GOLD);d.line((28,46,484,46),fill=GOLD,width=1);return im,d,fg
files={}
for i,(label,caption,a,b,kind)in enumerate(THEMES):
 left,d,fg=panel(label,i)
 d.text((28,63),caption,font=font(21),fill=fg)
 if kind=='notes':
  for row,length in enumerate([390,340,372,280,315]):d.rounded_rectangle((30,112+row*26,30+length,117+row*26),radius=2,fill=MUTED)
  d.line((31,238,177,238),fill=GOLD,width=3)
 elif kind=='reading':
  for col,h in enumerate([108,136,124,154,118]):
   x=35+col*84;d.rectangle((x,242-h,x+58,242),fill=[MUTED,GOLD,INK][col%3]);d.line((x+12,251-h,x+12,230),fill=CREAM,width=2)
 elif kind=='build':
  for x,y,w in [(32,123,130),(210,123,122),(340,190,124)]:d.rounded_rectangle((x,y,x+w,y+42),radius=8,outline=GOLD,width=2)
  d.line((162,144,190,144,190,210,340,210),fill=MUTED,width=2);d.line((332,144,400,144,400,190),fill=MUTED,width=2)
 else:
  for j in range(3):d.ellipse((40+j*144,112,148+j*144,220),outline=[GOLD,MUTED,INK][j],width=3)
  d.text((30,253),'A LITTLE ROOM TO THINK.',font=font(11),fill=GOLD)
 center,d,fg=panel('TERMINAL / '+str(i+1).zfill(2),i,True)
 size=58 if len(b)<10 else 39
 d.text((28,78),a,font=font(58),fill=CREAM);d.text((28,151),b,font=font(size),fill=CREAM);d.line((30,253,110,253),fill=GOLD,width=3)
 right,d,fg=panel('SCRATCHPAD',i)
 # Original quiet abstract composition, rather than fake market charts or live messages.
 cx,cy=255,171
 for j in range(4):
  r=24+j*20;d.arc((cx-r,cy-r,cx+r,cy+r),i*23+j*28,290+i*11+j*15,fill=GOLD if j%2 else MUTED,width=6)
 d.ellipse((cx-5,cy-5,cx+5,cy+5),fill=INK)
 d.text((28,261),'SAVE THE GOOD IDEAS.',font=font(11),fill=GOLD)
 atlas=Image.new('RGB',(W*3,H));atlas.paste(left,(0,0));atlas.paste(center,(W,0));atlas.paste(right,(W*2,0));p=OUT/('Desktop'+str(i+1).zfill(2)+'.png');atlas.save(p);files[p.name]=hashlib.sha256(p.read_bytes()).hexdigest()
meta={'status':'original_project_art_pending_native_material_and_camera_review','source':'Original layouts, diagrams and copy in create_desktop_art.py; no third-party UI, prices, market chart, claimed person or live activity','font':'Existing licensed project Manrope Semibold; font not redistributed by this candidate','font_sha256':hashlib.sha256(FONT.read_bytes()).hexdigest(),'atlas_px':[1536,296],'panel_count_per_station':3,'unique_station_atlases':8,'source_file_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'files':files};(HERE/'artwork-provenance.json').write_text(json.dumps(meta,indent=2)+'\n');print(json.dumps(meta,indent=2))
