#!/usr/bin/env python3
"""Checks that every operation CityLifeApi.ContractCovered names exists in contract/openapi.yaml, and that every
ProposalRoutes entry does NOT (so a proposal never masquerades as contract). Read-only."""
import pathlib, re, sys
REPO = pathlib.Path(__file__).resolve().parents[6]
api = (REPO / "client/Assets/Scripts/Features/CityLife/Net/CityLifeApi.cs").read_text()
spec = (REPO / "contract/openapi.yaml").read_text()

def block(name):
    m = re.search(name + r"\s*=\s*\{(.*?)\};", api, re.S)
    return re.findall(r'"([A-Z]+) ([^"]+)"', m.group(1))

ops = set()
path = None
for line in spec.splitlines():
    m = re.match(r"^  (/[^:]+):\s*$", line)
    if m:
        path = m.group(1)
        continue
    m = re.match(r"^    (get|post|put|patch|delete):", line)
    if m and path:
        ops.add((m.group(1).upper(), re.sub(r"\{[^}]+\}", "{}", path)))

norm = lambda p: re.sub(r"\{[^}]+\}", "{}", p)
bad = 0
for method, p in block("ContractCovered"):
    ok = (method, norm(p)) in ops
    print(("ok      " if ok else "MISSING ") + method + " " + p)
    bad += not ok
for method, p in block("ProposalRoutes"):
    clash = (method, norm(p)) in ops
    print(("CLASH   " if clash else "proposal ") + method + " " + p)
    bad += clash
print("RESULT:", "ok" if bad == 0 else f"{bad} problem(s)")
sys.exit(1 if bad else 0)
