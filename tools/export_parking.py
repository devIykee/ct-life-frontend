#!/usr/bin/env python3
"""Export public ParkedCar markers to the backend ParkingBay shape, without modifying contract/.

Uses current serialized cell prefabs (including uncommitted relocations). Empty VehicleSpawn bays
belong to the explicit offline showcase; they are deliberately excluded. Fails on non-identity
ancestor transforms so a future hierarchy change cannot silently export local-space coordinates.
"""
import argparse
import collections
import hashlib
import json
import math
import pathlib
import re
import struct
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[1]
DOCUMENT = re.compile(r"^--- !u!(\d+) &(\d+)\n(.*?)(?=^--- !u!|\Z)", re.M | re.S)

def f32(value):
    return struct.unpack("f", struct.pack("f", float(value)))[0]

def vector(body, field, axes="xyz"):
    value = re.search(r"^  " + re.escape(field) + r": \{([^}]+)\}", body, re.M)
    if not value:
        raise ValueError("Missing " + field)
    parts = dict(re.findall(r"(\w+): ([^,}]+)", value[1]))
    return tuple(f32(parts[axis]) for axis in axes)

def reference(body, field):
    value = re.search(r"^  " + re.escape(field) + r": \{fileID: (\d+)\}", body, re.M)
    if not value:
        raise ValueError("Missing " + field)
    return value[1]

def scalar(body, field):
    value = re.search(r"^  " + re.escape(field) + r": (.*)$", body, re.M)
    if not value:
        raise ValueError("Missing " + field)
    return value[1]

def away(value):
    return int(math.copysign(math.floor(abs(value) + 0.5), value))

def parked_id(kind, x, z):
    return f"park_{kind}_{away(x * 10)}_{away(z * 10)}"

def spec_for(classes, model, x, z):
    # VehicleCatalog.Specs declaration order; Flies excludes heli/drone, including Roadstar as runtime does.
    for kind in ("compact", "suv", "sports", "roadstar", "scooter"):
        if kind in model.lower():
            return kind
    if classes & 16 and classes != 31:
        return "roadstar"
    if classes == 8:
        return "scooter"
    choices = [name for bit, name in ((1, "compact"), (2, "suv"), (4, "sports")) if classes & bit]
    if not choices:
        choices = ["scooter" if classes & 8 else "compact"]
    # Mathf.RoundToInt: banker's rounding of the float32 multiplication; unchecked Int32 then positive mask.
    pick = ((round(f32(x * 10)) * 73856093) ^ (round(f32(z * 10)) * 19349663)) & 0x7fffffff
    return choices[pick % len(choices)]

