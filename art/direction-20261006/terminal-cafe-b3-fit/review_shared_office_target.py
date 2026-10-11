"""Provisional shared office target, using failed-foot V1 typing body exports only."""
from pathlib import Path
import hashlib
import json
import numpy as np
from mesh_geometry import pieces
from surface_witnesses import vertical, summaries

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
BODY = ROOT / 'art/character-candidates/motion-review-20261006/native-seated-goals-v1'
TARGET = HERE / 'adult_depth_48cm-office-target.obj.gz'
cv, cf = pieces(TARGET)[0]
ct = cv[cf]
n = np.cross(ct[:, 1] - ct[:, 0], ct[:, 2] - ct[:, 0])
ny = np.abs(n[:, 1]) / np.maximum(np.linalg.norm(n, axis=1), 1e-12)
seat = ct[(ny > .90) & (ct.mean(axis=1)[:, 1] > .40) & (ct.mean(axis=1)[:, 1] < .49)]
points = np.array([(x, z) for x in np.arange(-.205, .206, .006)
                   for z in np.arange(-.52, -.060, .006)])
seat_y = vertical(points, seat, .40, .49, highest=True)
records = []
for path in sorted(BODY.glob('*_act_type_SeatedContact_Goals-candidate-tier*-phase*.obj.gz')):
    bv, bf = pieces(path)[0]
    bt = bv[bf]
    bt = bt[(bt.min(axis=1)[:, 1] < .65) & (bt.max(axis=1)[:, 1] > .30)]
    by = vertical(points, bt, .30, .65)
    valid = np.isfinite(by) & np.isfinite(seat_y)
    central = valid & (np.abs(points[:, 0]) <= .145) & (points[:, 1] >= -.42) & (points[:, 1] <= -.15)
    gap = by[valid] - seat_y[valid]
    records.append({'look': path.name.split('_')[0], 'file': path.name,
        'posterior_gap_percentiles_m': summaries(by[central] - seat_y[central]),
        'whole_polygon_gap_percentiles_m': summaries(gap),
        'seat_support_y_percentiles_m': summaries(seat_y[valid]),
        'contact_within_5mm_rays': int((np.abs(gap) <= .005).sum()),
        'penetration_beyond_2mm_rays': int((gap < -.002).sum()),
        'lowest_gap_xz_m': points[valid][np.argmin(gap)].tolist()})
assert len(records) == 48
result = {
    'status': 'provisional_body_target_typing_V1_failed_foot_tolerance_not_acceptance',
    'target_sha256': hashlib.sha256(TARGET.read_bytes()).hexdigest(),
    'chair': {'source': 'existing native imported office_chair B2', 'yaw_degrees': 180,
        'scale_xyz': [1, 1, .8], 'center_relative_anchor_m': [0, 0, -.31],
        'seat_is_curved_not_a_plane': True,
        'selected_upper_surface_bounds_min_m': seat.min(axis=(0, 1)).tolist(),
        'selected_upper_surface_bounds_max_m': seat.max(axis=(0, 1)).tolist()},
    'ray_grid_m': .006, 'records': records,
    'limitations': ['Body changes and final foot loop must be re-exported before acceptance.',
        'Provisional 8 phases per look/LOD; no furniture imported or clips changed.',
        'Selected upper-surface extent is a geometric normal/height filter, not an ergonomic certification.']}
(HERE / 'shared-office-target-v1-provisional.json').write_text(json.dumps(result, indent=2) + '\n')
for look in ['Snow', 'Rain']:
    rows = [r for r in records if r['look'] == look]
    print(look, 'posterior_min_gap_range_m',
          [min(r['posterior_gap_percentiles_m']['0'] for r in rows), max(r['posterior_gap_percentiles_m']['0'] for r in rows)],
          'whole_min_gap_range_m', [min(r['whole_polygon_gap_percentiles_m']['0'] for r in rows), max(r['whole_polygon_gap_percentiles_m']['0'] for r in rows)],
          'penetration_rays', sum(r['penetration_beyond_2mm_rays'] for r in rows))
