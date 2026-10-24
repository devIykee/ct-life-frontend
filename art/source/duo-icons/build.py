#!/usr/bin/env python3
"""Solana Duo icon studio: writes icons.html (a grid of 256px iOS-26-style icons) for headless-Chrome rendering.
Glyph geometry: Phosphor Icons fill set (MIT). Materials and custom art: original."""
import math, re, os, json

HERE = os.path.dirname(os.path.abspath(__file__))
S = 256

def squircle(n=5.0, size=S, inset=0.0, steps=240):
    r = size / 2 - inset; c = size / 2; pts = []
    for i in range(steps):
        t = 2 * math.pi * i / steps
        ct, st = math.cos(t), math.sin(t)
        x = c + r * math.copysign(abs(ct) ** (2 / n), ct)
        y = c + r * math.copysign(abs(st) ** (2 / n), st)
        pts.append(f"{x:.2f},{y:.2f}")
    return "M" + " L".join(pts) + "Z"

SQ = squircle()
SQ_IN = squircle(inset=1.6)

def ph(name):
    t = open(os.path.join(HERE, "ph", name + ".svg")).read()
    return "".join(re.findall(r'<path d="([^"]+)"', t))

# tile palettes
LIGHT = ("#ffffff", "#eceef2")
def tile(c1, c2): return (c1, c2)

# id: (tile, glyph spec)
# glyph spec: ("ph", name, (g1,g2), scale) or ("custom", key)
ICONS = {
 "messages": (tile("#6cf08a", "#1fbf45"), ("custom", "messages")),
 "calls":    (tile("#6cf08a", "#1fbf45"), ("ph", "phone", ("#ffffff", "#e3fbe8"), 0.672)),
 "wallet":   (tile("#2b2c31", "#08080a"), ("custom", "wallet")),
 "map":      (None, ("custom", "map")),
 "perps":    (tile("#22252d", "#07080b"), ("custom", "perps")),
 "timeline": (tile("#2a2c33", "#0b0c10"), ("custom", "timeline")),
 "jobs":     (tile("#5cb8ff", "#0866f0"), ("ph", "briefcase", ("#ffffff", "#dcebff"), 0.672)),
 "bank":     (LIGHT, ("ph", "bank", ("#3ee07a", "#0f9a46"), 0.706)),
 "richlist": (LIGHT, ("ph", "crown-simple", ("#ffd85a", "#ff9500"), 0.722)),
 "home":     (tile("#ffb15c", "#ff6a00"), ("ph", "house-line", ("#ffffff", "#ffe7d1"), 0.689)),
 "predict":  (tile("#c08cff", "#7a2ff0"), ("ph", "dice-five", ("#ffffff", "#ecdcff"), 0.655)),
 "settings": (tile("#a3a6ae", "#5d6068"), ("custom", "settings")),
 "pulse":    (LIGHT, ("custom", "pulse")),
 "shop":     (tile("#5fe0f2", "#0aa2c4"), ("ph", "shopping-bag-open", ("#ffffff", "#d9f7fd"), 0.672)),
 "biz":      (tile("#8892ff", "#3a43e6"), ("ph", "buildings", ("#ffffff", "#e0e3ff"), 0.672)),
 "ore":      (tile("#3a2a1c", "#140c06"), ("custom", "ore")),
 "spaces":   (tile("#d18bff", "#8a2be2"), ("ph", "microphone-stage", ("#ffffff", "#f1e2ff"), 0.672)),
 "profile":  (LIGHT, ("custom", "profile")),
 "topup":    (LIGHT, ("ph", "plus-circle", ("#43e07c", "#12a54a"), 0.739)),
 "week":     (LIGHT, ("custom", "week")),
 "ads":      (tile("#ffe066", "#ffb300"), ("ph", "megaphone-simple", ("#ffffff", "#fff4d6"), 0.672)),
 "beef":     (tile("#ff7a6b", "#e5192c"), ("ph", "fire", ("#ffffff", "#ffe2d6"), 0.672)),
 "builder":  (tile("#59d3ff", "#0a84ff"), ("ph", "hammer", ("#ffffff", "#dcf3ff"), 0.672)),
 "cabal":    (tile("#3b2a63", "#120a24"), ("ph", "users-three", ("#d9c2ff", "#9b6bff"), 0.706)),
 "farm":     (tile("#7fe08a", "#21a34a"), ("ph", "plant", ("#ffffff", "#e3fbe6"), 0.672)),
 "league":   (LIGHT, ("ph", "trophy", ("#ffd24a", "#ff9500"), 0.706)),
 "npcchat":  (tile("#5ee3d0", "#0f9f9a"), ("ph", "robot", ("#ffffff", "#dcfbf6"), 0.672)),
 "vc":       (tile("#2d3a33", "#0b120e"), ("ph", "rocket-launch", ("#7dffb0", "#14c46a"), 0.672)),
 "xlink":    (tile("#2a2c33", "#0b0c10"), ("ph", "link-simple", ("#ffffff", "#c9ccd6"), 0.672)),
 "app":      (tile("#b4b7bf", "#6f727b"), ("ph", "squares-four", ("#ffffff", "#e9eaee"), 0.622)),
 "duoshop":  (tile("#5aa8ff", "#0a5fe0"), ("ph", "storefront", ("#ffffff", "#dcebff"), 0.672)),
 "quests":   (tile("#ff7aa8", "#ff2d6f"), ("ph", "target", ("#ffffff", "#ffe0ea"), 0.672)),
}

