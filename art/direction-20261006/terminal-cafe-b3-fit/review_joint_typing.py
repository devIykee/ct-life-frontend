"""Compare actual native body-fit exports against the frozen shared chair target.

This is discrete surface evidence, not physics contact or continuous-time acceptance.
"""
from pathlib import Path
import hashlib
import json
import numpy as np
from mesh_geometry import pieces, chair_intersection_count
from surface_witnesses import vertical, summaries

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
BODY = ROOT / 'art/character-candidates/motion-review-20261006/native-typing-work-body-fit-v1'
TARGET = HERE / 'adult_depth_48cm-office-target.obj.gz'
cv, cf = pieces(TARGET)[0]
ct = cv[cf]
n = np.cross(ct[:, 1] - ct[:, 0], ct[:, 2] - ct[:, 0])
ny = np.abs(n[:, 1]) / np.maximum(np.linalg.norm(n, axis=1), 1e-12)
seat = ct[(ny > .90) & (ct.mean(axis=1)[:, 1] > .40) & (ct.mean(axis=1)[:, 1] < .49)]
assert len(seat) > 0
points = np.array([(x, z) for x in np.arange(-.205, .206, .006)
                   for z in np.arange(-.493, -.070, .006)])
seat_y = vertical(points, seat, .40, .49, highest=True)
records = []
hashes = {str(TARGET.relative_to(ROOT)): hashlib.sha256(TARGET.read_bytes()).hexdigest()}
for path in sorted(BODY.glob('*_TypingWorkBodyFit-candidate-tier*-phase*.obj.gz')):
    bv, bf = pieces(path)[0]
    bt = bv[bf]
    subset = bt[(bt.min(axis=1)[:, 1] < .70) & (bt.max(axis=1)[:, 1] > .30)
                & (bt.max(axis=1)[:, 2] > -.60) & (bt.min(axis=1)[:, 2] < .12)]
    body_y = vertical(points, subset, .30, .70)
    valid = np.isfinite(body_y) & np.isfinite(seat_y)
    central = valid & (np.abs(points[:, 0]) <= .145) & (points[:, 1] >= -.42) & (points[:, 1] <= -.15)
    gap = body_y[valid] - seat_y[valid]
    contact = valid & (np.abs(body_y-seat_y) <= .005)
    contact_points = points[contact]
    intersections = chair_intersection_count((bv, bf), (cv, cf), 0, min_y=-.01, max_y=1.2)
    records.append({
        'look': path.name.split('_')[0], 'file': path.name,
        'paired_rays': int(valid.sum()), 'posterior_rays': int(central.sum()),
        'whole_seat_gap_percentiles_m': summaries(gap),
        'posterior_gap_percentiles_m': summaries(body_y[central] - seat_y[central]),
        'penetration_beyond_2mm_rays': int((gap < -.002).sum()),
        'contact_within_5mm_rays': int(contact.sum()),
        'contact_xz_min_m': contact_points.min(axis=0).tolist() if len(contact_points) else None,
        'contact_xz_max_m': contact_points.max(axis=0).tolist() if len(contact_points) else None,
        'intersecting_chair_triangles_all_heights': intersections,
        'lowest_gap_xz_m': points[valid][np.argmin(gap)].tolist() if len(gap) else None,
    })
    hashes[str(path.relative_to(ROOT))] = hashlib.sha256(path.read_bytes()).hexdigest()
assert len(records) == 48
result = {
    'status': 'sampled_native_body_fit_against_cpu_transformed_native_chair_not_final_acceptance',
    'coordinate_frame': 'immutable actor-root local metres',
    'chair': 'one shared office_chair, yaw180 centerZ-.31 scaleXYZ(1,1,.8), original height; curved frozen target mesh',
    'chair_support_triangles': len(seat), 'ray_grid_m': .006,
    'limitations': [
        'Chair is a transformed native B2 export; native B3 placement remains independently gated.',
        'Eight phases per look and three body LODs, not continuous-time geometry.',
        'Edge-triangle crossing of all chair triangles is checked; enclosed volumes and coplanar overlap are not proven absent.',
        'Vertical surface gap and posterior region are geometric witnesses, not pressure/contact mechanics.',
        'Native rendered furniture/body review and production-controller integration remain pending.',
    ], 'input_sha256': hashes, 'records': records,
}
(HERE / 'joint-typing-body-fit-v1-surfaces.json').write_text(json.dumps(result, indent=2) + '\n')
for look in ['Snow', 'Rain']:
    rows = [r for r in records if r['look'] == look]
    print(look, 'cases', len(rows), 'posterior_min_gap_range_m',
          [min(r['posterior_gap_percentiles_m']['0'] for r in rows), max(r['posterior_gap_percentiles_m']['0'] for r in rows)],
          'contact_ray_range', [min(r['contact_within_5mm_rays'] for r in rows), max(r['contact_within_5mm_rays'] for r in rows)],
          'penetrations', sum(r['penetration_beyond_2mm_rays'] for r in rows),
          'triangle_crossings', [r['intersecting_chair_triangles_all_heights'] for r in rows])
