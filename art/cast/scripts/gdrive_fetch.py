"""Fetch a public Google Drive folder tree (as linked from an asset author's own page) without an account.

usage: python3 gdrive_fetch.py <folder_id> <out_root> [top-level folder names to include ...]
Writes <out_root>/download-manifest.json (drive id, sha256, bytes, retrieval date per file).
Remote file/folder names are untrusted: each is reduced to a single safe path component and every
destination must resolve inside <out_root>, otherwise the run aborts. Google quota pages are rejected.
"""
import re, sys, html, os, json, hashlib, datetime, urllib.request, http.cookiejar

UA = {'User-Agent': 'Mozilla/5.0 (Macintosh) ct-life-asset-pipeline'}
cj = http.cookiejar.CookieJar()
op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj))
ID_RE = re.compile(r'^[A-Za-z0-9_-]{10,}$')
ROOT = None          # set by main(); safe_join refuses to run without it
TODAY = datetime.date.today().isoformat()
RESERVED = {'download-manifest.json'}   # our own receipt; a remote file may not overwrite it

def get(u):
    return op.open(urllib.request.Request(u, headers=UA), timeout=120)

def listing(fid):
    t = get(f'https://drive.google.com/embeddedfolderview?id={fid}').read().decode('utf8', 'replace')
    return [(html.unescape(n), u) for u, n in re.findall(
        r'<a href="(https://drive\.google\.com/[^"]+)"[^>]*>.*?<div class="flip-entry-title">([^<]+)</div>', t, re.S)]

def safe_name(name):
    """Single path component from an untrusted remote name, or None if unusable."""
    base = os.path.basename(name.replace('\\', '/')).strip()
    if base in ('', '.', '..') or '\x00' in base or base in RESERVED:
        return None
    return base

def safe_join(parent, name, root=None):
    """Join a sanitised name under parent and require the result to stay inside the download root
    (realpath containment, so symlinked parents/targets cannot escape either)."""
    root = root or ROOT
    if not root:
        raise SystemExit('ABORT: download root not set')
    comp = safe_name(name)
    if comp is None:
        return None
    dest = os.path.realpath(os.path.join(parent, comp))
    root = os.path.realpath(root)
    if os.path.commonpath([dest, root]) != root or dest == root:
        raise SystemExit(f'ABORT: remote name {name!r} resolves outside {root}')
    return dest

def download(fileid, dest):
    u = f'https://drive.usercontent.google.com/download?id={fileid}&export=download&confirm=t'
    h = hashlib.sha256()
    with get(u) as r, open(dest, 'wb') as f:
        while True:
            b = r.read(1 << 20)
            if not b:
                break
            f.write(b)
            h.update(b)
    return h.hexdigest(), os.path.getsize(dest)

def walk(fid, out, depth=0, manifest=None, only=None):
    os.makedirs(out, exist_ok=True)
    for name, u in listing(fid):
        if '/folders/' in u:
            sub = u.split('/folders/')[1].split('?')[0]
            if not ID_RE.match(sub):
                print('skip folder with unexpected id', repr(name)); continue
            if only is not None and depth == 0 and name not in only:
                print('skip folder', repr(name)); continue
            dest_dir = safe_join(out, name)
            if dest_dir is None:
                print('skip folder with unusable name', repr(name)); continue
            walk(sub, dest_dir, depth + 1, manifest, only)
        else:
            try:
                fileid = u.split('/d/')[1].split('/')[0]
            except IndexError:
                print('skip unexpected link', u); continue
            if not ID_RE.match(fileid):
                print('skip file with unexpected id', repr(name)); continue
            dest = safe_join(out, name)
            if dest is None:
                print('skip file with unusable name', repr(name)); continue
            if os.path.exists(dest) and os.path.getsize(dest) > 0:
                continue
            sha, size = download(fileid, dest)
            with open(dest, 'rb') as fh:
                head = fh.read(200)
            if b'<!DOCTYPE html>' in head and b'Google Drive' in head:
                os.remove(dest)
                raise SystemExit('QUOTA ' + os.path.basename(dest))
            manifest[os.path.relpath(dest, os.path.realpath(ROOT))] = {'drive_id': fileid, 'sha256': sha, 'bytes': size, 'retrieved': TODAY}
            print('ok', os.path.relpath(dest, os.path.realpath(ROOT)), size, flush=True)

if __name__ == '__main__':
    if len(sys.argv) < 3 or not ID_RE.match(sys.argv[1]):
        raise SystemExit(__doc__)
    ROOT = sys.argv[2]
    os.makedirs(ROOT, exist_ok=True)
    man = {}
    try:
        walk(sys.argv[1], os.path.realpath(ROOT), manifest=man, only=set(sys.argv[3:]) or None)
    finally:
        mp = os.path.join(ROOT, 'download-manifest.json')
        old = json.load(open(mp)) if os.path.exists(mp) else {}
        old.update(man)
        json.dump(old, open(mp, 'w'), indent=1)
