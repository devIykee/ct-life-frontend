"""Reconcile existing exports; no binary regeneration or backend/catalog registration.
python3 v2_manifest.py --validation /fresh/validation_v2.json [--output /review]
"""
import argparse, copy, hashlib, json, struct, sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from v2_common import EXPORT, V2, CTS_BONES, CTS_PARENT, CTS_DEFORM, HUMANOID, CAPS, MAX_INFL, SOCKETS, TINT_REGIONS, HAIR_COLORS, CLOTH_COLORS
import v2_spec as SPEC

def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,d): p.write_text(json.dumps(d,indent=2)+'\n')
def read(p): return json.loads(p.read_text())
def asset(rel):
    p=EXPORT/rel
    if p.resolve().is_relative_to(EXPORT.resolve()) and p.is_file(): return p
    raise ValueError('Missing or unsafe asset: '+rel)

def default_parts(base):
    return [k for k,p in base['parts'].items() if not p.get('kind')]

def compose(base, hair=None, facial=(), accessories=()):
    """Art-only selection arithmetic. No remote ID encoding. Reject incompatible/missing parts."""
    selected=default_parts(base)
    library=base.get('hair_library',{})
    if hair is not None:
        if hair!='none' and hair not in library.get('hair',{}): raise ValueError('unknown hairstyle for this base')
        selected=[p for p in selected if p!='hair']
        if hair!='none': selected.append(library['hair'][hair]['part'])
    if len(set(facial))!=len(facial) or len(set(accessories))!=len(accessories): raise ValueError('duplicate selection')
    for style in facial:
        if style not in library.get('facial',{}): raise ValueError('unknown facial hair for this base')
        selected.append(library['facial'][style]['part'])
    sockets=set()
    for key in accessories:
        if key not in base['accessories']: raise ValueError('unknown accessory')
        acc=base['accessories'][key]
        if acc['socket'] in sockets: raise ValueError('two accessories use the same socket')
        sockets.add(acc['socket'])
        if acc['flags'].get('replaces_head'):
            selected=[p for p in selected if base['parts'][p]['slot']!='head' and not p.startswith('facial_')]
        if acc['flags'].get('hides_hair'): selected=[p for p in selected if p!='hair' and not p.startswith('hair_')]
    totals={t:sum(base['parts'][p]['triangles'][t] for p in selected)+sum(base['accessories'][a]['triangles'] for a in accessories) for t in CAPS}
    return {'parts':selected,'accessories':list(accessories),'triangles':totals,
            'over_budget':{t:n-CAPS[t] for t,n in totals.items() if n>CAPS[t]}}

