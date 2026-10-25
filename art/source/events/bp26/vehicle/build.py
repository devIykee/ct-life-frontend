"""Our own signage for the Breakpoint bus (Sketchfab 2e32927de49e42fe913ec01c097fb257, CC-BY): replaces the model's real
ads (an airline, a gum maker), the operator's fleet name and number, and the real route blind. Each output is the same
size and orientation as the source image it replaces (images are stored upside down for glTF), applied before atlasing by
world_v3_process.py (world_v3_config 'retex').

  1. dump the source images: art/pipeline/blender.sh <dump_tex.py> -- <bus.glb> <dir>   (Image_14/16/17 needed)
  2. python3 -I art/source/events/bp26/vehicle/build.py <dir>          writes <name>.html here
  3. Chrome headless screenshots each html (1024 x 1024, transparent background) to <name>.png (see run() below)
Fonts: Inter, Manrope (OFL, client/Assets/Resources/UI/Fonts).
"""
import os, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..', '..', 'client', 'Assets', 'Resources', 'UI', 'Fonts'))
CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'

HEAD = f"""<!doctype html><html><head><meta charset="utf-8"><style>
@font-face{{font-family:Inter;font-weight:700;src:url('file://{FONTS}/Phone/Inter-Bold.ttf')}}
@font-face{{font-family:Geist;font-weight:600;src:url('file://{FONTS}/Events/Geist-SemiBold.ttf')}}
@font-face{{font-family:GeistMono;font-weight:500;src:url('file://{FONTS}/Events/GeistMono-Medium.ttf')}}
@font-face{{font-family:Inter;font-weight:600;src:url('file://{FONTS}/Phone/Inter-SemiBold.ttf')}}
@font-face{{font-family:Manrope;font-weight:600;src:url('file://{FONTS}/Manrope-Semibold.ttf')}}
html,body{{margin:0;width:1024px;height:1024px;background:transparent;overflow:hidden}} canvas{{display:block}}
</style></head><body><canvas id="out" width="1024" height="1024"></canvas><script>
const W=1024,H=1024,BLACK='#000000',PURPLE='#aa67fb',WHITE='#ffffff',GREY='#a2a2a2',GOLD='#d8b56a';
function steps(c,x,y,w,h,cell,col,seed){{let r=seed;const rnd=()=>{{r=(r*16807)%2147483647;return r/2147483647}};let lv=1,xx=x;c.fillStyle=col;while(xx<x+w){{const bw=[1,1,2,3,5,8][Math.floor(rnd()*6)]*cell;lv=Math.max(0,Math.min(Math.floor(h/cell),lv+[-1,0,0,1,1][Math.floor(rnd()*5)]));c.fillRect(xx,y+h-lv*cell,Math.min(bw,x+w-xx),lv*cell);if(rnd()<0.35&&lv)c.fillRect(xx,y+h-(lv+2)*cell,cell,cell);xx+=bw;}}}}
function brk(c,x,y,w,h,col){{const g=h*1.1,s=h*0.7,a=w/2-g/2;c.fillStyle=col;c.beginPath();c.moveTo(x,y);c.lineTo(x+a+s,y);c.lineTo(x+a,y+h);c.lineTo(x,y+h);c.fill();
 c.beginPath();c.moveTo(x+a+g+s,y);c.lineTo(x+w,y);c.lineTo(x+w,y+h);c.lineTo(x+a+g,y+h);c.fill();}}
function txt(c,t,x,y,font,col,align,track){{c.font=font;c.fillStyle=col;c.textAlign=align||'left';c.textBaseline='alphabetic';
 if(track){{c.letterSpacing=track}} else {{c.letterSpacing='0px'}} c.fillText(t,x,y);}}
async function main(){{await document.fonts.load('700 40px Inter');await document.fonts.load('600 40px Geist');await document.fonts.load('500 40px GeistMono');await document.fonts.load('600 40px Manrope');await document.fonts.load('600 40px Inter');
 const work=document.createElement('canvas');work.width=W;work.height=H;const c=work.getContext('2d');
 const src=new Image();src.src=SRC;await src.decode();
 // the source as stored is upside down: turn it the right way up, edit, turn it back
 c.save();c.translate(0,H);c.scale(1,-1);c.drawImage(src,0,0,W,H);c.restore();
 edit(c);
 const o=document.getElementById('out').getContext('2d');o.translate(0,H);o.scale(1,-1);o.drawImage(work,0,0);}}
"""
TAIL = "main();</script></body></html>"

