"""Top-down plan image for docs/design/ct-island-v2-plan.png (hillshaded relief, water, forests, roads, venues)."""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
from scipy import ndimage

import geom as G
import layout as L
import terrain as T

VIEW = (-880.0, -720.0, 900.0, 740.0)   # x0, z0, x1, z1
PPM = 1.12                               # pixels per metre


def font(size, bold=False):
    """The project's bundled fonts (SIL OFL 1.1, client/Assets/Resources/UI/Fonts): Lilita One for bold sign text,
    Manrope otherwise; system fonts only as a fallback for plan previews."""
    fonts = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'client', 'Assets', 'Resources', 'UI', 'Fonts')
    for p in ([os.path.join(fonts, 'LilitaOne-Regular.ttf')] if bold else [os.path.join(fonts, 'Manrope-Semibold.ttf')]) + \
             ['/System/Library/Fonts/Supplemental/Arial.ttf', '/System/Library/Fonts/Helvetica.ttc']:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except OSError:
                pass
    return ImageFont.load_default(size=size)


def _inside(q, P):
    x, z = q
    inside = False
    j = len(P) - 1
    for i in range(len(P)):
        xi, zi = P[i]
        xj, zj = P[j]
        if (zi > z) != (zj > z) and x < (xj - xi) * (z - zi) / (zj - zi + 1e-12) + xi:
            inside = not inside
        j = i
    return inside


def px(x, z):
    return ((x - VIEW[0]) * PPM, (VIEW[3] - z) * PPM)


def plan_image(h, land, net, venues, homes, river, forest, path, landmarks=None, helipads=None, view=None, ppm=None,
               annotate=True, buildings=None, size=None):
    """annotate=False: the map atlas (no labels, venue frames, legend); buildings: dress.json building boxes drawn as
    roofs with a soft drop shadow; view/ppm/size: the area and resolution (defaults: the plan image)."""
    global VIEW, PPM
    saved = (VIEW, PPM)
    if view is not None:
        VIEW = view
    if ppm is not None:
        PPM = ppm
    try:
        return _plan_image(h, land, net, venues, homes, river, forest, path, landmarks, helipads, annotate, buildings, size)
    finally:
        VIEW, PPM = saved


