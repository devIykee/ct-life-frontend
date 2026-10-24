"""Hardened CC0 asset downloads for the art pipeline (Poly Haven, ambientCG): https only to an allowlist of hosts, curl
with --proto =https (also on redirects) and '--' before the URL, API-derived names sanitised, writes confined to a root.
Shared by world_v3_trees.py (tree models) and world_v3_ground.py (terrain scans); plain Python, no Blender needed."""
import json, os, re, subprocess, urllib.parse, urllib.request

UA = 'CTLife-artpipeline/1.0 (CC0 asset fetch)'
# hosts the Poly Haven API hands out (the API itself, its download CDN); anything else is refused
ALLOWED_HOSTS = {'api.polyhaven.com', 'dl.polyhaven.org'}


def check_url(url):
    u = urllib.parse.urlparse(url)
    if u.scheme != 'https' or u.hostname not in ALLOWED_HOSTS:
        raise SystemExit('refusing to fetch ' + repr(url))
    return url


def safe(name):
    """An API-derived name made safe for a file name."""
    return re.sub(r'[^A-Za-z0-9_-]', '_', str(name))


def inside(path, root):
    p = os.path.normpath(path)
    if not p.startswith(os.path.normpath(root) + os.sep):
        raise SystemExit('refusing to write outside ' + root + ': ' + path)
    return p


def get(url, path):
    check_url(url)
    if os.path.exists(path):
        return
    for _ in range(8):
        if subprocess.run(['curl', '-sS', '-L', '--proto', '=https', '--proto-redir', '=https', '-C', '-', '--retry', '5', '--retry-all-errors',
                           '-A', UA, '-o', path + '.part', '--', url]).returncode == 0:
            os.replace(path + '.part', path)
            return
    raise SystemExit('download failed ' + url)


def get_json(url):
    check_url(url)
    with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': UA}), timeout=60) as r:
        return json.load(r)


def ph_texture(pid, root, maps=(('Diffuse', 'diff'), ('nor_gl', 'nor_gl'), ('arm', 'arm')), res='1k', fmt='png'):
    """A Poly Haven texture's maps into <root>/tex_<pid>/<pid>_<tag>_<res>.<fmt>, plus meta.json (name, authors, CC0)."""
    pid = safe(pid)
    d = inside(os.path.join(root, 'tex_' + pid), root)
    os.makedirs(d, exist_ok=True)
    files = get_json('https://api.polyhaven.com/files/' + pid)
    for key, tag in maps:
        p = inside(os.path.join(d, f'{pid}_{safe(tag)}_{safe(res)}.{safe(fmt)}'), d)
        if not os.path.exists(p):
            print('[fetch] Poly Haven', pid, tag, flush=True)
            get(files[key][res][fmt]['url'], p)
    mp = os.path.join(d, 'meta.json')
    if not os.path.exists(mp):
        inf = get_json('https://api.polyhaven.com/info/' + pid)
        json.dump(dict(id=pid, name=inf['name'], url=f'https://polyhaven.com/a/{pid}', authors=list(inf['authors']), licence='CC0',
                       dimensions_mm=inf.get('dimensions')), open(mp, 'w'), indent=1)
    return d


def ph_model(pid, root, res='1k'):
    """A Poly Haven model's glTF (+ its .bin and textures, relative names kept, each part sanitised) into <root>/<pid>/,
    plus meta.json (name, authors, CC0, dimensions, polycount). Cached: a directory with meta.json is skipped."""
    pid = safe(pid)
    d = inside(os.path.join(root, pid), root)
    if os.path.exists(os.path.join(d, 'meta.json')):
        return d
    inf = get_json('https://api.polyhaven.com/info/' + pid)
    files = get_json('https://api.polyhaven.com/files/' + pid)
    g = files['gltf'][res]['gltf']
    os.makedirs(d, exist_ok=True)
    print('[fetch] Poly Haven model', pid, flush=True)
    get(g['url'], inside(os.path.join(d, f'{pid}_{safe(res)}.gltf'), d))
    for rel, v in g['include'].items():
        parts = [p for p in rel.replace('\\', '/').split('/') if p not in ('', '.', '..')]
        if not parts:
            continue
        stem, ext = os.path.splitext(parts[-1])
        clean = [safe(p) for p in parts[:-1]] + [safe(stem) + '.' + safe(ext.lstrip('.'))]
        if clean != parts:
            print('[fetch] skipped an unexpected file name', repr(rel), flush=True)
            continue
        t = inside(os.path.join(d, *clean), d)
        os.makedirs(os.path.dirname(t), exist_ok=True)
        get(v['url'], t)
    json.dump(dict(id=pid, name=inf['name'], url=f'https://polyhaven.com/a/{pid}', authors=list(inf['authors']), licence='CC0',
                   dimensions_mm=inf.get('dimensions'), polycount=inf.get('polycount')), open(os.path.join(d, 'meta.json'), 'w'), indent=1)
    return d
