"""Replay reviewed Unity placement corrections in deterministic island dressing output.

Original proto + XYZ matching keeps repeat runs idempotent. This is an authored overlay,
not a new placement generator; scale, rotation, tint and all unrelated records stay intact.
"""
import json
from pathlib import Path


def apply_repairs(doc, moves=None):
    if moves is None:
        path = Path(__file__).with_suffix('.json')
        moves = json.loads(path.read_text())['moves'] if path.exists() else []
    by_proto = {}
    for move in moves:
        by_proto.setdefault(move['proto'], []).append(move)
    changed = 0
    for group in doc.get('instances', []):
        records = group['d']
        for i in range(0, len(records), 10):
            original = records[i:i + 3]
            for move in by_proto.get(group['proto'], []):
                if sum((a - b) ** 2 for a, b in zip(original, move['from'])) >= 0.0004:
                    continue
                destination = move['to']
                for box in doc.get('buildingBoxes', []):
                    if abs(box[0] - original[0]) < 0.02 and abs(box[2] - original[2]) < 0.02:
                        box[:3] = [box[k] + destination[k] - original[k] for k in range(3)]
                records[i:i + 3] = destination
                changed += 1
                break
    for move in moves:
        for key, field in [('facades', 'wallDecor'), ('shopSigns', 'shopSigns')]:
            for panel in move.get(key, []):
                for row in doc.get(field, []):
                    if sum((row[k] - panel['from'][k]) ** 2 for k in range(3)) < 0.0004 and abs(row[3] - panel['from'][3]) < 0.01:
                        row[:3] = panel['to'][:3]
                        changed += 1
    return changed
