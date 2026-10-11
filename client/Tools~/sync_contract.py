#!/usr/bin/env python3
"""Bundles the contract registries + config into client/Assets/Resources/Contract/config_snapshot.json, the base the
client merges every GET /config over (and its offline fallback).

Sources (2026-10-05): contract/ = the backend developer's contract-v1.0.0-rc1 (read-only copy, see contract/SOURCE.md),
plus the FE overlay docs/fe-contract-proposals/ids/{anims,sfx,spots}.json, which WINS for those three FE-owned
registries until he merges the proposal. Run after any contract re-sync:  python3 client/Tools~/sync_contract.py
"""
import json, pathlib

repo = pathlib.Path(__file__).resolve().parents[2]
contract = repo / "contract"
overlay_dir = repo / "docs" / "fe-contract-proposals" / "ids"
out = repo / "client" / "Assets" / "Resources" / "Contract" / "config_snapshot.json"

def load_dir(d, skip=()):
    res = {}
    for f in sorted((contract / d).glob("*.json")):
        if f.name.endswith(".schema.json") or f.stem in skip:
            continue
        res[f.stem] = json.loads(f.read_text())
    return res

ids = load_dir("ids")
config = load_dir("config")
overlaid = []
for name in ("anims", "sfx", "spots"):
    f = overlay_dir / f"{name}.json"
    if f.exists():
        ids[name] = json.loads(f.read_text())
        overlaid.append(name)
version = next((v.get("version") for v in ids.values() if isinstance(v, dict) and v.get("version")), "0.0.0")
payload = {"version": version, "minClientVersion": "0.0.0", "contractVersion": version, "degraded": [],
           "ids": ids, "config": config}
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text(json.dumps(payload, separators=(",", ":")))
print(f"wrote {out.relative_to(repo)}: {len(ids)} registries ({', '.join(overlaid)} from FE overlay), "
      f"{len(config)} config files, contract {version}, {out.stat().st_size} bytes")