def generate(validation_path,output):
    validation=read(validation_path)
    if validation.get('revision')!='fresh-import-20261009' or validation.get('source_drift'):
        raise ValueError('Requires pinned fresh-import receipt without source drift (failures remain reportable).')
    files={p.relative_to(EXPORT).as_posix():sha(p) for p in (EXPORT/'bases').rglob('*') if p.is_file()}
    if files!=validation['input_hashes']: raise ValueError('Fresh import receipt does not match all current inputs')
    bases={}
    refs=set()
    reconciliation={}
    for bid in SPEC.BASES:
        source='bases/'+bid+'/'+bid+'.json'; base=read(asset(source));refs.add(source)
        if base['id']!=bid: raise ValueError('base identity mismatch')
        base['authoring_metadata_sha256']=sha(asset(source))
        base['source_metadata_triangles']={t:r['triangles'] for t,r in base['tiers'].items()}
        changes=[]; errors=[]
        for kind,rel in base['files'].items():
            refs.add(rel); actual=validation['files'][rel]
            for tier,total in actual.get('tier_totals',{}).items():
                if base['tiers'][tier]['triangles']!=total:
                    changes.append({'file':rel,'tier':tier,'authoring':base['tiers'][tier]['triangles'],'fresh_import':total})
                base['tiers'][tier]['triangles']=total
            errors.extend({'file':rel,'error':e} for e in actual['errors'] if 'disagrees with base metadata' not in e)
        for key,part in base['parts'].items():
            rel=part['fbx'];refs.add(rel); actual=validation['files'][rel]
            if part['triangles']!=actual.get('tier_totals'):
                changes.append({'file':rel,'authoring':part['triangles'],'fresh_import':actual.get('tier_totals')})
            part['authoring_triangles']=part['triangles'];part['triangles']=actual['tier_totals']
            part['asset_sha256']=sha(asset(rel))
            errors.extend({'file':rel,'error':e} for e in actual['errors'] if e!='part triangles disagree with metadata')
        for key,acc in base['accessories'].items():
            rel=acc['fbx'];refs.add(rel);acc['asset_sha256']=sha(asset(rel))
            errors.extend({'file':rel,'error':e} for e in validation['files'][rel]['errors'])
        for rel in base['textures'].values(): refs.add(rel)
        base['texture_dimensions']={k:list(struct.unpack('>II',asset(v).read_bytes()[16:24])) for k,v in base['textures'].items()}
        base['validation']={'structural_errors':errors,'metadata_reconciliations':changes,'unity_import':'not_reviewed',
                            'animation_and_visual_fit':'not_accepted','default_assembly':compose(base)}
        base['source_geometry_findings'] = []
        if bid in ('hero_assassin','hero_tactical'):
            base['source_geometry_findings'].append({
                'kind':'duplicate_face_indices_in_exported_fbx',
                'counts':{'lod2':3} if bid=='hero_assassin' else {'lod1':2,'lod2':4},
                'evidence':'../provenance/fbx-triangle-reconciliation-20261009.json',
                'scope':'Exact duplicate vertex-index sets account for observed Blender import-count reductions. Original FBX remains unchanged; Unity handling unverified.'})
        if bid=='hero_tactical':
            base['source_geometry_findings'].append({
                'kind':'placeholder_hands','source':'scripts/v2_adapters.py:mitten_hands',
                'scope':'Source gloves are removed. Replacements are palm/thumb boxes with rigid hand-bone weights, no articulated finger geometry. Not art or interaction accepted.'})
            base['source_geometry_findings'].append({
                'kind':'misplaced_head_weighted_source_component',
                'scope':'Object_102/material,722 vertices at shin height in source POSE and REST. A separate exclusion candidate exists but is not selected or accepted.'})
        if compose(base)['triangles']!={t:x['triangles'] for t,x in base['tiers'].items()}:
            raise ValueError('Default part sum differs from whole merged tiers: '+bid)
        reconciliation[bid]={'errors':errors,'metadata_reconciliations':changes}
        bases[bid]=base
    orphans=sorted(set(files)-refs)
    pal=read(EXPORT/'skin_palette.json')
    proof=read(V2/'provenance/sketchfab-verification-20261009.json')
    for bid,spec in SPEC.HEROES.items():
        if proof[bid]['local_source_sha256']!=sha(Path(spec['file'])): raise ValueError('Hero source changed: '+bid)
    groups={}
    for bid,b in bases.items():
        groups[bid]={
            'asset_base_id':bid,'wire_head_id':None,'wire_look':None,'wire_status':'unregistered_no_accepted_alias',
            'default_part_keys':default_parts(b),
            'complete_head_parts':[p for p in default_parts(b) if b['parts'][p]['slot']=='head'],
            'slots':{'Head':[p for p in default_parts(b) if b['parts'][p]['slot']=='head'],
                     'Hair':[p for p in default_parts(b) if b['parts'][p]['slot']=='hair'],
                     'Top':['top'] if 'top' in b['parts'] else [],'Bottom':['bottom'] if 'bottom' in b['parts'] else [],
                     'Shoes':['feet'] if 'feet' in b['parts'] else []},
            'hair_styles':b.get('hair_library',{}).get('hair',{}), 'facial_hair_additions':b.get('hair_library',{}).get('facial',{}),
            'cross_base_swaps':False,'skeleton_proportions_family':bid,
            'parts':b['parts'],'accessories':b['accessories'],'sockets':b['sockets'],
            'palette_asset':'skin_palette.json','palette_sha256':sha(EXPORT/'skin_palette.json'),
            'material_inputs':b['textures'],'tint_regions_present':list(b['tint_defaults'])}
    mapdoc={'version':'art-runtime-map-20261009','purpose':'Unity integration input, not a shipped UI or server catalogue',
        'current_wire_fields':['skinTone','headId','hairId','topId','bottomId','shoesId','accId'],
        'current_wire_limits':['No baseId, scale, facial list, accent or full socket array is transmitted.',
            'One accId cannot represent all simultaneous accessory sockets.',
            'The existing UI collapses left/right shoulder and hand sockets; this map preserves sides.',
            'skin_01 through skin_10 collide by ID with different current UI hex values. Explicit palette translation is required.',
            'No seven-base aliases exist in the current CharacterRegistry, CreatorLookCatalog or item catalogue.'],
        'binding_requirements':['Create an own-proportion Humanoid Avatar per base; shared bone names do not prove interchangeable bind poses.',
            'Assemble either the merged default FBX or the complete selected parts, never both.',
            'Head includes eyes and brows where supplied. Facial hair adds to a selected hairstyle.',
            'Use same-base fitted part meshes; cross-fitted UBC hair is already exported separately for each receiving base.',
            'Remap every renderer bone and bindpose to this base; validate missing bones explicitly.',
            'Accessories have their own FBX material named acc_palette. Bind to the base atlas deliberately; one-material composition is not yet implemented.',
            'Select only one LOD per part. Count all visible parts and accessories against total caps before enabling a combination.',
            'Replaces-head/hides-hair flags require renderer visibility changes; covered faces and protrusion need an actual visual check.',
            'Require exact complete-art resolution; do not keep an old/default actor when a requested selection is missing.',
            'Do not expose modular options merely because catalogue IDs resolve. Native pose/tint/hide/restore controls remain required.'],
        'bases':groups}
    schema={'version':2,'scope':'local art-selection proposal only; NOT the backend Look schema',
        'example':{'base':'ubc_m','skin':'skin_06','hair':'Hair_Buzzed','facial':['Hair_Beard'],
                   'tints':{'hair':'black','top':'orange','bottom':'denim','shoes':'white'},'accessories':[]},
        'fields':{'base':'exact manifest base ID; own skeleton proportions','skin':'this export skin_palette.json ID, requires explicit UI mapping',
                  'hair':'per-base hair_library.hair key, or none; null retains default','facial':'per-base facial keys, additive, not a hairstyle replacement',
                  'tints':'hair palette for hair, cloth palette for top/bottom/shoes/accent; only existing masks have a visible effect',
                  'accessories':'per-base accessory keys; at most one per exact socket; composed triangle gate required'},
        'compatibility':'No cross-base clothing swaps are declared. Only existing per-base files are catalogued.',
        'wire_handoff':'runtime_part_map.json; aliases remain null until exact IDs and renderer support are integrated',
        'tint_preview':{'space':'linear RGB, masks non-color; source textures albedo/emission sRGB',
            'mask0':'R skin, G hair, B top, A bottom','mask1':'R shoes, G accent',
            'formula':'sequential out = lerp(current, tint * min(luminance(current) / max(0.03, luminance(region_default)), 1.6), mask)',
            'source':'scripts/v2_render.py:toon_material; a Blender preview, not a shipped Unity shader guarantee'}}
    humanoid={'skeleton':'CTS','bones':CTS_BONES,'parent':CTS_PARENT,'deform_bones':CTS_DEFORM,'unity_humanoid':HUMANOID,
              'notes':['Each base has distinct bone positions and its own Avatar requirement.',
                       'Blender character faces -Y; *_l is +X. Confirm imported native facing rather than guessing a rotation.',
                       'Source T/A references require actual Unity Avatar configuration and pose testing. No native acceptance here.']}
    manifest={'version':'v2-reconciled-20261009','status':'existing_exports_reconciled_not_runtime_accepted',
        'skeleton':'cts_humanoid_map.json','customizer_schema':'customizer_schema.json','runtime_part_map':'runtime_part_map.json',
        'validation':{'path':'validation_v2.json','sha256':sha(validation_path),'all_pass':validation['all_pass'],
                      'original_failures_preserved':validation['summary'],'reconciliation':reconciliation},
        'budgets':{'triangles':CAPS,'max_bone_influences':MAX_INFL,'materials_per_character_target':1,
                   'actual_texture_size':1024,'crowd_texture_size_target':512,'crowd_512_exports_exist':False,
                   'scope':'combined visible assembly, including accessories; material merging and crowd downsizing pending'},
        'palettes':{'skin':pal['tones'],'skin_method':pal['method'],'skin_neighbour_delta_e2000':pal['neighbour_delta_e2000'],'hair':HAIR_COLORS,'cloth':CLOTH_COLORS},
        'tint_regions':TINT_REGIONS,'sockets':SOCKETS,'bases':bases,
        'catalogue':{bid:{'label':b['label'],'gender':b['gender'],'archetypes':b['archetypes'],'swap_family':bid,'wire_head_id':None} for bid,b in bases.items()},
        'balance':{'bases':len(bases),'female':sum(b['gender']=='f' for b in bases.values()),'male':sum(b['gender']=='m' for b in bases.values())},
        'inventory':{'referenced_files':len(refs),'fbx_files_on_disk':len(validation['files']),'preserved_unreferenced_files':orphans,
                     'assets':{r:{'sha256':files[r],'bytes':asset(r).stat().st_size} for r in sorted(refs)}},
        'provenance':'../provenance/README.md'}
    output.mkdir(parents=True,exist_ok=True)
    for name,data in [('manifest_v2.json',manifest),('customizer_schema.json',schema),('runtime_part_map.json',mapdoc),('cts_humanoid_map.json',humanoid)]: write(output/name,data)
    if validation_path.resolve()!=(output/'validation_v2.json').resolve(): (output/'validation_v2.json').write_bytes(validation_path.read_bytes())
    print(json.dumps({'bases':len(bases),'referenced_files':len(refs),'preserved_orphans':orphans,'output':str(output)}))
    return manifest,mapdoc,schema

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--validation',required=True,type=Path);p.add_argument('--output',type=Path,default=EXPORT)
    a=p.parse_args();generate(a.validation,a.output)
