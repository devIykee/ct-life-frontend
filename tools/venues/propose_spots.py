#!/usr/bin/env python3
"""Append venue-builder spot additions to the FE spot proposal (docs/fe-contract-proposals/ids/spots.json).

Additive, except the listed fe.4 MOVES (Spaces Studio redesign: positions/rotY only): ids, kinds, anims and order are
never changed or renamed. New spot sets are
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
    # fe.4 (round 3, Spaces Studio redesign): two green-room lounge sofa seats beside the neon arch doorway.
    "spaces_studio": [spot("spaces_studio", "greenroom_1", "seat", -11.0, -2.0, HALF, "act_sit"),
                      spot("spaces_studio", "greenroom_2", "seat", -11.0, -0.8, HALF, "act_sit")],
}

# fe.4 (round 3, Spaces Studio redesign to the in-game concept): the only non-additive change in this file.
# Ids, kinds and anims stay; x/z/rotY move so the room reads as a broadcast studio, not rows of chairs:
# seat_1..20 sit on four arcs (r 8.2 / 10.2 / 12.2 / 14.2 m) concentric with the round stage (centre 0, 6.2), facing it;
# stage_4..7 are a shallow talk-show arc facing the room (x as rc1, turned 8-15 degrees inwards, z 5.2-5.75 instead of 6);
# stage_1..3 / 8..10 are speaker pods hugging the stage
# (r 5.65 m), facing it. stage_host, entrance, exit and center are unchanged.
MOVES = {
    "spaces_studio": {
        "seat_1": (-5.25, -0.1, 0.695), "seat_2": (-2.79, -1.51, 0.348), "seat_3": (0.0, -2.0, 0.0), "seat_4": (2.79, -1.51, -0.348),
        "seat_5": (5.25, -0.1, -0.695), "seat_6": (-5.41, -2.45, 0.559), "seat_7": (-2.81, -3.6, 0.279), "seat_8": (0.0, -4.0, 0.0),
        "seat_9": (2.81, -3.6, -0.279), "seat_10": (5.41, -2.45, -0.559), "seat_11": (-5.49, -4.69, 0.467), "seat_12": (-2.82, -5.67, 0.234),
        "seat_13": (0.0, -6.0, 0.0), "seat_14": (2.82, -5.67, -0.234), "seat_15": (5.49, -4.69, -0.467), "seat_16": (-5.55, -6.87, 0.401),
        "seat_17": (-2.83, -7.71, 0.201), "seat_18": (0.0, -8.0, 0.0), "seat_19": (2.83, -7.71, -0.201), "seat_20": (5.55, -6.87, -0.401),
        "stage_1": (-4.99, 3.55, 1.082), "stage_2": (-5.63, 5.71, 1.484), "stage_3": (-5.37, 7.95, 1.885),
        "stage_4": (-3.0, 5.2, 2.88), "stage_5": (-1.3, 5.75, 3.002), "stage_6": (1.3, 5.75, -3.002), "stage_7": (3.0, 5.2, -2.88),
        "stage_8": (5.37, 7.95, -1.885), "stage_9": (5.63, 5.71, -1.484), "stage_10": (4.99, 3.55, -1.082),
    },
    # Timeline Square v2: the fountain now stands on the square's centre, so `center` and `square_center` move just south
    # of it (the camera side), and the six benches on the r = 9 ring turn to face the fountain (rc1 rotY values were
    # 30 to 150 degrees off from facing the centre).
    "timeline_square": {
        "center": (0.0, -5.6, 0.0), "square_center": (0.0, -7.0, 0.0),
        "bench_1": (4.5, 7.79, -2.618), "bench_2": (-4.5, 7.79, 2.618), "bench_3": (-9.0, 0.0, 1.571),
        "bench_4": (-4.5, -7.79, 0.524), "bench_5": (4.5, -7.79, -0.524), "bench_6": (9.0, -0.0, -1.571),
        # fe.5 (Timeline Square v3, compact square): only the spots that stood outside the new 23 x 22 m plaza move.
        "crier_1": (-8.0, 8.6, 0.0), "crier_2": (8.0, 8.6, 0.0), "crier_3": (0.0, 11.0, 0.0),
        "entrance": (0.0, -10.9, 0.0), "exit": (0.0, -11.9, 3.142),
        "pixel_wall": (-9.6, 6.2, 1.571), "billboard_view": (6.2, -9.6, 0.0),
    },
}

# fe.5: walkable rectangles that change with a venue rebuild (the venue's own NavMesh is built from these).
WALKABLE = {
    "timeline_square": [{"x0": -11.5, "z0": -12.4, "x1": 11.5, "z1": 9.8}, {"x0": -3.5, "z0": 9.8, "x1": 3.5, "z1": 12.0}],
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
    moved = 0
    for vid, rects in WALKABLE.items():
        if venues[vid]["walkable"] != rects:
            venues[vid]["walkable"] = rects
            moved += 1
    for vid, moves in MOVES.items():
        for s in venues[vid]["spots"]:
            local = s["id"].split(".", 1)[1]
            if local in moves:
                x, z, rot = moves[local]
                if (s["x"], s["z"], s["rotY"]) != (x, z, rot):
                    s["x"], s["z"], s["rotY"] = x, z, rot
                    moved += 1
    if added or moved or " fe.4 " in doc["_note"]:
        doc["version"] = "1.0.0-rc1+fe.5"
        cut = doc["_note"].find(" fe.4 ")
        if cut >= 0:
            doc["_note"] = doc["_note"][:cut]  # fe.4 notes are rewritten whole on every run
        for note in (" fe.4 (venue builder, 2026-10-08, Spaces Studio redesign): spaces_studio greenroom_1/2 added (green-room lounge sofa);"
                     " seat_1..20 moved onto four arcs facing the round stage, stage_4..7 into a shallow talk-show arc and stage_1..3 / 8..10"
                     " into pods round the stage (ids, kinds and anims unchanged; positions and rotY only). stage_host stands and stage_4..7"
                     " sit on the raised stage (floor y 0.22 m); seat_11..15 / 16..20 sit on risers (0.12 / 0.24 m).",
                     " fe.4 also (Timeline Square v2): timeline_square.center -> (0, -5.6) and square_center -> (0, -7.0), off the new"
                     " central fountain; bench_1..6 rotY now face the fountain (positions unchanged).",
                     " fe.5 (venue builder, 2026-10-08, Timeline Square v3): the square is compact (walkable 23 x 22.2 m plaza plus the"
                     " street to the town hall steps, x -3.5..3.5 z 9.8..12); crier_1/2/3, entrance, exit, pixel_wall and billboard_view"
                     " move inside it (ids, kinds, anims and rotY unchanged); center, square_center and the benches are unchanged.",
                     " fe.5 also (Spaces Studio): stage_5/stage_6 move 0.3 m apart (x -1.3 / 1.3) so the walkable gap between the"
                     " talk-show chairs keeps their side-step approach points reachable now that seat furniture is carved from the NavMesh."):
            if note.strip() not in doc["_note"]:
                doc["_note"] += note
    out = json.dumps(doc, indent=2, ensure_ascii=False) + "\n"
    # Guard: every pre-existing id keeps its exact record.
    before = json.loads(original)["venues"]
    for vid, body in before.items():
        old = {s["id"]: s for s in body["spots"]}
        new = {s["id"]: s for s in doc["venues"][vid]["spots"]}
        for sid, rec in old.items():
            moves = MOVES.get(vid, {})
            if sid.split(".", 1)[1] in moves:
                keep = {k: v for k, v in rec.items() if k not in ("x", "z", "rotY")}
                assert {k: v for k, v in new[sid].items() if k not in ("x", "z", "rotY")} == keep, f"changed id/kind/anim of {sid}"
                continue
            assert new.get(sid) == rec, f"changed existing spot {sid}"
        assert vid in WALKABLE or doc["venues"][vid]["walkable"] == body["walkable"], f"changed walkable {vid}"
    with open(PROPOSAL, "w") as f:
        f.write(out)
    print(f"added {added} spots, moved {moved}")


if __name__ == "__main__":
    main()