def _plan_image(h, land, net, venues, homes, river, forest, path, landmarks, helipads, annotate, buildings, size):
    W, H = size or (int((VIEW[2] - VIEW[0]) * PPM), int((VIEW[3] - VIEW[1]) * PPM))
    xs = VIEW[0] + (np.arange(W) + 0.5) / PPM
    zs = VIEW[3] - (np.arange(H) + 0.5) / PPM
    XX, ZZ = np.meshgrid(xs, zs)
    hh = T.sample(h, XX.ravel(), ZZ.ravel()).reshape(H, W)
    ff = T.sample(forest, XX.ravel(), ZZ.ravel()).reshape(H, W)
    ll = T.sample(land.astype(float), XX.ravel(), ZZ.ravel()).reshape(H, W) > 0.5
    gz, gx = np.gradient(hh, 1.0 / PPM)
    slope = np.hypot(gx, gz)
    # hillshade, light from the north-west, 45 degrees
    lx, ly, lz = -0.5, 0.707, 0.5
    nrm = np.stack([-gx, np.ones_like(hh), gz], axis=-1)       # image z flipped: gz is d/dz with z decreasing downwards
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    shade = np.clip(nrm[..., 0] * lx + nrm[..., 1] * ly + nrm[..., 2] * lz, 0, 1)
    col = np.zeros((H, W, 3))
    # land colours
    lush, olive, sand, rock, snow = map(np.array, ([0.47, 0.70, 0.30], [0.62, 0.66, 0.34], [0.91, 0.83, 0.60], [0.62, 0.57, 0.53], [0.96, 0.96, 0.95]))
    hi = G.smoothstep(8.0, 90.0, hh)[..., None]
    c = lush * (1 - hi) + olive * hi
    noise = G.fbm(XX / 60.0, ZZ / 60.0, octaves=3)[..., None]
    c = c * (0.92 + 0.16 * noise)
    c = c * (1 - 0.45 * ff[..., None]) + np.array([0.18, 0.40, 0.20]) * 0.45 * ff[..., None]
    r = G.smoothstep(0.75, 1.1, slope)[..., None]
    c = c * (1 - r) + rock * r
    sn = (G.smoothstep(126.0, 140.0, hh) * G.smoothstep(1.4, 0.7, slope))[..., None]
    c = c * (1 - sn) + snow * sn
    sd = (G.smoothstep(L.SEA + 3.2, L.SEA + 1.2, hh))[..., None]
    c = c * (1 - sd) + sand * sd
    col = c * (0.58 + 0.62 * shade[..., None])
    # sea
    depth = np.clip(L.SEA - hh, 0, 30)
    deep, shallow = np.array([0.13, 0.42, 0.67]), np.array([0.40, 0.78, 0.84])
    t = G.smoothstep(0.0, 9.0, depth)[..., None]
    wcol = shallow * (1 - t) + deep * t
    sea = (~ll) | (hh < L.SEA - 0.05)
    col = np.where(sea[..., None], wcol, col)
    img = Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8), 'RGB').convert('RGBA')
    d = ImageDraw.Draw(img, 'RGBA')
    # river and lakes
    line, level, hw, depth_r = river
    tan = G.tangents(line)
    rn = G.right_normals(tan)
    left = line - rn * hw[:, None]
    right = line + rn * hw[:, None]
    poly = [px(x, z) for x, z in np.vstack([left, right[::-1]])]
    riv = Image.new('L', img.size, 0)
    ImageDraw.Draw(riv).polygon(poly, fill=255)
    rivm = np.asarray(riv) > 0
    rivm &= ll
    arr = np.asarray(img).copy()
    arr[rivm] = (78, 160, 205, 255)
    img = Image.fromarray(arr, 'RGBA')
    d = ImageDraw.Draw(img, 'RGBA')
    # district grounds, parks and farm fields (plan colours only; the 3D ground comes from terrain maps and props)
    def fillpoly(pts, col, clip=True):
        if not clip:
            d.polygon([px(x, z) for x, z in pts], fill=col)
            return
        layer = Image.new('RGBA', img.size, (0, 0, 0, 0))
        ImageDraw.Draw(layer).polygon([px(x, z) for x, z in pts], fill=col)
        la = np.asarray(layer).copy()
        la[~(ll & (hh > L.SEA + 0.2))] = 0
        img.alpha_composite(Image.fromarray(la, 'RGBA'))
    for dd in L.DISTRICTS:
        if dd['id'] == 'trenches':
            fillpoly(dd['poly'], (176, 164, 150, 150))
        if dd['id'] == 'dubai_marina':
            fillpoly(dd['poly'], (226, 220, 206, 120))
    # downtown: paved ground under the turned grid (the lots and buildings are drawn from dress.json when present)
    fillpoly(L.DOWNTOWN_POLY, (206, 196, 182, 170))
    for name, poly in L.PARKS:
        fillpoly(poly, (126, 176, 96, 210))
    crop_col = {'wheat': (226, 196, 96, 230), 'corn': (150, 186, 70, 230), 'sunflower': (240, 206, 60, 230),
                'cabbage': (120, 176, 104, 230), 'lavender': (170, 140, 210, 230), 'pumpkin': (214, 150, 70, 230)}
    for poly_f, ang, crop in L.FIELDS:
        fillpoly(poly_f, crop_col.get(crop, (200, 180, 100, 220)), clip=False)
        P = np.array(poly_f, float)
        c0 = P.mean(axis=0)
        a = math.radians(ang)
        u = np.array([math.sin(a), math.cos(a)])
        v = np.array([u[1], -u[0]])
        for k in range(-14, 15):
            p0 = c0 + v * k * 4.5 - u * 60
            p1 = c0 + v * k * 4.5 + u * 60
            seg = []
            for t in np.linspace(0, 1, 40):
                q = p0 + (p1 - p0) * t
                inside = _inside(q, P)
                if inside:
                    seg.append(px(*q))
                elif len(seg) > 1:
                    d.line(seg, fill=(0, 0, 0, 40), width=1); seg = []
            if len(seg) > 1:
                d.line(seg, fill=(0, 0, 0, 40), width=1)
    for name, poly_p in L.PARKS:
        fillpoly(poly_p, (96, 168, 84, 170))
    d = ImageDraw.Draw(img, 'RGBA')
    for lake in L.LAKES:
        cx, cz = lake['center']
        R = lake['radius']
        a, b = px(cx - R, cz + R), px(cx + R, cz - R)
        d.ellipse([a, b], fill=(78, 160, 205, 255))
    # forest canopy stipple (ref 8): little round trees on the forest mask
    rng = np.random.default_rng(7)
    pts = rng.uniform([VIEW[0], VIEW[1]], [VIEW[2], VIEW[3]], size=(26000, 2))
    fv = T.sample(forest, pts[:, 0], pts[:, 1])
    hv = T.sample(h, pts[:, 0], pts[:, 1])
    keep = (fv > rng.uniform(0.25, 1.0, len(fv))) & (hv > L.SEA + 1)
    for (x, z), hv_ in zip(pts[keep], hv[keep]):
        rr = rng.uniform(2.2, 4.2) * PPM
        cx, cy = px(x, z)
        g = rng.uniform(0.0, 1.0)
        base = (int(40 + 30 * g), int(105 + 40 * g), int(48 + 18 * g), 255) if hv_ < 70 else (int(35 + 20 * g), int(88 + 30 * g), int(60 + 20 * g), 255)
        d.ellipse([cx - rr + 1.2, cy - rr + 1.6, cx + rr + 1.2, cy + rr + 1.6], fill=(20, 45, 25, 110))
        d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=base)
    # roads: sidewalks, then carriageways, then centre lines
    def ribbon(pts2, wr, wl):
        tn = G.tangents(pts2)
        nr = G.right_normals(tn)
        a = pts2 + nr * wr
        b = pts2 - nr * wl
        return [px(x, z) for x, z in np.vstack([a, b[::-1]])]

    segs = []
    for s in net.segs:
        p2, ys, ug = net.trimmed(s)
        if len(p2) < 2:
            continue
        segs.append((s, p2, ug))
    for s, p2, ug in segs:
        if s.sidewalk > 0:
            d.polygon(ribbon(p2, s.out_r, s.out_l), fill=(222, 214, 196, 255))
    for j in net.junctions:
        for f in j['fillets']:
            if f['sidewalk'] > 0 and not f['straight']:
                pts = np.array([[p[0], p[2]] for p in f['points']])
                c = np.array(j['center'])[[0, 2]]
                out = pts + (pts - c) / np.maximum(np.linalg.norm(pts - c, axis=1, keepdims=True), 1e-6) * f['sidewalk']
                d.polygon([px(x, z) for x, z in np.vstack([pts, out[::-1]])], fill=(222, 214, 196, 255))
    for s, p2, ug in segs:
        fill = (150, 118, 78, 255) if s.prof['surface'] == 'dirt' else (88, 90, 98, 255) if s.kind != 'highway' else (70, 72, 80, 255)
        if s.bridge:
            d.polygon(ribbon(p2, s.cw_r + 1.4, s.cw_l + 1.4), fill=(40, 40, 46, 120))
            fill = (122, 124, 132, 255)
        if s.tunnel and ug is not None:
            for k in range(len(p2) - 1):
                if ug[k]:
                    a, b = px(*p2[k]), px(*p2[k + 1])
                    d.line([a, b], fill=(40, 40, 40, 160), width=max(1, int((s.cw_r + s.cw_l) * PPM * 0.5)))
            vis = ~ug
            runs, cur = [], []
            for k in range(len(p2)):
                if vis[k]:
                    cur.append(k)
                elif cur:
                    runs.append(cur); cur = []
            if cur:
                runs.append(cur)
            for run in runs:
                if len(run) > 1:
                    d.polygon(ribbon(p2[run], s.cw_r, s.cw_l), fill=fill)
            continue
        d.polygon(ribbon(p2, s.cw_r, s.cw_l), fill=fill)
    for j in net.junctions:
        poly = [px(p[0], p[2]) for p in j['polygon']]
        if len(poly) >= 3:
            d.polygon(poly, fill=(88, 90, 98, 255))
    for c, ring in net.ring_nodes.items():
        cx = np.mean([net.nodes[r][0] for r in ring]); cz = np.mean([net.nodes[r][1] for r in ring])
        R = L.ROUNDABOUTS[c] - 4.5
        a, b = px(cx - R, cz + R), px(cx + R, cz - R)
        d.ellipse([a, b], fill=(120, 170, 90, 255), outline=(230, 230, 220, 255), width=2)
    for s, p2, ug in segs:
        m = s.prof['markings']
        if m == 'none' or len(p2) < 3:
            continue
        pts = [px(x, z) for x, z in p2]
        if m == 'double_yellow':
            d.line(pts, fill=(238, 196, 64, 255), width=2)
        else:
            for k in range(0, len(pts) - 1, 3):
                d.line([pts[k], pts[min(k + 1, len(pts) - 1)]], fill=(240, 240, 235, 230), width=1)
    # buildings (dress.json): a soft shadow to the south-east, then the roof
    if buildings:
        sh = Image.new('RGBA', img.size, (0, 0, 0, 0))
        sd_ = ImageDraw.Draw(sh, 'RGBA')
        for cx, cy, cz, w, hgt, dep, yaw in buildings:
            off = min(hgt * 0.35, 14.0)
            r = G.oriented_rect(cx + off * 0.7, cz - off * 0.7, w, dep, yaw)
            sd_.polygon([px(x, z) for x, z in r], fill=(20, 24, 30, 90))
        img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(1.2 * PPM)))
        d = ImageDraw.Draw(img, 'RGBA')
        for cx, cy, cz, w, hgt, dep, yaw in buildings:
            k = min(1.0, hgt / 40.0)
            roof = (int(214 - 40 * k), int(206 - 36 * k), int(196 - 26 * k), 255)
            r = G.oriented_rect(cx, cz, w, dep, yaw)
            d.polygon([px(x, z) for x, z in r], fill=roof, outline=(120, 112, 104, 255))
    if not annotate:
        img.convert('RGB').save(path, optimize=True)
        return img
    # venues: oriented frames with door ticks and numbers
    fnt, fnt_b, fnt_big = font(15), font(15, True), font(24, True)
    pal = {'timeline': (237, 128, 74), 'trenches': (170, 118, 86), 'dubai_marina': (92, 170, 214), 'boardwalk': (240, 196, 92),
           'airdrop_farm': (226, 190, 70), 'builder_hills': (126, 104, 214), 'breakpoint': (216, 92, 118), 'mountain': (214, 182, 132),
           'residences': (120, 188, 120)}
    legend = []
    for i, v in enumerate(venues, 1):
        mn, sz = v['frame_min'], v['frame_size']
        cx, cz = G.local_to_world(v['origin'][0], v['origin'][2], v['yaw'], mn[0] + sz[0] / 2, mn[1] + sz[1] / 2)
        rect = G.oriented_rect(cx, cz, sz[0], sz[1], v['yaw'])
        colr = pal.get(v['district'], (200, 200, 200))
        d.polygon([px(x, z) for x, z in rect], fill=colr + (235,), outline=(30, 30, 34, 255), width=2)
        dx, dz = v['door'][0], v['door'][2]
        a = math.radians(v['yaw'])
        fx, fz = -math.sin(a), -math.cos(a)
        d.line([px(dx, dz), px(dx + fx * 7, dz + fz * 7)], fill=(255, 255, 255, 255), width=3)
        tx, ty = px(cx, cz)
        d.text((tx, ty), str(i), font=fnt_b, fill=(20, 20, 24, 255), anchor='mm', stroke_width=2, stroke_fill=(255, 255, 255, 255))
        legend.append(f'{i:2d}  {v["title"]}')
    for v in homes:
        mn, sz = v['frame_min'], v['frame_size']
        cx, cz = G.local_to_world(v['origin'][0], v['origin'][2], v['yaw'], mn[0] + sz[0] / 2, mn[1] + sz[1] / 2)
        rect = G.oriented_rect(cx, cz, sz[0] * 0.8, sz[1] * 0.8, v['yaw'])
        d.polygon([px(x, z) for x, z in rect], fill=(250, 246, 236, 230), outline=(80, 80, 80, 255), width=1)
    # landmarks and helipads
    for lm in (landmarks or L.LANDMARKS):
        x, z = lm['at']
        cx, cy = px(x, z)
        star = []
        for k in range(10):
            rr = 9 if k % 2 == 0 else 4
            ang = -math.pi / 2 + k * math.pi / 5
            star.append((cx + rr * math.cos(ang), cy + rr * math.sin(ang)))
        d.polygon(star, fill=(255, 220, 60, 255), outline=(60, 40, 10, 255))
        d.text((cx + 12, cy - 9), lm['name'], font=fnt, fill=(25, 25, 25, 255), stroke_width=2, stroke_fill=(255, 255, 255, 220))
    for hp in (helipads or []):
        cx, cy = px(hp[0], hp[2])
        d.ellipse([cx - 9, cy - 9, cx + 9, cy + 9], fill=(60, 60, 66, 255), outline=(255, 255, 255, 255), width=2)
        d.text((cx, cy), 'H', font=fnt_b, fill=(255, 255, 255, 255), anchor='mm')
    for dd in L.DISTRICTS:
        x, z = dd['label']
        d.text(px(x, z), dd['name'].upper(), font=fnt_big, fill=(255, 255, 255, 240), anchor='mm', stroke_width=4, stroke_fill=(30, 30, 40, 200))
    # title, legend, scale, north
    d.rectangle([20, 20, 470, 86], fill=(255, 255, 255, 220))
    d.text((34, 28), 'CT Island v2: plan', font=font(30, True), fill=(20, 20, 24, 255))
    d.text((34, 62), '1.6 x 1.2 km; south-facing; doors face their streets', font=fnt, fill=(60, 60, 60, 255))
    # venue key: three compact columns over the sea in the bottom-right corner
    cols, colw, rowh = 3, 205, 21
    rows = (len(legend) + cols - 1) // cols
    lx0, ly0 = 300, H - rows * rowh - 30
    d.rectangle([lx0 - 10, ly0 - 8, lx0 + cols * colw + 10, H - 20], fill=(255, 255, 255, 215))
    for k, t in enumerate(legend):
        c, r = divmod(k, rows)
        d.text((lx0 + c * colw, ly0 + r * rowh), t, font=fnt, fill=(25, 25, 25, 255))
    sx, sy = 40, H - 50
    d.rectangle([sx, sy, sx + 200 * PPM, sy + 8], fill=(20, 20, 20, 255))
    d.rectangle([sx + 100 * PPM, sy, sx + 200 * PPM, sy + 8], fill=(250, 250, 250, 255), outline=(20, 20, 20, 255))
    d.text((sx, sy - 22), '0      100     200 m', font=fnt, fill=(20, 20, 20, 255))
    nx, ny = 70, H - 130
    d.polygon([(nx, ny - 30), (nx - 12, ny + 8), (nx, ny), (nx + 12, ny + 8)], fill=(20, 20, 20, 255))
    d.text((nx, ny + 22), 'N', font=fnt_b, fill=(20, 20, 20, 255), anchor='mm')
    img.convert('RGB').save(path, optimize=True)
