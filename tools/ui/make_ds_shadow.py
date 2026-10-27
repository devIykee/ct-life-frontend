#!/usr/bin/env python3
"""Bakes the design system's card shadow (SYSTEM.md 2.4 e2: no blur at runtime) as a 9-slice PNG.
A rounded rect (radius 32) blurred by ~24 px, black at 28% max, centred in a 192 px square; the card element sits on top
of it with the shadow nudged 16 units down. Slice 72 on every side (INSET + RADIUS); the card covers the opaque middle. Pure stdlib (no PIL): python3 -I make_ds_shadow.py OUT."""
import math, struct, sys, zlib

SIZE, INSET, RADIUS, SIGMA, ALPHA = 192, 40, 32, 12.0, 0.28

def rr_dist(x, y):
    # signed distance to a rounded rect spanning [INSET, SIZE-INSET]
    cx, cy = SIZE / 2, SIZE / 2
    hx = hy = SIZE / 2 - INSET
    qx, qy = abs(x - cx) - (hx - RADIUS), abs(y - cy) - (hy - RADIUS)
    ox, oy = max(qx, 0.0), max(qy, 0.0)
    return math.hypot(ox, oy) + min(max(qx, qy), 0.0) - RADIUS

def erf(x):  # Abramowitz-Stegun
    s = 1 if x >= 0 else -1; x = abs(x)
    t = 1 / (1 + 0.3275911 * x)
    y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * math.exp(-x * x)
    return s * y

rows = []
for y in range(SIZE):
    row = bytearray([0])
    for x in range(SIZE):
        d = rr_dist(x + 0.5, y + 0.5)
        a = ALPHA * 0.5 * (1 - erf(d / (SIGMA * math.sqrt(2))))
        row += bytes([0, 0, 0, max(0, min(255, round(a * 255)))])
    rows.append(bytes(row))
raw = b"".join(rows)
def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
open(sys.argv[1], "wb").write(png)
print("wrote", sys.argv[1], SIZE, "px")
