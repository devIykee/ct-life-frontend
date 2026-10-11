#!/usr/bin/env python3
"""Append venue-builder spot additions to the FE spot proposal (docs/fe-contract-proposals/ids/spots.json).

Strictly additive: existing venues, ids, positions and order are never changed or renamed. New spot sets are
appended for venues that had none (checker_kiosk, yacht_club, penthouse_row); new ids are appended to existing
sets. Re-running is idempotent. The JSON is written with the same formatting (indent=2) so diffs stay minimal.

Run: python3 tools/venues/propose_spots.py
"""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PROPOSAL = os.path.join(ROOT, "docs", "fe-contract-proposals", "ids", "spots.json")
PI, HALF = 3.142, 1.571


def spot(vid, sid, kind, x, z, rot=0.0, anim=None):
    s = {"id": f"{vid}.{sid}", "kind": kind, "x": x, "z": z, "rotY": rot}
    if anim:
        s["anim"] = anim
    return s


def generic(vid, x0, z0, x1, z1):
    """entrance / exit / center for a new venue with a south door at x = 0."""
    return [spot(vid, "entrance", "exit", 0, z0 + 1, 0), spot(vid, "exit", "exit", 0, z0, PI), spot(vid, "center", "stand", 0, 0, 0.0)]


ADDITIONS = {
    "ratio_arena": [spot("ratio_arena", "corner_red", "stand", -2.5, 0, HALF), spot("ratio_arena", "corner_blue", "stand", 2.5, 0, -HALF)]
    + [spot("ratio_arena", f"bleacher_{i + 1}", "seat", x, 8.0, PI, "act_sit") for i, x in enumerate([-7, -5, -3, -1, 1, 3, 5, 7])],
    "conference_hall": [spot("conference_hall", "stage_speaker", "stage", 0, 8, PI, "loco_idle")]
    + [spot("conference_hall", f"seat_{r * 6 + i + 1}", "seat", x, z, 0, "act_sit")
       for r, z in enumerate([0.0, -2.5]) for i, x in enumerate([-7.5, -5, -2.5, 2.5, 5, 7.5])]
    + [spot("conference_hall", "booth_1", "npc", -10.6, -5, HALF), spot("conference_hall", "booth_2", "npc", 10.6, -5, -HALF)],
    "ct_jail": [spot("ct_jail", f"bunk_{i + 1}", "seat", x, 8.6, PI, "act_sit") for i, x in enumerate([-10.2, -4.2, 1.8, 7.8])]
    + [spot("ct_jail", "kyc_desk", "npc", -9, -5, HALF), spot("ct_jail", "mugshot", "stand", 7, -6, PI)],
    "hackathon_house": [spot("hackathon_house", f"desk_{i + 1}", "terminal", x, -2.75, 0, "act_type") for i, x in enumerate([-6, -2, 2, 6])]
    + [spot("hackathon_house", f"desk_{i + 5}", "terminal", x, -0.25, PI, "act_type") for i, x in enumerate([-6, -2, 2, 6])]
    + [spot("hackathon_house", "couch_1", "seat", -9.5, 6.2, 0, "act_sit"), spot("hackathon_house", "couch_2", "seat", -8.0, 6.2, 0, "act_sit")],
    "audit_row": [spot("audit_row", f"audit_desk_{i + 1}", "terminal", x, 7.6, 0, "act_type") for i, x in enumerate([-7.5, -2.5, 2.5, 7.5])],
    "discord_heights": [spot("discord_heights", f"sofa_{i + 1}", "seat", x, -3.0, 0, "act_sit") for i, x in enumerate([-4.6, -3.4, 3.4, 4.6])],
    "vc_row": [spot("vc_row", f"pitch_{i + 1}", "seat", x, 6.6, 0, "act_sit") for i, x in enumerate([-2, 0, 2])]
    + [spot("vc_row", f"pitch_{i + 4}", "seat", x, 8.6, PI, "act_sit") for i, x in enumerate([-2, 0, 2])]
    + [spot("vc_row", "partner_desk", "terminal", -9, -4, 0, "act_type")],
    "airdrop_fields": [spot("airdrop_fields", "bench_1", "seat", -8, -5, HALF, "act_sit"), spot("airdrop_fields", "bench_2", "seat", 8, -5, -HALF, "act_sit")],
    "sybil_barn": [spot("sybil_barn", "hay_1", "seat", -9, -3, HALF, "act_sit"), spot("sybil_barn", "hay_2", "seat", 9, -3, -HALF, "act_sit")],
    "cabal_lounge": [spot("cabal_lounge", f"booth_{i + 1}", "seat", x, 8.2, PI, "act_sit") for i, x in enumerate([-7.4, -5.6, -0.9, 0.9, 5.6, 7.4])],
}