def defs(uid, t, g=None):
    out = [f'<clipPath id="clip{uid}"><path d="{SQ}"/></clipPath>']
    if t:
        out.append(f'<linearGradient id="tile{uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{t[0]}"/><stop offset="1" stop-color="{t[1]}"/></linearGradient>')
    if g:
        out.append(f'<linearGradient id="gly{uid}" x1="0.2" y1="0" x2="0.8" y2="1"><stop offset="0" stop-color="{g[0]}"/><stop offset="1" stop-color="{g[1]}"/></linearGradient>')
    out.append(f'<linearGradient id="spec{uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".62"/><stop offset=".48" stop-color="#fff" stop-opacity="0"/></linearGradient>')
    out.append(f'<filter id="sh{uid}" x="-30%" y="-30%" width="160%" height="170%"><feDropShadow dx="0" dy="5" stdDeviation="6" flood-color="#000" flood-opacity=".22"/></filter>')
    out.append(f'<filter id="soft{uid}" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="2.2"/></filter>')
    return "".join(out)

def frame(uid, t, inner):
    """tile + rim + content + top sheen"""
    bg = f'<path d="{SQ}" fill="url(#tile{uid})"/>' if t else ""
    sheen = (f'<path d="{SQ}" fill="url(#spec{uid})" opacity=".35"/>' if t and t != LIGHT else
             f'<path d="{SQ}" fill="url(#spec{uid})" opacity=".55"/>')
    rim = (f'<path d="{SQ_IN}" fill="none" stroke="#fff" stroke-opacity=".55" stroke-width="2.2"/>'
           f'<path d="{SQ}" fill="none" stroke="#000" stroke-opacity=".10" stroke-width="1.2"/>')
    return f'<g clip-path="url(#clip{uid})">{bg}{inner}{sheen}</g>{rim}'

def glass_glyph(uid, d, scale):
    # phosphor glyph box ~ 16..240; centre and scale
    k = scale; off = (S - S * k) / 2
    tr = f'translate({off:.1f},{off + 2:.1f}) scale({k})'
    return (f'<g filter="url(#sh{uid})"><g transform="{tr}">'
            f'<path d="{d}" fill="url(#gly{uid})"/>'
            f'<path d="{d}" fill="url(#spec{uid})"/>'
            f'<path d="{d}" fill="none" stroke="#fff" stroke-opacity=".38" stroke-width="2.2"/>'
            f'</g></g>')