EDITS = {
    # the ads: the portrait poster (left) and the long upper-deck side panel (top band)
    'bus_ads': ('Image_16', """function edit(c){
 c.fillStyle=BLACK;c.fillRect(4,384,452,636);
 c.fillStyle=PURPLE;c.fillRect(4,384,452,330);
 steps(c,4,654,452,72,18,BLACK,7);
 txt(c,'LONDON · 15–17 NOV',36,440,'500 22px GeistMono',BLACK,'left','2px');
 txt(c,'Break',30,530,'600 96px Geist',BLACK,'left','-4px');txt(c,'point',30,615,'600 96px Geist',BLACK,'left','-4px');
 txt(c,'2026',34,800,'600 100px Geist',WHITE,'left','-4px');
 txt(c,'OLYMPIA',36,930,'500 26px GeistMono',WHITE,'left','2px');txt(c,'CONVENTION CENTRE',36,968,'500 20px GeistMono',GREY,'left','2px');
 c.fillStyle=PURPLE;c.fillRect(0,24,W,265);
 steps(c,0,190,W,99,18,BLACK,11);
 txt(c,'Breakpoint 2026',36,160,'600 104px Geist',BLACK,'left','-4px');
}"""),
    # the route blind: the destination, the via list and the route number
    'bus_dest': ('Image_14', """function edit(c){
 c.fillStyle='#151515';c.fillRect(0,0,W,H);
 txt(c,'OLYMPIA',512,960,'700 92px Inter','#f2f2ee','center','2px');
 const via=[['Breakpoint 2026','Expo Row'],['Keynote Stage','Hacker House'],['Olympia Way','Kensington'],['Breakpoint Park','Conference Centre']];
 via.forEach((r,i)=>{txt(c,r[0],70,800-i*92,'600 50px Inter','#ecece6','left');txt(c,r[1],540,800-i*92,'600 50px Inter','#ecece6','left');});
 c.fillStyle='#000';c.fillRect(60,114,350,240);txt(c,'26',235,320,'700 210px Inter','#f2f2ee','center','-4px');
}"""),
    # the fleet name and number (gold with a dark outline, as the original); "Please pay as you..." is kept
    'bus_decals': ('Image_17', """function edit(c){
 c.clearRect(0,355,W,150);c.clearRect(20,118,400,124);
 c.font='700 84px Inter';c.letterSpacing='10px';c.textAlign='center';c.lineWidth=7;c.strokeStyle='#1d1608';
 c.strokeText('CT ISLAND TRANSIT',512,474);c.fillStyle=GOLD;c.fillText('CT ISLAND TRANSIT',512,474);
 c.font='700 92px Inter';c.letterSpacing='6px';c.textAlign='left';c.strokeText('BP26',40,222);c.fillText('BP26',40,222);
}"""),
    'bus_decals_mask': ('Image_17', """function edit(c){ c.clearRect(0,0,W,H); c.fillStyle='#ffff00'; c.fillRect(0,0,W,H); }"""),
}


def run(src_dir):
    for name, (image, js) in EDITS.items():
        src = os.path.join(src_dir, image + '.png')
        html = os.path.join(HERE, name + '.html')
        open(html, 'w').write(HEAD + f"const SRC='file://{src}';\n" + js + TAIL)
        out = os.path.join(HERE, name + '.png')
        for _ in range(4):   # headless Chrome now and then shoots before the async draw lands: retry a blank frame
            subprocess.run([CHROME, '--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
                            '--default-background-color=00000000', '--virtual-time-budget=8000', '--allow-file-access-from-files',
                            '--window-size=1024,1024', '--screenshot=' + out, 'file://' + html], check=True, capture_output=True)
            if name.endswith('_mask') or os.path.getsize(out) > 20000:
                break
        print('wrote', out)


if __name__ == '__main__':
    run(sys.argv[1])