NEW_VENUES = {
    "checker_kiosk": {
        "walkable": [{"x0": -8, "z0": -6, "x1": 8, "z1": 6}],
        "spots": generic("checker_kiosk", -8, -6, 8, 6)
        + [spot("checker_kiosk", f"kiosk_{i + 1}", "stand", x, 3.4, 0.0) for i, x in enumerate([-4, 0, 4])]
        + [spot("checker_kiosk", "bench_1", "seat", -6, -2, HALF, "act_sit"), spot("checker_kiosk", "bench_2", "seat", 6, -2, -HALF, "act_sit"),
           spot("checker_kiosk", "attendant", "npc", 6.5, 3.5, -HALF)],
    },
    "yacht_club": {
        "walkable": [{"x0": -12, "z0": -8, "x1": 12, "z1": 8}],
        "spots": generic("yacht_club", -12, -8, 12, 8)
        + [spot("yacht_club", f"lounger_{i + 1}", "seat", x, 5.4, 0, "act_sit") for i, x in enumerate([-9, -6, -3, 3, 6, 9])]
        + [spot("yacht_club", f"bar_{i + 1}", "seat", -9.0, z, -HALF, "act_sit") for i, z in enumerate([-4, -2.5, -1, 0.5])]
        + [spot("yacht_club", "bartender", "npc", -11.3, -1.75, HALF), spot("yacht_club", "gangway", "stand", 0, 7.3, 0.0)],
    },
    "penthouse_row": {
        "walkable": [{"x0": -12, "z0": -9, "x1": 12, "z1": 9}],
        "spots": generic("penthouse_row", -12, -9, 12, 9)
        + [spot("penthouse_row", f"door_{i + 1}", "stand", x, 8.2, 0.0) for i, x in enumerate([-7, 0, 7])]
        + [spot("penthouse_row", f"lounger_{i + 1}", "seat", 3.4, z, HALF, "act_sit") for i, z in enumerate([-4.6, -3.0, -1.4, 0.2])]
        + [spot("penthouse_row", "sofa_1", "seat", -9.6, -3.0, HALF, "act_sit"), spot("penthouse_row", "sofa_2", "seat", -9.6, -1.2, HALF, "act_sit"),
           spot("penthouse_row", "concierge", "npc", -5, -5.4, PI)],
    },
}


def main():
    with open(PROPOSAL) as f:
        original = f.read()
    doc = json.loads(original)
    venues = doc["venues"]
    added = 0
    for vid, spots in ADDITIONS.items():
        existing = {s["id"] for s in venues[vid]["spots"]}
        for s in spots:
            if s["id"] not in existing:
                venues[vid]["spots"].append(s)
                added += 1
    for vid, body in NEW_VENUES.items():
        if vid not in venues:
            venues[vid] = body
            added += len(body["spots"])
        else:
            existing = {s["id"] for s in venues[vid]["spots"]}
            for s in body["spots"]:
                if s["id"] not in existing:
                    venues[vid]["spots"].append(s)
                    added += 1
    if added:
        doc["version"] = "1.0.0-rc1+fe.2"
        note = " fe.2 (venue builder, 2026-10-08): additive spots for the venue dioramas; new sets checker_kiosk, yacht_club, penthouse_row."
        if note.strip() not in doc["_note"]:
            doc["_note"] += note
    out = json.dumps(doc, indent=2, ensure_ascii=False) + "\n"
    # Guard: every pre-existing id keeps its exact record.
    before = json.loads(original)["venues"]
    for vid, body in before.items():
        old = {s["id"]: s for s in body["spots"]}
        new = {s["id"]: s for s in doc["venues"][vid]["spots"]}
        for sid, rec in old.items():
            assert new.get(sid) == rec, f"changed existing spot {sid}"
        assert doc["venues"][vid]["walkable"] == body["walkable"], f"changed walkable {vid}"
    with open(PROPOSAL, "w") as f:
        f.write(out)
    print(f"added {added} spots")


if __name__ == "__main__":
    main()
