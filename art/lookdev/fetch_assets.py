#!/usr/bin/env python3
"""Download the CC0 source assets used by the CT Life lookdev renders into art/lookdev/src/.

Stdlib only. No logins: itch.io free "name your own price" packs are fetched through the
public "No thanks, just take me to the downloads" flow; Google Drive folders through the
public embedded folder view; Poly Haven through its public API (api.polyhaven.com).

Usage:  python3 art/lookdev/fetch_assets.py            (skips files that already exist)
"""
import http.cookiejar
import json
import os
import re
import sys
import urllib.parse
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "src")
UA = "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) ct-life-lookdev/1.0"

# ---- what we fetch ---------------------------------------------------------------------
ITCH = {  # slug -> filename substring to pick (free Standard tier only)
    "downtown-city-megakit": "Standard",
    "stylized-nature-megakit": "Standard",
}
DRIVE = {  # local dir -> public folder id (Quaternius packs, CC0)
    "quaternius_background_posed_humans": "18dWyZlA53euzsc1WobITqFYLcT5sFULz",
    "quaternius_cars/FBX": "1cjhc5GgiFR_pqPINeW99XDeWc62FWDlY",
}
PH_HDRIS = {"kiara_7_late-afternoon": "4k", "aft_lounge": "2k"}
PH_MODELS = [
    "jacaranda_tree", "island_tree_01", "island_tree_02", "tree_small_02",
    "shrub_01", "shrub_02", "shrub_04", "street_lamp_01", "painted_wooden_bench",
    "Megaphone_01", "potted_plant_01", "potted_plant_02", "planter_box_01",
    "metal_trash_can", "fire_hydrant", "CoffeeCart_01", "outdoor_table_chair_set_01",
    "metal_office_desk", "modern_arm_chair_01", "bar_chair_round_01", "classic_laptop",
    "hanging_industrial_lamp", "modern_ceiling_lamp_01", "Sofa_01", "modern_coffee_table_01", "Shelf_01",
]
PH_TEXTURES = ["herringbone_pavement", "brick_pavement_02", "clean_asphalt", "leafy_grass", "concrete_pavement",
               "wood_floor", "plastered_wall_04"]


def opener():
    cj = http.cookiejar.CookieJar()
    op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj))
    op.addheaders = [("User-Agent", UA)]
    return op


def fetch(op, url, dest=None, data=None):
    if dest and os.path.exists(dest) and os.path.getsize(dest) > 0:
        print("  have", os.path.relpath(dest, SRC))
        return dest
    body = urllib.parse.urlencode(data).encode() if data else None
    with op.open(url, body, timeout=600) as r:
        if dest is None:
            return r.read().decode("utf-8", "replace")
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        tmp = dest + ".part"
        with open(tmp, "wb") as f:
            while True:
                chunk = r.read(1 << 20)
                if not chunk:
                    break
                f.write(chunk)
        os.replace(tmp, dest)
        print("  got ", os.path.relpath(dest, SRC), os.path.getsize(dest) // 1024, "KB")
        return dest


def itch(slug, pick):
    op = opener()
    page = fetch(op, f"https://quaternius.itch.io/{slug}")
    csrf = re.search(r'name="csrf_token" value="([^"]+)"', page).group(1)
    dl = json.loads(fetch(op, f"https://quaternius.itch.io/{slug}/download_url",
                          data={"csrf_token": csrf}))["url"]
    dpage = fetch(op, dl)
    key = re.search(r"/download/([^?\"']+)", dl).group(1)
    csrf2 = re.search(r'name="csrf_token" value="([^"]+)"', dpage).group(1)
    for m in re.finditer(r'data-upload_id="(\d+)".*?class="upload_name".*?title="([^"]+)"', dpage, re.S):
        uid, name = m.groups()
        if pick not in name:
            continue
        dest = os.path.join(SRC, "quaternius_" + slug.replace("-", "_"), name)
        if os.path.exists(dest):
            print("  have", name)
            continue
        info = json.loads(fetch(op, f"https://quaternius.itch.io/{slug}/file/{uid}?source=view_game&as_props=1&after_download_lightbox=true",
                                data={"csrf_token": csrf2}))
        fetch(op, info["url"], dest)
    for name in os.listdir(os.path.join(SRC, "quaternius_" + slug.replace("-", "_"))):
        if name.endswith(".zip"):
            d = os.path.join(SRC, "quaternius_" + slug.replace("-", "_"))
            if not any(x in os.listdir(d) for x in ("Exports", "glTF")):
                print("  unzip", name)
                zipfile.ZipFile(os.path.join(d, name)).extractall(d)


def drive_folder(local, fid):
    op = opener()
    html = fetch(op, f"https://drive.google.com/embeddedfolderview?id={fid}")
    for href, title in re.findall(r'href="(https://drive.google.com/[^"]+)"[^>]*>.*?<div class="flip-entry-title">([^<]+)', html, re.S):
        title = title.strip()
        if "/folders/" in href:
            drive_folder(os.path.join(local, title), href.split("/folders/")[1].split("?")[0])
        else:
            file_id = href.split("/file/d/")[1].split("/")[0]
            fetch(op, f"https://drive.usercontent.google.com/download?id={file_id}&export=download&confirm=t",
                  os.path.join(SRC, local, title))


def polyhaven():
    op = opener()
    for hid, res in PH_HDRIS.items():
        files = json.loads(fetch(op, f"https://api.polyhaven.com/files/{hid}"))
        url = files["hdri"][res]["hdr"]["url"]
        fetch(op, url, os.path.join(SRC, "polyhaven", "hdri", os.path.basename(url)))
    for mid in PH_MODELS:
        files = json.loads(fetch(op, f"https://api.polyhaven.com/files/{mid}"))
        g = files["gltf"]["1k"]["gltf"]
        base = os.path.join(SRC, "polyhaven", "models", mid)
        fetch(op, g["url"], os.path.join(base, os.path.basename(g["url"])))
        for rel, inc in g.get("include", {}).items():
            fetch(op, inc["url"], os.path.join(base, rel))
    for tid in PH_TEXTURES:
        files = json.loads(fetch(op, f"https://api.polyhaven.com/files/{tid}"))
        base = os.path.join(SRC, "polyhaven", "textures", tid)
        for kind in ("Diffuse", "nor_gl", "Rough", "AO", "arm"):
            if kind in files and "1k" in files[kind]:
                fmt = "jpg" if "jpg" in files[kind]["1k"] else "png"
                u = files[kind]["1k"][fmt]["url"]
                fetch(op, u, os.path.join(base, os.path.basename(u)))


if __name__ == "__main__":
    which = sys.argv[1:] or ["polyhaven", "drive", "itch"]
    if "polyhaven" in which:
        print("Poly Haven"); polyhaven()
    if "drive" in which:
        for local, fid in DRIVE.items():
            print("Drive", local); drive_folder(local, fid)
    if "itch" in which:
        for slug, pick in ITCH.items():
            print("itch", slug); itch(slug, pick)
