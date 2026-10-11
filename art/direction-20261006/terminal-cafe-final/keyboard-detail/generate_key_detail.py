"""Original keycap/legend artwork for the unchanged CC0 Kenney plate surfaces."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import hashlib,json,math
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[3]
W,H,S=1024,512,3
linear=lambda v:12.92*v if v<=.0031308 else 1.055*v**(1/2.4)-.055
color=lambda values:tuple(round(max(0,min(1,linear(v)))*255)for v in values)
dark=(.3058824,.3882353,.3882353);medium=(.3686275,.4666667,.4666667)
base=color(medium);gap=color(tuple(v*.43 for v in dark));edge=color(dark);legend=color((.93,.91,.84));top=color(tuple(min(1,v*1.08)for v in medium))
im=Image.new('RGB',(W*S,H*S),base);d=ImageDraw.Draw(im)
fontpath=ROOT/'client/Assets/Resources/UI/Fonts/Manrope-Semibold.ttf'
font=ImageFont.truetype(str(fontpath),14*S);small=ImageFont.truetype(str(fontpath),10*S)
def xy(x,z):return ((x+.23)/.46*W*S,(1-(z-.205)/.15)*H*S)
def key(x0,x1,znear,zfar,label,small_label=False):
 a=xy(x0,zfar);b=xy(x1,znear);pad=1.6*S
 box=(a[0]+pad,a[1]+pad,b[0]-pad,b[1]-pad)
 d.rounded_rectangle(box,radius=3*S,fill=edge)
 face=(box[0]+.8*S,box[1]+.8*S,box[2]-.8*S,box[3]-2.1*S)
 d.rounded_rectangle(face,radius=2.4*S,fill=base)
 d.line((face[0]+3*S,face[1]+.8*S,face[2]-3*S,face[1]+.8*S),fill=top,width=S)
 if label:
  f=small if small_label or len(label)>1 else font
  d.text(((face[0]+face[2])/2,(face[1]+face[3])/2-.5*S),label,font=f,fill=legend,anchor='mm')
near,far=.226028073,.307233759
left,right=-.1988913345,.0910345216
# Existing flat plates retain exactly their source silhouette and contact height.
a=xy(left,far);b=xy(right,near);d.rectangle((a[0],a[1],b[0],b[1]),fill=gap)
rows=[[(x,1)for x in ['~','1','2','3','4','5','6','7','8','9','0','-','=']]+[('Back',2)],
 [('Tab',1.5)]+[(x,1)for x in 'QWERTYUIOP']+[('[',1),(']',1),('\\',1.5)],
 [('Caps',1.75)]+[(x,1)for x in 'ASDFGHJKL']+[(';',1),("'",1),('Enter',2.25)],
 [('Shift',2.25)]+[(x,1)for x in 'ZXCVBNM']+[(',',1),('.',1),('/',1),('Shift',2.75)],
 [('Ctrl',1.25),('Fn',1.25),('Alt',1.25),('',6.25),('Alt',1.25),('Fn',1.25),('Ctrl',2.5)]]
for row_index,row in enumerate(rows):
 assert abs(sum(v for _,v in row)-15)<1e-6
 x=left;step=(right-left)/15;depth=(far-near)/5
 for label,units in row:
  key(x,x+units*step,far-(row_index+1)*depth,far-row_index*depth,label)
  x+=units*step
nl,nr=.1246440255,.1988913182
a=xy(nl,far);b=xy(nr,near);d.rectangle((a[0],a[1],b[0],b[1]),fill=gap)
cells=[(0,0,1,1,'Num'),(1,0,1,1,'/'),(2,0,1,1,'*'),(3,0,1,1,'-'),(0,1,1,1,'7'),(1,1,1,1,'8'),(2,1,1,1,'9'),(3,1,1,2,'+'),(0,2,1,1,'4'),(1,2,1,1,'5'),(2,2,1,1,'6'),(0,3,1,1,'1'),(1,3,1,1,'2'),(2,3,1,1,'3'),(3,3,1,2,'Enter'),(0,4,2,1,'0'),(2,4,1,1,'.')]
for c,r,w,h,label in cells:key(nl+c*(nr-nl)/4,nl+(c+w)*(nr-nl)/4,far-(r+h)*(far-near)/5,far-r*(far-near)/5,label,True)
im.resize((W,H),Image.Resampling.LANCZOS).save(HERE/'keyboard-keys.png')
meta={'status':'original_flat_plate_detail_texture_pending_native_beauty','source':'unchanged Kenney Furniture Kit2.0 computerKeyboard; two original material groups restored','artwork':'Original restrained QWERTY/numpad keycaps and legends on existing plate; no raised geometry','dimensions_px':[W,H],'material_colors_linear':{'metalDark':dark,'metalMedium':medium},'font':'Existing project Manrope Semibold, same retained project license','font_sha256':hashlib.sha256(fontpath.read_bytes()).hexdigest(),'texture_srgb':True,'texture_sha256':hashlib.sha256((HERE/'keyboard-keys.png').read_bytes()).hexdigest(),'contact_target_changed':False,'geometry_changed':False}
(HERE/'appearance.json').write_text(json.dumps(meta,indent=2)+'\n')
print(meta['texture_sha256'])
