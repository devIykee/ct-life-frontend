#!/usr/bin/env python3
"""Freesound helper: search CC0-only and fetch HQ previews without logging in.

Freesound only serves original files to logged-in users. The public HQ previews
(192 kbps Ogg Vorbis, 44.1 kHz) need no login and carry the same licence as the
sound, so we use those. Every download re-checks the sound page for the CC0 link.

Usage:
  freesound.py search "footsteps metal" [--n 15] [--max-dur 60]
  freesound.py info 698697
"""
import html
import json
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

UA = {"User-Agent": "Mozilla/5.0 (CT Life audio agent; CC0 sourcing)"}
CC0 = "creativecommons.org/publicdomain/zero/1.0/"


def get(url: str) -> str:
    """GET with polite backoff: freesound returns HTTP 429 to parallel scrapers."""
    for attempt in range(6):
        try:
            req = urllib.request.Request(url, headers=UA)
            with urllib.request.urlopen(req, timeout=60) as r:
                return r.read().decode("utf-8", "replace")
        except urllib.error.HTTPError as e:
            if e.code != 429 or attempt == 5:
                raise
            time.sleep(5 * (attempt + 1))
    raise RuntimeError("unreachable")


def search(q: str, n: int = 15, max_dur: float | None = None, page: int = 1):
    params = {"q": q, "f": 'license:"Creative Commons 0"', "page": str(page)}
    if max_dur:
        params["f"] += f" duration:[0 TO {max_dur}]"
    url = "https://freesound.org/search/?" + urllib.parse.urlencode(params)
    text = get(url)
    ids = []
    for m in re.finditer(r'href="/people/([^/"]+)/sounds/(\d+)/"', text):
        key = (m.group(1), m.group(2))
        if key not in ids:
            ids.append(key)
    return ids[:n]


def info(user: str | None, sid: str):
    if user is None:
        url = f"https://freesound.org/s/{sid}/"
    else:
        url = f"https://freesound.org/people/{user}/sounds/{sid}/"
    text = get(url)
    title = re.search(r'og:audio:title" content="([^"]*)"', text)
    artist = re.search(r'og:audio:artist" content="([^"]*)"', text)
    page_url = re.search(r'og:url" content="([^"]*)"', text)
    dur = re.search(r'duration="([0-9.]+)"', text)
    desc = (re.findall(r'<meta name="description" content="([^"]*)"', text) or [None])[-1]
    tags = re.findall(r'href="/browse/tags/([^/"]+)/', text)
    hq = re.search(r'https://cdn\.freesound\.org/previews/\d+/\d+_\d+-hq\.mp3', text)
    lic_cc0 = CC0 in text
    lic_other = re.findall(r'creativecommons\.org/licenses/[a-z\-]+/[0-9.]+/', text)
    return {
        "id": int(sid),
        "title": html.unescape(title.group(1)) if title else None,
        "author": html.unescape(artist.group(1)) if artist else user,
        "url": page_url.group(1) if page_url else url,
        "duration": float(dur.group(1)) if dur else None,
        "license": "CC0-1.0" if lic_cc0 and not lic_other else ("MIXED/OTHER" if lic_other else "UNKNOWN"),
        "preview_ogg": hq.group(0).replace("-hq.mp3", "-hq.ogg") if hq else None,
        "tags": sorted(set(tags))[:15],
        "description": html.unescape(desc)[:200] if desc else "",
    }


def download(sid: int, dest: str):
    meta = info(None, str(sid))
    if meta["license"] != "CC0-1.0":
        raise SystemExit(f"Refusing {sid}: licence is {meta['license']}")
    req = urllib.request.Request(meta["preview_ogg"], headers=UA)
    with urllib.request.urlopen(req, timeout=120) as r, open(dest, "wb") as f:
        f.write(r.read())
    return meta


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "search":
        q = sys.argv[2]
        n = int(sys.argv[sys.argv.index("--n") + 1]) if "--n" in sys.argv else 12
        md = float(sys.argv[sys.argv.index("--max-dur") + 1]) if "--max-dur" in sys.argv else None
        for user, sid in search(q, n, md):
            try:
                m = info(user, sid)
            except Exception as e:  # noqa: BLE001
                print(sid, "ERR", e)
                continue
            print(f"{m['id']:>7} {m['license']:8} {m['duration'] or 0:7.1f}s  {m['author'][:18]:18} {m['title'][:60]}  | {m['description'][:110]}")
            time.sleep(1.0)
    elif cmd == "info":
        print(json.dumps(info(None, sys.argv[2]), indent=2))
