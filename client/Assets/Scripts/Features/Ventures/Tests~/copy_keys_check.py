#!/usr/bin/env python3
"""Every literal VStrings key used in the Ventures sources must exist in VStrings.cs (dynamic keys are covered by CopyTests).
Also reports unused keys. Exit 1 on a missing key."""
import pathlib, re, sys
root = pathlib.Path(__file__).resolve().parents[1]
defined = set(re.findall(r'\["([a-zA-Z0-9_.]+)"\]\s*=', (root / "Logic/VStrings.cs").read_text()))
used = {}
for f in list(root.glob("**/*.cs")):
    if "Tests" in f.parts or "Tests~" in f.parts: continue
    for m in re.finditer(r'(?:VStrings\.(?:T|F|Has)|Key\s*=)\(?\s*"([a-zA-Z0-9_.]+)"', f.read_text()):
        used.setdefault(m.group(1), f.name)
missing = sorted(k for k in used if k not in defined and not k.endswith("."))
print(f"defined {len(defined)} keys, used (literal) {len(used)}")
for k in missing: print("MISSING", k, "in", used[k])
sys.exit(1 if missing else 0)
