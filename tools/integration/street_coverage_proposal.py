#!/usr/bin/env python3
"""Review-only geometry proposal; never writes the runtime/backend contract or Unity assets."""
import csv
import hashlib
import html
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "docs/fe-contract-proposals/street-coverage-20261019"
GEO = ROOT / "contract/ids/districts.json"
PARKING = ROOT / "docs/fe-contract-proposals/parking-20261019/parking.json"
PROVENANCE = PARKING.with_name("provenance.json")
geo = json.loads(GEO.read_text())
bays = json.loads(PARKING.read_text())["bays"]
markers = {m["id"]: m for m in json.loads(PROVENANCE.read_text())["markers"]}


def inside(poly, x, z):
    found = False
    for a, b in zip(poly, poly[1:] + poly[:1]):
        if (a[1] > z) != (b[1] > z) and x < (b[0] - a[0]) * (z - a[1]) / (b[1] - a[1]) + a[0]:
            found = not found
    return found


def distance(poly, x, z):
    if inside(poly, x, z):
        return 0.0
    best = math.inf
    for a, b in zip(poly, poly[1:] + poly[:1]):
        ex, ez = b[0] - a[0], b[1] - a[1]
        length = ex * ex + ez * ez
        t = max(0, min(1, ((x - a[0]) * ex + (z - a[1]) * ez) / length)) if length else 0
        best = min(best, math.hypot(x - a[0] - ex * t, z - a[1] - ez * t))
    return best


# Existing mountain rectangle with a connected, review-only south annex.
# Annex bounds enclose all 38 bays outside exact client polygons with >=10m margin.
proposed = [[-720, 380], [-120, 380], [-120, 250], [170, 250], [170, 380], [640, 380], [640, 620], [-720, 620]]
rows = []
for bay in bays:
    near, meters = min(((key, distance(poly, bay["x"], bay["z"])) for key, poly in geo["districts"].items()), key=lambda pair: pair[1])
    if meters == 0:
        continue
    marker = markers[bay["id"]]
    rows.append({**{key: bay[key] for key in ["id", "kind", "x", "y", "z"]}, "nearestDistrict": near,
                 "metersOutsidePolygon": round(meters, 6), "metersBeyondServerBorder": round(max(0, meters - 30), 6),
                 "outsideServerCoverage": meters > 30, "insideProposedMountain": inside(proposed, bay["x"], bay["z"]),
                 "sourceFile": marker["file"], "sourceLine": marker["line"], "sourceMarker": marker["marker"]})
OUT.mkdir(parents=True, exist_ok=True)
with (OUT / "affected-parking.csv").open("w", newline="") as f:
    writer = csv.DictWriter(f, fieldnames=rows[0].keys(), lineterminator="\n"); writer.writeheader(); writer.writerows(rows)
(OUT / "mountain-polygon.proposed.json").write_text(json.dumps(proposed, indent=2) + "\n")
ore = geo["venues"]["ore_refinery"]
summary = {
    "status": "review-only; no runtime geometry or backend implementation changed",
    "backendCommit": "7d2f069a3f432347dee7b9fa33d5431676dfb3c0",
    "inputSha256": {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in [GEO, PARKING, PROVENANCE]},
    "parkingCount": len(bays), "outsideExactClientPolygons": len(rows),
    "outsideServerPolygonsPlus30m": sum(r["outsideServerCoverage"] for r in rows),
    "allAffectedBaysInsideProposedPolygon": all(r["insideProposedMountain"] for r in rows),
    "ore": {"declaredDistrict": ore["district"], "door": ore["door"],
            "metersOutsideDeclaredPolygon": distance(geo["districts"][ore["district"]], *ore["door"]),
            "insideProposedPolygon": inside(proposed, *ore["door"])},
    "proposedAnnex": {"minX": -120, "maxX": 170, "minZ": 250, "maxZ": 380, "additionalAreaSquareMeters": 37700},
}
(OUT / "coverage-summary.json").write_text(json.dumps(summary, indent=2) + "\n")

# Standalone SVG map, x right/east and z up/north, with the relevant approach road enlarged.
def point(x, z):
    return (70 + (x + 470) * 1.12, 640 - (z - 170) * 1.12)


svg = ['<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="760" viewBox="0 0 1200 760">',
       '<rect width="1200" height="760" fill="#f8fafc"/>',
       '<style>text{font-family:system-ui,sans-serif;fill:#172033} .label{font-size:15px}</style>',
       '<text x="32" y="35" font-size="24" font-weight="700">ORE / north approach street coverage — review proposal</text>',
       '<text x="32" y="60" font-size="14">Island metres · north up · existing polygons solid · proposed mountain annex dashed</text>',
       '<defs><clipPath id="map"><rect x="32" y="85" width="1136" height="570"/></clipPath></defs><g clip-path="url(#map)">']
for key in ["timeline", "breakpoint", "builder_hills", "mountain"]:
    coords = " ".join(f"{a:.2f},{b:.2f}" for a, b in [point(*p) for p in geo["districts"][key]])
    svg.append(f'<polygon points="{coords}" fill="#dce5ee" fill-opacity="0.6" stroke="#7c8fa5" stroke-width="2"/>')
coords = " ".join(f"{a:.2f},{b:.2f}" for a, b in [point(*p) for p in proposed])
svg.append(f'<polygon points="{coords}" fill="#c9f2d8" fill-opacity="0.45" stroke="#147c47" stroke-width="3" stroke-dasharray="9 6"/>')
for r in rows:
    x, y = point(r["x"], r["z"]); color = "#d16b00" if r["outsideServerCoverage"] else "#2461bc"
    svg.append(f'<circle cx="{x:.2f}" cy="{y:.2f}" r="4" fill="{color}"><title>{html.escape(r["id"])}</title></circle>')
x, y = point(*ore["door"])
svg.append(f'<circle cx="{x}" cy="{y}" r="8" fill="#be1839"/><text x="{x+13}" y="{y-10}" class="label">ORE door (133.32, 309.44)</text>')
for key, p in [("Breakpoint", (-390, 290)), ("Builder Hills", (235, 310)), ("Mountain", (-80, 470)), ("Timeline", (-180, 190))]:
    x, y = point(*p); svg.append(f'<text x="{x}" y="{y}" class="label">{key}</text>')
svg.extend(['</g>', '<text x="32" y="690" font-size="16" fill="#d16b00">Orange: 36 bays outside server polygons + 30 m</text>',
            '<text x="32" y="715" font-size="16">Blue: 2 more bays outside exact client polygons, within server border</text>',
            '<text x="32" y="740" font-size="14">Candidate uses the existing mountain polygon field. FE/BE must adopt identical reviewed vertices together.</text>', '</svg>'])
(OUT / "coverage-map.svg").write_text("\n".join(svg) + "\n")
print(json.dumps(summary, indent=2))