def custom(uid, key):
    if key == "messages":
        bubble = "M128 58c-46 0-83 29-83 66 0 21 12 40 31 52-2 11-9 22-18 30 17 0 33-7 43-15 9 2 18 3 27 3 46 0 83-30 83-70S174 58 128 58Z"
        return (f'<linearGradient id="mg{uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#d4f7db"/></linearGradient>',
                f'<g filter="url(#sh{uid})"><path d="{bubble}" fill="url(#mg{uid})" fill-opacity=".97"/><path d="{bubble}" fill="url(#spec{uid})"/>'
                f'<path d="{bubble}" fill="none" stroke="#fff" stroke-width="2.4" stroke-opacity=".9"/></g>')
    if key == "wallet":
        cards = [("#5aa9ff", "#2f7bff", 58), ("#ffd84a", "#ffb300", 82), ("#ff8a6b", "#ff4d5e", 106)]
        g = []; d = []
        for i, (a, b, y) in enumerate(cards):
            d.append(f'<linearGradient id="wc{i}{uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{a}"/><stop offset="1" stop-color="{b}"/></linearGradient>')
            g.append(f'<rect x="52" y="{y}" width="152" height="96" rx="16" fill="url(#wc{i}{uid})"/>'
                     f'<rect x="52" y="{y}" width="152" height="40" rx="16" fill="#fff" fill-opacity=".18"/>')
        pocket = "M44 134h56c6 0 10 4 12 9 4 9 10 14 16 14s12-5 16-14c2-5 6-9 12-9h56v52c0 11-9 20-20 20H64c-11 0-20-9-20-20Z"
        d.append(f'<linearGradient id="wp{uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#f6efe8"/><stop offset="1" stop-color="#d9cdc2"/></linearGradient>')
        return ("".join(d), f'<g filter="url(#sh{uid})">{"".join(g)}<path d="{pocket}" fill="url(#wp{uid})" fill-opacity=".92"/>'
                f'<path d="{pocket}" fill="url(#spec{uid})" opacity=".7"/><path d="{pocket}" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="2"/></g>')
    if key == "map":
        return ("",
            '<rect width="256" height="256" fill="#5fd36d"/>'
            '<path d="M0 150 L120 256 L0 256Z" fill="#ff79b0"/>'
            '<path d="M150 256 L256 150 L256 256Z" fill="#ffd34d"/>'
            '<circle cx="230" cy="40" r="58" fill="none" stroke="#a8f0b0" stroke-width="10"/>'
            '<circle cx="230" cy="40" r="34" fill="none" stroke="#a8f0b0" stroke-width="10"/>'
            '<path d="M86 -10 L118 -10 L126 256 L94 256Z" fill="#fff"/>'
            '<path d="M101 -10 L109 -10 L112 140 L104 140Z" fill="#2f86ff"/>'
            '<path d="M-10 196 L256 120 L256 152 L-10 228Z" fill="#fff"/>'
            f'<g filter="url(#sh{uid})"><circle cx="108" cy="150" r="52" fill="#fff"/>'
            '<circle cx="108" cy="150" r="42" fill="#1f7cff"/><circle cx="108" cy="150" r="42" fill="url(#spec' + uid + ')"/>'
            '<path d="M108 120 L128 170 L108 160 L88 170Z" fill="#fff"/></g>')
    if key == "perps":
        pts = [(28,182),(52,170),(70,176),(92,150),(112,158),(132,128),(150,136),(172,104),(190,112),(210,84),(228,76)]
        dl = "M" + " L".join(f"{x} {y}" for x, y in pts)
        return (f'<linearGradient id="pa{uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3ddc84" stop-opacity=".55"/><stop offset="1" stop-color="#3ddc84" stop-opacity="0"/></linearGradient>'
                f'<filter id="glow{uid}"><feGaussianBlur stdDeviation="5"/></filter>',
                f'<path d="{dl} L228 210 L28 210Z" fill="url(#pa{uid})"/>'
                f'<path d="{dl}" fill="none" stroke="#3ddc84" stroke-width="16" stroke-linecap="round" stroke-linejoin="round" opacity=".55" filter="url(#glow{uid})"/>'
                f'<path d="{dl}" fill="none" stroke="#7dffb4" stroke-width="10" stroke-linecap="round" stroke-linejoin="round"/>'
                '<circle cx="228" cy="76" r="13" fill="#fff"/><circle cx="228" cy="76" r="7" fill="#3ddc84"/>')
    if key == "timeline":
        rows = []
        for i, (y, op) in enumerate([(62, 1), (114, .78), (166, .5)]):
            rows.append(f'<g opacity="{op}"><circle cx="70" cy="{y+18}" r="18" fill="url(#tg{uid})"/>'
                        f'<rect x="100" y="{y+4}" width="96" height="12" rx="6" fill="url(#tg{uid})"/>'
                        f'<rect x="100" y="{y+24}" width="66" height="12" rx="6" fill="url(#tg{uid})" opacity=".7"/></g>')
        return (f'<linearGradient id="tg{uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#cfd5e3"/></linearGradient>',
                f'<g filter="url(#sh{uid})">{"".join(rows)}</g><circle cx="196" cy="70" r="11" fill="#1d9bf0"/>')
    if key == "settings":
        import math as M
        teeth=24; ro=92; ri=80; pts=[]
        for i in range(teeth*4):
            a=2*M.pi*i/(teeth*4) - M.pi/2
            r = ro if (i%4 in (1,2)) else ri
            pts.append(f"{128+r*M.cos(a):.2f},{128+r*M.sin(a):.2f}")
        outer="M"+" L".join(pts)+"Z"
        spokes="".join(f'<rect x="122" y="80" width="12" height="52" rx="6" fill="#8d9099" transform="rotate({a} 128 128)"/>' for a in (0,120,240))
        return (f'<linearGradient id="sg{uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fafbfc"/><stop offset="1" stop-color="#b4b7bf"/></linearGradient>'
                f'<linearGradient id="si{uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#7c7f88"/><stop offset="1" stop-color="#b9bcc4"/></linearGradient>',
                f'<g filter="url(#sh{uid})"><path d="{outer}" fill="url(#sg{uid})"/><path d="{outer}" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="2"/>'
                f'<circle cx="128" cy="128" r="62" fill="url(#si{uid})"/><circle cx="128" cy="128" r="50" fill="#e7e8ec"/>'
                f'<circle cx="128" cy="128" r="50" fill="url(#spec{uid})"/>{spokes}<circle cx="128" cy="128" r="14" fill="#8d9099"/></g>')
    if key == "pulse":
        heart = "M128 206s-78-44-78-102c0-28 21-48 46-48 15 0 26 8 32 18 6-10 17-18 32-18 25 0 46 20 46 48 0 58-78 102-78 102Z"
        return (f'<linearGradient id="hg{uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ff6b8a"/><stop offset="1" stop-color="#ff1f4b"/></linearGradient>',
                f'<g filter="url(#sh{uid})"><path d="{heart}" fill="url(#hg{uid})"/><path d="{heart}" fill="url(#spec{uid})"/>'
                f'<path d="{heart}" fill="none" stroke="#fff" stroke-opacity=".45" stroke-width="2.4"/></g>'
                '<path d="M60 128 L100 128 L114 98 L132 158 L146 118 L156 128 L196 128" fill="none" stroke="#fff" stroke-width="11" stroke-linecap="round" stroke-linejoin="round"/>')
    if key == "ore":
        facets = [("M128 46 L196 104 L128 214Z", "#ffcf6b", "#ff9f1c"), ("M128 46 L60 104 L128 214Z", "#ffb347", "#e86a00"),
                  ("M60 104 L196 104 L128 214Z", "#ff9f1c", "#c95400"), ("M92 62 L164 62 L196 104 L60 104Z", "#ffe7a3", "#ffc04d")]
        d = []; g = []
        for i, (p, a, b) in enumerate(facets):
            d.append(f'<linearGradient id="of{i}{uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{a}"/><stop offset="1" stop-color="{b}"/></linearGradient>')
            g.append(f'<path d="{p}" fill="url(#of{i}{uid})"/>')
        return ("".join(d), f'<g filter="url(#sh{uid})">{"".join(g)}<path d="M92 62 L164 62 L196 104 L128 214 L60 104Z" fill="none" stroke="#fff3cf" stroke-width="3" stroke-linejoin="round" stroke-opacity=".8"/></g>')
    if key == "profile":
        return (f'<linearGradient id="pg{uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#b9c0cf"/><stop offset="1" stop-color="#7f879a"/></linearGradient>',
                f'<g filter="url(#sh{uid})"><circle cx="128" cy="100" r="40" fill="url(#pg{uid})"/><circle cx="128" cy="100" r="40" fill="url(#spec{uid})"/>'
                f'<path d="M58 206c4-38 34-62 70-62s66 24 70 62Z" fill="url(#pg{uid})"/><path d="M58 206c4-38 34-62 70-62s66 24 70 62Z" fill="url(#spec{uid})"/></g>')
    if key == "week":
        dots = []
        for r in range(2):
            for c in range(4):
                x = 66 + c * 41; y = 146 + r * 40
                fill = "#ff3b30" if (r, c) == (0, 2) else "#d7d9df"
                dots.append(f'<circle cx="{x}" cy="{y}" r="13" fill="{fill}"/>')
        return ("", '<text x="128" y="92" text-anchor="middle" font-family="Inter" font-weight="700" font-size="40" fill="#ff3b30" letter-spacing="2">WEEK</text>' + "".join(dots))
    raise KeyError(key)