def export(root):
    island = root / "client/Assets/Resources/World/Island/CTIsland.prefab"
    roots = [m[3] for m in DOCUMENT.finditer(island.read_text()) if m[1] == "4" and reference(m[3], "m_Father") == "0"]
    if len(roots) != 1 or vector(roots[0], "m_LocalPosition") != (0, 0, 0) or vector(roots[0], "m_LocalRotation", "xyzw") != (0, 0, 0, 1) or vector(roots[0], "m_LocalScale") != (1, 1, 1):
        raise ValueError("Island root must preserve the published island coordinate frame")
    bays, records, sources, skipped = [], [], {str(island.relative_to(root)): hashlib.sha256(island.read_bytes()).hexdigest()}, []
    for path in sorted((root / "client/Assets/Resources/World/Island/Cells").glob("*.prefab")):
        raw = path.read_bytes(); text = raw.decode()
        docs = {m[2]: (int(m[1]), m[3], text.count("\n", 0, m.start()) + 1) for m in DOCUMENT.finditer(text)}
        transforms = {reference(body, "m_GameObject"): key for key, (typ, body, _) in docs.items() if typ == 4}
        relevant = False
        for _, (typ, body, line) in docs.items():
            if typ != 114 or "CTLife.World::CTLife.World.ParkedCar\n" not in body:
                continue
            relevant = True
            game = reference(body, "m_GameObject")
            if scalar(docs[game][1], "m_IsActive") != "1" or scalar(body, "m_Enabled") != "1":
                raise ValueError(f"Inactive marker needs explicit handling: {path}:{line}")
            transform = docs[transforms[game]][1]
            parent = reference(transform, "m_Father")
            while parent != "0":
                ancestor = docs[parent][1]
                if (vector(ancestor, "m_LocalPosition") != (0, 0, 0) or
                    vector(ancestor, "m_LocalRotation", "xyzw") != (0, 0, 0, 1) or
                    vector(ancestor, "m_LocalScale") != (1, 1, 1)):
                    raise ValueError(f"Nonidentity marker ancestor: {path}:{line} transform {parent}")
                if scalar(docs[reference(ancestor, "m_GameObject")][1], "m_IsActive") != "1":
                    raise ValueError(f"Inactive marker ancestor: {path}:{line}")
                parent = reference(ancestor, "m_Father")
            x, y, z = vector(transform, "m_LocalPosition")
            model = scalar(body, "model"); classes = int(scalar(body, "vehicleClass"))
            spec = spec_for(classes, model, x, z)
            kind = "moped" if spec == "scooter" else spec
            source = {"file": str(path.relative_to(root)), "line": line, "marker": scalar(docs[game][1], "m_Name"), "model": model, "vehicleClass": classes}
            if kind not in ("compact", "suv", "sports", "moped"):
                skipped.append({**source, "kind": kind, "reason": "premium entitlement unavailable"}); continue
            qx, qy, qz, qw = vector(transform, "m_LocalRotation", "xyzw")
            norm = math.sqrt(qx*qx + qy*qy + qz*qz + qw*qw)
            qx, qy, qz, qw = (q / norm for q in (qx, qy, qz, qw))
            yaw = math.atan2(2*(qw*qy + qx*qz), 1-2*(qy*qy+qx*qx)) % (2*math.pi)
            bay = {"id": parked_id(kind, x, z), "kind": kind, "x": x, "y": f32(y + f32(0.05)), "z": z, "yaw": yaw}
            if not all(math.isfinite(bay[k]) for k in ("x", "y", "z", "yaw")):
                raise ValueError("Nonfinite bay " + bay["id"])
            bays.append(bay); records.append({"id": bay["id"], **source})
        if relevant:
            sources[str(path.relative_to(root))] = hashlib.sha256(raw).hexdigest()
    duplicate = [key for key, count in collections.Counter(b["id"] for b in bays).items() if count > 1]
    if duplicate:
        raise ValueError("Duplicate stable parking IDs: " + ", ".join(duplicate))
    if not bays:
        raise ValueError("No public parking markers found")
    return sorted(bays, key=lambda b: b["id"]), records, sources, skipped

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=pathlib.Path, default=ROOT)
    parser.add_argument("--out", type=pathlib.Path, default=ROOT / "docs/fe-contract-proposals/parking-20261019")
    args = parser.parse_args(); root = args.root.resolve()
    bays, markers, sources, skipped = export(root)
    args.out.mkdir(parents=True, exist_ok=True)
    document = {"_owner": "FE", "_provisional": True, "version": "0.0.2", "_note": "Proposed public parking export from actual ParkedCar markers. Island metres; yaw radians. Stable IDs match ParkedCars/IslandProtocol. Includes marker Y + 0.05m runtime spawn offset. Not yet merged or seeded upstream; no premium vehicles.", "bays": bays}
    raw = json.dumps(document, indent=2) + "\n"
    (args.out / "parking.json").write_text(raw)
    source_files = ["client/Assets/Scripts/World/Vehicles/ParkedCars.cs", "client/Assets/Scripts/World/Vehicles/VehicleSpec.cs", "client/Assets/Scripts/Net/Realtime/IslandRealtimeService.cs", "client/Assets/Scripts/World/Island/Markers/ParkedCar.cs"]
    sources.update({name: hashlib.sha256((root / name).read_bytes()).hexdigest() for name in source_files})
    provenance = {"frontendCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(), "includesWorkingTreeEdits": True,
                  "backendSchemaCommit": "7d2f069a3f432347dee7b9fa33d5431676dfb3c0", "parkingSha256": hashlib.sha256(raw.encode()).hexdigest(),
                  "counts": dict(sorted(collections.Counter(b["kind"] for b in bays).items())), "sourceSha256": sources, "markers": markers, "excluded": skipped,
                  "limits": ["Serialized pose/schema export; not a new physics or driveability certification.", "Empty VehicleSpawn showcase slots excluded; only actual public ParkedCar decor exported.", "Backend merge/restart required to seed these; published parking registry remains empty."]}
    (args.out / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
    print(json.dumps({"publicBays": len(bays), "excludedPremium": len(skipped), "kinds": provenance["counts"], "output": str(args.out)}))

if __name__ == "__main__":
    main()
