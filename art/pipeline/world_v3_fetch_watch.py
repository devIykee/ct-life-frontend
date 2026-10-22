"""World v3: finish Sketchfab downloads after a 429 cool-down (resumable, one uid per browser call).

1. Run this watcher:  python3 -I art/pipeline/world_v3_fetch_watch.py <uid,uid,...>   (default: every world_v3_config uid
   whose raw/sketchfab/<uid>/<uid>.glb is missing). It picks up ~/Downloads/sfdl_<uid>.json files, curls the signed S3 URL at
   once (it expires within minutes; never printed) into ct-life/art/raw/sketchfab/<uid>/<uid>.glb and deletes the json.
2. In your own Sketchfab tab (logged in), run per uid, at most a handful per hour (the download endpoint is rate limited
   per account, HTTP 429):
     const u='<uid>'; const r=await fetch('/i/models/'+u+'/download',{credentials:'include'});
     const g=(await r.json()).latest.glb; const p=(g.length>1&&g[0].size>40e6)?g[1]:g[0];
     const s=await (await fetch(p.url,{credentials:'include'})).json();
     const a=document.createElement('a'); a.href=URL.createObjectURL(new Blob([JSON.stringify({url:s.url})]));
     a.download='sfdl_'+u+'.json'; document.body.appendChild(a); a.click(); a.remove();
   (Chrome freezes timers in background tabs, so drive it per call rather than with a setTimeout loop.)
3. Then: Blender -b --factory-startup -P art/pipeline/world_v3_process.py -- <uid|prefix,...>; python3 world_v3_manifest.py
"""
import json, os, glob, time, subprocess, threading, sys
R = '/Users/mac/Documents/codes/projects/ct-life/art/raw/sketchfab'
DL = os.path.expanduser('~/Downloads')
if len(sys.argv) > 1:
    want = set(sys.argv[1].split(","))
else:
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import world_v3_config as C
    want = {u for u in C.PACKS if len(u) == 32 and not os.path.exists(f"{R}/{u}/{u}.glb")}
done = set(); running = {}
def get(u, url):
    os.makedirs(f'{R}/{u}', exist_ok=True)
    dst = f'{R}/{u}/{u}.glb'
    r = subprocess.run(['curl', '-s', '-f', '--retry', '2', '-o', dst + '.part', url])
    if r.returncode == 0:
        os.rename(dst + '.part', dst); print(u, 'ok', os.path.getsize(dst) // 1000, 'kB', flush=True)
    else:
        print(u, 'fail', r.returncode, flush=True)
    done.add(u)
t0 = time.time()
while time.time() - t0 < 4 * 3600 and len(done) < len(want):
    for f in glob.glob(DL + '/sfdl_*.json'):
        u = os.path.basename(f)[5:-5].split(' ')[0].split('(')[0]
        if u in want and u not in running:
            try:
                url = json.load(open(f))['url']
            except Exception:
                continue
            os.remove(f)
            th = threading.Thread(target=get, args=(u, url)); th.start(); running[u] = th
            print(u, 'started', flush=True)
    time.sleep(1)
for th in running.values(): th.join()
print('ALL DONE', len(done), flush=True)
