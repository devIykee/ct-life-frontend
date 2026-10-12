"""Derive exact Snow/Rain outfit tint masks from preserved cast part-ID maps.

Run in Blender 5.1.2: no blend, mesh, material or original texture is overwritten.
Output masks share the exact shipped SlotUV atlas; eyes, lashes, mouths, accessories,
shoe soles and costume hardware are excluded from their neighbouring color regions.
"""
import sys,json,hashlib
from pathlib import Path
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
import cast_common as C
import cast_tex as T
import cast_spec as S
ROOT=C.REPO/'client/Assets/Art/characters/palettes/humans'
ROOT.mkdir(parents=True,exist_ok=True)
SOURCE=C.REPO/'client/Assets/Art/characters/cast'
manifest=json.loads((SOURCE/'manifest.json').read_text())
REGIONS=['skin','hair','top','bottom','shoes','brows']
CHANNELS={'skin':(0,0),'hair':(0,1),'top':(0,2),'bottom':(0,3),'shoes':(1,0),'brows':(1,2)}
records=[]
def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def hexcolor(c): return '#'+''.join(f'{round(float(x)*255):02x}' for x in np.clip(c,0,1))
for lid,record in manifest['looks'].items():
    if lid not in ('snow_everyday','rain_breeze') and not lid.startswith('player_'): continue
    fam=record['family']; resolution=record['atlas_size']; assert resolution==1024
    masks=[np.zeros((resolution,resolution,4),np.float32) for _ in range(2)]
    supports={r:0 for r in REGIONS}
    occupied=set()
    mapHashes={}
    for part,slot in record['slots'].items():
        if slot in occupied:
            assert part=='legs'; continue
        occupied.add(slot)
        info=S.part_info(fam,part)
        mapPath=C.fam_dir(fam)/('base_id.npy' if info['src']=='base' else 'maps/'+info['src']+'.npz')
        mapHashes[str(mapPath.relative_to(C.EVID))]=sha(mapPath)
        t=T.crop(fam,slot,prefix=info['src'])
        assignments={r:np.zeros(t.cov.shape,bool) for r in REGIONS}
        if slot=='HEAD':
            assignments['skin']=t.mask('face','scalp')
            assignments['brows']=t.mask('brows')
        elif slot=='HAIR': assignments['hair']=t.cov & ~np.isin(t.sub,[C.SUB_IDS['band']])
        elif slot=='SKIN': assignments['skin']=t.cov
        elif slot in ('TOP','OUTER'):
            assignments['top']=t.cov & ~np.isin(t.sub,[C.SUB_IDS[n] for n in ('lining','dickey','acc_metal','acc_dark','acc_glow','acc_tex')])
        elif slot=='BOTTOM': assignments['bottom']=t.cov
        elif slot=='SHOES': assignments['shoes']=t.cov & ~np.isin(t.sub,[C.SUB_IDS[n] for n in ('sole','detail','acc_metal')])
        packed=[np.zeros((*t.cov.shape,4),np.float32) for _ in range(2)]
        for region,mask in assignments.items():
            channel,component=CHANNELS[region]; packed[channel][...,component]=mask
        r0,r1,c0,c1=C.slot_px(slot,resolution)
        for channel in range(2):
            small,cov=T._down(packed[channel],t.cov,T.RES//resolution)
            masks[channel][r0:r1,c0:c1]=C.dilate(small,cov>0,10)
    albedoPath=SOURCE/'looks'/lid/(lid+'_BaseColor.png')
    albedo=C.read_image(albedoPath,3)
    assert albedo.shape[:2]==(resolution,resolution)
    references={}
    for region,(channel,component) in CHANNELS.items():
        mask=masks[channel][...,component]>.95
        supports[region]=int(mask.sum())
        references[region]=hexcolor(np.median(albedo[mask],axis=0)) if mask.any() else '#ffffff'
    assert supports['skin']>1000 and supports['top']>1000 and supports['shoes']>50,(lid,supports)
    total=masks[0].sum(axis=2)+masks[1].sum(axis=2)
    assert total.max()<1.005,(lid,total.max())
    for i,mask in enumerate(masks): C.write_png(ROOT/(lid+f'_Mask{i}.png'),mask)
    head='head_snow' if lid=='snow_everyday' else 'head_rain' if lid=='rain_breeze' else 'head_'+lid
    if lid=='snow_everyday': look=['skin_tone3',head,'hair_snow_bun','top_vneck_charcoal','bottom_denim_light','shoes_sneakers_blue','acc_none']
    elif lid=='rain_breeze': look=['skin_tone3',head,'hair_rain_ponytail','top_sleeveless_white','bottom_denim_fitted','shoes_canvas_ochre','acc_scarf_sky']
    else: look=['skin_'+record['skin_tone'],head,'hair_'+lid,'top_'+lid,'bottom_'+lid,'shoes_'+lid,'acc_none']
    records.append({'id':lid,'headId':head,'look':look,'family':fam,'resolution':resolution,
        'files':{'mask0':lid+'_Mask0.png','mask1':lid+'_Mask1.png'},
        'sha256':{f'mask{i}':sha(ROOT/(lid+f'_Mask{i}.png')) for i in range(2)},
        'sourceAlbedoSHA256':sha(albedoPath),'sourceManifestSHA256':sha(SOURCE/'manifest.json'),
        'sourceParts':record['parts'],'sourceSlots':record['slots'],'sourceMapSHA256':mapHashes,
        'regionPixels':supports,'regions':[r for r in REGIONS[:-1] if supports[r]>0],'tint_reference':references})
    print('MASKS',lid,supports,flush=True)
# Reuse exact identical masks across outfit bindings; Unity then loads one texture per unique hash.
seen={}
for record in records:
    for role,digest in record['sha256'].items():
        record['files'][role]=seen.setdefault((role,digest),record['files'][role])
(ROOT/'manifest.json').write_text(json.dumps({'version':1,'source':'Preserved Claude cast maps; same SlotUV as shipped Snow/Rain outfits','models':records},indent=2)+'\n')
print('EXPORTED_HUMAN_MASKS',len(records),flush=True)
