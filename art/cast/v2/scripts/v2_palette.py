"""10-tone skin palette, very fair -> very deep, with perceptually even steps (equal CIEDE2000 between
neighbours) along a skin-locus curve in CIELAB. Plain python3 + numpy. Writes palette.json.
usage: python3 v2_palette.py <out.json>
"""
import json, sys, math
import numpy as np

def lab_to_xyz(L, a, b):
    fy = (L + 16) / 116; fx = fy + a / 500; fz = fy - b / 200
    def f(t):
        return np.where(t ** 3 > 0.008856, t ** 3, (t - 16 / 116) / 7.787)
    Xn, Yn, Zn = 0.95047, 1.0, 1.08883
    return Xn * f(fx), Yn * f(fy), Zn * f(fz)

def xyz_to_srgb(X, Y, Z):
    r = 3.2406 * X - 1.5372 * Y - 0.4986 * Z
    g = -0.9689 * X + 1.8758 * Y + 0.0415 * Z
    b = 0.0557 * X - 0.2040 * Y + 1.0570 * Z
    def gam(c):
        c = np.clip(c, 0, 1)
        return np.where(c <= 0.0031308, 12.92 * c, 1.055 * c ** (1 / 2.4) - 0.055)
    return gam(r), gam(g), gam(b)

def ciede2000(l1, l2):
    L1, a1, b1 = l1; L2, a2, b2 = l2
    C1 = math.hypot(a1, b1); C2 = math.hypot(a2, b2); Cb = (C1 + C2) / 2
    G = 0.5 * (1 - math.sqrt(Cb ** 7 / (Cb ** 7 + 25 ** 7)))
    a1p, a2p = (1 + G) * a1, (1 + G) * a2
    C1p, C2p = math.hypot(a1p, b1), math.hypot(a2p, b2)
    h1p = math.degrees(math.atan2(b1, a1p)) % 360; h2p = math.degrees(math.atan2(b2, a2p)) % 360
    dLp = L2 - L1; dCp = C2p - C1p
    dh = h2p - h1p
    if dh > 180: dh -= 360
    if dh < -180: dh += 360
    dHp = 2 * math.sqrt(C1p * C2p) * math.sin(math.radians(dh / 2))
    Lbp = (L1 + L2) / 2; Cbp = (C1p + C2p) / 2
    hbp = (h1p + h2p) / 2 if abs(h1p - h2p) <= 180 else (h1p + h2p + 360) / 2
    T = 1 - 0.17 * math.cos(math.radians(hbp - 30)) + 0.24 * math.cos(math.radians(2 * hbp)) + 0.32 * math.cos(math.radians(3 * hbp + 6)) - 0.20 * math.cos(math.radians(4 * hbp - 63))
    dTh = 30 * math.exp(-((hbp - 275) / 25) ** 2)
    Rc = 2 * math.sqrt(Cbp ** 7 / (Cbp ** 7 + 25 ** 7))
    SL = 1 + 0.015 * (Lbp - 50) ** 2 / math.sqrt(20 + (Lbp - 50) ** 2); SC = 1 + 0.045 * Cbp; SH = 1 + 0.015 * Cbp * T
    RT = -math.sin(math.radians(2 * dTh)) * Rc
    return math.sqrt((dLp / SL) ** 2 + (dCp / SC) ** 2 + (dHp / SH) ** 2 + RT * (dCp / SC) * (dHp / SH))

KEYS = [(0.0, (88.0, 8.0, 14.0)), (0.25, (72.0, 13.0, 22.0)), (0.5, (56.0, 15.0, 25.0)),
        (0.75, (40.0, 14.5, 22.0)), (1.0, (24.0, 11.0, 16.0))]

def locus(t):
    """t=0 very fair (rosy) .. t=1 very deep (warm brown); key points within the CIELAB range reported
    for human skin (L* ~24-88, hue angle ~50-65 deg). Smooth (Catmull-Rom) through the keys."""
    ks = [k for k, _ in KEYS]; vs = [np.array(v) for _, v in KEYS]
    i = min(len(ks) - 2, max(0, int(np.searchsorted(ks, t, side='right')) - 1))
    u = (t - ks[i]) / (ks[i + 1] - ks[i])
    p0 = vs[max(i - 1, 0)]; p1 = vs[i]; p2 = vs[i + 1]; p3 = vs[min(i + 2, len(vs) - 1)]
    v = 0.5 * ((2 * p1) + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (-p0 + 3 * p1 - 3 * p2 + p3) * u ** 3)
    return tuple(float(x) for x in v)

def build(n=10):
    ts = np.linspace(0, 1, 2001)
    pts = [locus(t) for t in ts]
    cum = [0.0]
    for p, q in zip(pts, pts[1:]):
        cum.append(cum[-1] + ciede2000(p, q))
    cum = np.array(cum)
    targets = np.linspace(0, cum[-1], n)
    idx = [int(np.argmin(np.abs(cum - x))) for x in targets]
    tones = []
    names = ['porcelain', 'fair', 'light', 'light_medium', 'medium', 'tan', 'brown', 'dark_brown', 'deep', 'very_deep']
    for k, i in enumerate(idx):
        L, a, b = pts[i]
        X, Y, Z = lab_to_xyz(np.array(L), np.array(a), np.array(b))
        r, g, bb = xyz_to_srgb(X, Y, Z)
        hexc = '#%02x%02x%02x' % tuple(int(round(float(c) * 255)) for c in (r, g, bb))
        tones.append({'id': f'skin_{k + 1:02d}', 'name': names[k], 'hex': hexc, 'lab': [round(float(L), 1), round(float(a), 1), round(float(b), 1)]})
    steps = [round(ciede2000(tones[i]['lab'], tones[i + 1]['lab']), 2) for i in range(n - 1)]
    return tones, steps

if __name__ == '__main__':
    tones, steps = build()
    out = {'method': 'Equal CIEDE2000 steps along a CIELAB skin locus (L* 88 -> 24); sRGB D65.',
           'neighbour_delta_e2000': steps, 'tones': tones}
    json.dump(out, open(sys.argv[1], 'w'), indent=1)
    for t in tones:
        print(t['id'], t['name'], t['hex'], t['lab'])
    print('dE00 steps', steps)