def icon(name):
    t, g = ICONS[name]; uid = name
    if g[0] == "ph":
        _, phn, cols, sc = g
        return f'<svg width="{S}" height="{S}" viewBox="0 0 {S} {S}"><defs>{defs(uid, t, cols)}</defs>{frame(uid, t, glass_glyph(uid, ph(phn), sc))}</svg>'
    extra_defs, body = custom(uid, g[1])
    return f'<svg width="{S}" height="{S}" viewBox="0 0 {S} {S}"><defs>{defs(uid, t)}{extra_defs}</defs>{frame(uid, t, body)}</svg>'

names = list(ICONS)
cols = 6; gap = 24
html = ['<!doctype html><html><head><meta charset="utf-8">',
        '<link href="https://fonts.googleapis.com/css2?family=Inter:wght@700&display=swap" rel="stylesheet">',
        f'<style>html,body{{margin:0;background:transparent}}.g{{display:grid;grid-template-columns:repeat({cols},{S}px);gap:{gap}px;padding:{gap}px}}svg{{display:block}}</style></head><body><div class="g">']
for n in names: html.append(icon(n))
html.append('</div></body></html>')
open(os.path.join(HERE, "icons.html"), "w").write("".join(html))
json.dump({"names": names, "cols": cols, "size": S, "gap": gap}, open(os.path.join(HERE, "layout.json"), "w"))
print(len(names), "icons")
