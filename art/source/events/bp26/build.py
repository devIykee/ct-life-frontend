"""Breakpoint 2026 London skin (`london_2026`): the print atlas for banners, flags, booths, the keynote screen, the kiosks,
the countdown totem, the IRL board and the event nods.

Look (2026-10-24, from the live solana.com/breakpoint page's computed CSS): black #000 and white on neutrals #1E1E1E /
#353535 / #4E4E4E / #A2A2A2, Breakpoint purple #AA67FB as the hero colour, accents green #14F195, blue #3C91FF, lime
#C0E021, pink #E65DDB used sparingly; a large clean grotesk (Geist, OFL, a free lookalike: NOT their licensed face) with
UPPERCASE MONOSPACE labels (Geist Mono, OFL); a pixel-stepped "glitch" edge over a purple duotone halftone London
landmark (our own vector Elizabeth Tower, not their photography). No Breakpoint wordmark, no Solana logomark, nothing
implying endorsement: "Breakpoint 2026 · London" is our own typesetting. Exhibitors are in-game brands only.

Regenerate (writes atlas.html + layout.json next to this file, then the PNG):
  python3 -I art/source/events/bp26/build.py
  "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --headless=new --disable-gpu --hide-scrollbars \
    --force-device-scale-factor=1 --default-background-color=00000000 --virtual-time-budget=4000 \
    --window-size=2048,4096 --screenshot=art/source/events/bp26/bp26_atlas.png "file://$PWD/art/source/events/bp26/atlas.html"
Unity reads layout.json (pixel rects, origin top-left, atlas 2048 x 4096) through BreakpointSkinBuilder.
"""
import json, os, random

HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..', 'client', 'Assets', 'Resources', 'UI', 'Fonts'))
W, H = 2048, 4096

BLACK, N1, N2, N3, N4, WHITE = '#000000', '#1e1e1e', '#353535', '#4e4e4e', '#a2a2a2', '#ffffff'
PURPLE, GREEN, BLUE, LIME, PINK, GOLD = '#aa67fb', '#14f195', '#3c91ff', '#c0e021', '#e65ddb', '#f2b33d'

# exhibitors: in-game brands only; one accent each, on the icon only
BOOTHS = [
    ('Validator Row', 'Stake with the city', GREEN, 'shield'),
    ('Duo', 'The phone is the wallet', BLUE, 'phone'),
    ('Perp Pit', 'Leverage, responsibly', LIME, 'chart'),
    ('Buildooor Station', 'Ship before the keynote', PINK, 'code'),
    ('Timeline', 'Post it before it is alpha', PURPLE, 'feed'),
    ('CT Parcel', 'Same-day, door to door', WHITE, 'box'),
]

SIZES = [('fascia', 2048, 200), ('busad', 1024, 176), ('arch', 1024, 168)]
SIZES += [(f'booth_{i + 1}', 576, 500) for i in range(6)]
SIZES += [('screen', 1024, 576), ('upcoming', 512, 768), ('irl_board', 512, 768)]
SIZES += [(f'vban_{i + 1}', 192, 768) for i in range(4)]
SIZES += [('flag_a', 192, 576), ('flag_b', 192, 576)]
SIZES += [(f'counter_{i + 1}', 512, 104) for i in range(6)]
SIZES += [(f'head_{i + 1}', 576, 72) for i in range(6)]
SIZES += [('kiosk', 320, 512), ('stream', 320, 512), ('stage_l', 192, 768), ('stage_r', 192, 768), ('nod', 256, 256), ('nod_back', 256, 256)]


def pack(sizes):
    """Shelf packer, tallest first, 8 px gutters."""
    order = sorted(sizes, key=lambda s: -s[2])
    rects, x, y, shelf = {}, 0, 0, 0
    for n, w, h in order:
        if x + w > W:
            x, y, shelf = 0, y + shelf + 8, 0
        rects[n] = (x, y, w, h)
        x += w + 8
        shelf = max(shelf, h)
    assert y + shelf <= H, 'atlas overflow'
    return rects


def steps(w, h, seed, color=BLACK, side='bottom', rows=7, cell=None):
    """The pixel-stepped glitch edge: a staircase of hard-edged blocks rising from one side, deterministic per seed."""
    rnd = random.Random(seed)
    cell = cell or max(6, h // rows)
    out, x = [], 0
    level = rnd.randint(1, 2)
    while x < w:
        bw = rnd.choice([1, 1, 2, 3, 5, 8]) * cell
        level = max(0, min(rows, level + rnd.choice([-1, 0, 0, 1, 1])))
        bh = level * cell
        out.append(f'<div style="position:absolute;left:{x}px;{side}:0;width:{bw}px;height:{bh}px;background:{color}"></div>')
        # a few detached pixels beyond the edge (the sorted-glitch flecks)
        if rnd.random() < 0.35 and bh > 0:
            fx = x + rnd.randint(0, max(0, bw - cell))
            fy = bh + cell * rnd.randint(1, 2)
            out.append(f'<div style="position:absolute;left:{fx}px;{side}:{fy}px;width:{cell}px;height:{cell}px;background:{color}"></div>')
        x += bw
    return f'<div style="position:absolute;left:0;right:0;{side}:0;height:{h}px;overflow:hidden">{"".join(out)}</div>'


_tower_n = [0]


def tower(size, color=BLACK, opacity=0.55):
    """Our own vector of the Elizabeth Tower, filled with a halftone dot pattern (the duotone landmark of the look)."""
    _tower_n[0] += 1
    pid = f'ht{_tower_n[0]}'
    return (f'<svg width="{size * 0.42:.0f}" height="{size}" viewBox="0 0 42 100" style="opacity:{opacity};display:block">'
            f'<defs><pattern id="{pid}" width="1.6" height="1.6" patternUnits="userSpaceOnUse"><circle cx=".8" cy=".8" r=".55" fill="{color}"/></pattern></defs>'
            f'<g fill="url(#{pid})"><path d="M21 0 L23 9 L25 14 L25 18 L17 18 L17 14 L19 9 Z"/><rect x="15" y="18" width="12" height="6"/>'
            f'<rect x="13" y="24" width="16" height="18"/><rect x="11" y="42" width="20" height="58"/>'
            f'<rect x="6" y="70" width="5" height="30"/><rect x="31" y="70" width="5" height="30"/></g>'
            f'<circle cx="21" cy="33" r="5.5" fill="none" stroke="{color}" stroke-width="1.2"/>'
            f'<path d="M21 33 L21 29.5 M21 33 L23.5 34" stroke="{color}" stroke-width=".9"/></svg>')


ICONS = {
    'shield': '<path d="M50 6 88 20v28c0 24-17 40-38 46C29 88 12 72 12 48V20Z" fill="none" stroke="currentColor" stroke-width="8" stroke-linejoin="round"/><path d="m34 50 12 12 22-24" fill="none" stroke="currentColor" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/>',
    'phone': '<rect x="28" y="6" width="44" height="88" rx="10" fill="none" stroke="currentColor" stroke-width="8"/><circle cx="50" cy="80" r="5" fill="currentColor"/>',
    'chart': '<path d="M8 80 34 50l18 16 38-44" fill="none" stroke="currentColor" stroke-width="9" stroke-linecap="round" stroke-linejoin="round"/><path d="M70 20h22v22" fill="none" stroke="currentColor" stroke-width="9" stroke-linecap="round" stroke-linejoin="round"/>',
    'code': '<path d="m34 24-24 26 24 26M66 24l24 26-24 26M58 14 42 86" fill="none" stroke="currentColor" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/>',
    'feed': '<rect x="10" y="12" width="80" height="20" fill="currentColor"/><rect x="10" y="40" width="80" height="20" fill="currentColor" opacity=".7"/><rect x="10" y="68" width="56" height="20" fill="currentColor" opacity=".45"/>',
    'box': '<path d="M50 8 90 28v44L50 92 10 72V28Z" fill="none" stroke="currentColor" stroke-width="8" stroke-linejoin="round"/><path d="M10 28l40 20 40-20M50 48v44" fill="none" stroke="currentColor" stroke-width="8" stroke-linejoin="round"/>',
}


def icon(name, size, color):
    return f'<svg width="{size}" height="{size}" viewBox="0 0 100 100" style="color:{color}">{ICONS[name]}</svg>'


def cell(n, r, inner, style=''):
    x, y, w, h = r
    return f'<div class="c" id="{n}" style="left:{x}px;top:{y}px;width:{w}px;height:{h}px;{style}">{inner}</div>'


def bars(color):
    return ''.join(f'<div style="width:14px;height:{8 + (k % 3) * 8}px;background:{color}"></div>' for k in range(6))


def build():
    R = pack(SIZES)
    p = []
    # fascia: black band, the name in the grotesk, mono details, the stepped purple edge on the right
    p.append(cell('fascia', R['fascia'],
        f'<div style="position:absolute;right:0;top:0;bottom:0;width:560px;background:{PURPLE}"></div>'
        f'<div style="position:absolute;right:0;top:0;bottom:0;width:620px">{steps(620, 200, 11, BLACK, "bottom", 8, 25)}</div>'
        f'<div class="row" style="position:absolute;left:64px;top:0;bottom:0;gap:40px">'
        f'<div class="wm" style="font-size:112px">Breakpoint 2026</div>'
        f'<div class="mono" style="font-size:30px;line-height:1.45;color:{N4}">15–17 NOVEMBER 2026<br>OLYMPIA · LONDON</div></div>',
        f'background:{BLACK};color:{WHITE}'))
    p.append(cell('busad', R['busad'],
        f'{steps(1024, 70, 21, BLACK, "bottom", 5, 14)}'
        f'<div class="row" style="position:absolute;left:36px;right:36px;top:22px;gap:22px"><div class="wm" style="font-size:72px;color:{BLACK}">Breakpoint 2026</div>'
        f'<div style="flex:1"></div><div class="mono" style="font-size:24px;line-height:1.4;color:{BLACK};text-align:right">LONDON<br>15–17 NOV</div></div>',
        f'background:{PURPLE}'))
    p.append(cell('arch', R['arch'],
        f'<div style="position:absolute;right:0;top:0;width:340px;height:168px">{steps(340, 168, 31, BLACK, "top", 6, 28)}</div>'
        f'<div class="row" style="position:absolute;left:44px;top:0;bottom:0;gap:24px"><div class="wm" style="font-size:78px;color:{WHITE}">Breakpoint 2026</div></div>'
        f'<div class="mono" style="position:absolute;right:40px;bottom:30px;font-size:26px;color:{WHITE}">LONDON ↗</div>',
        f'background:{PURPLE}'))
    for i, (name, tag, accent, ic) in enumerate(BOOTHS):
        p.append(cell(f'booth_{i + 1}', R[f'booth_{i + 1}'],
            f'<div style="position:absolute;left:0;right:0;top:0;height:150px;background:{PURPLE}"></div>'
            f'<div style="position:absolute;left:0;right:0;top:40px;height:130px">{steps(576, 110, 40 + i, BLACK, "bottom", 5, 22)}</div>'
            f'<div style="position:absolute;right:0;top:0;padding:18px 30px 14px 22px;text-align:right;background:{PURPLE};z-index:3" class="mono"><span style="font-size:20px;color:{BLACK}">BOOTH</span><br>'
            f'<span style="font-size:54px;color:{BLACK};font-weight:600">{i + 1:02d}</span></div>'
            f'<div style="position:absolute;left:40px;top:196px">{icon(ic, 84, accent)}</div>'
            f'<div class="wm" style="position:absolute;left:40px;right:30px;top:298px;font-size:{68 if len(name) < 11 else 52}px;white-space:normal;line-height:.98">{name}</div>'
            f'<div class="mono" style="position:absolute;left:40px;bottom:66px;font-size:19px;color:{N4}">{tag.upper()}</div>'
            f'<div class="mono" style="position:absolute;left:40px;right:40px;bottom:28px;font-size:15px;color:{N3};border-top:2px solid {N2};padding-top:10px">BREAKPOINT 2026 · EXPO · LONDON</div>',
            f'background:{BLACK};color:{WHITE}'))
        p.append(cell(f'counter_{i + 1}', R[f'counter_{i + 1}'],
            f'<div class="row" style="height:100%;padding:0 26px;gap:16px">{icon(ic, 52, accent)}'
            f'<div class="wm" style="font-size:40px">{name}</div></div>',
            f'background:{BLACK};color:{WHITE};border-top:8px solid {PURPLE}'))
        p.append(cell(f'head_{i + 1}', R[f'head_{i + 1}'],
            f'<div class="row" style="height:100%;padding:0 22px;gap:14px"><div class="mono" style="font-size:26px;color:{PURPLE}">BOOTH {i + 1:02d}</div>'
            f'<div style="flex:1;height:2px;background:{N3}"></div><div class="mono" style="font-size:22px;color:{WHITE}">BREAKPOINT 2026</div></div>',
            f'background:{BLACK}'))
    # keynote screen (the live countdown is text on top: rows 38%..74% left clear)
    p.append(cell('screen', R['screen'],
        f'<div style="position:absolute;right:30px;bottom:60px">{tower(400, PURPLE, 0.6)}</div>'
        f'<div style="position:absolute;left:0;right:0;bottom:0;height:70px;background:{PURPLE}"></div>'
        f'<div style="position:absolute;left:0;right:0;bottom:60px;height:110px">{steps(1024, 110, 51, PURPLE, "bottom", 5, 22)}</div>'
        f'<div class="mono" style="position:absolute;left:56px;top:44px;font-size:24px;color:{PURPLE}">KEYNOTE STAGE · LONDON</div>'
        f'<div class="wm" style="position:absolute;left:52px;top:84px;font-size:84px">Breakpoint 2026</div>'
        f'<div class="mono" style="position:absolute;left:56px;bottom:22px;font-size:22px;color:{BLACK}">15–17 NOVEMBER 2026 · OLYMPIA CONVENTION CENTRE</div>',
        f'background:{BLACK};color:{WHITE}'))
    # the countdown totem outside the event (live text in the dark window at 340..490 px)
    p.append(cell('upcoming', R['upcoming'],
        f'<div style="position:absolute;left:0;right:0;top:0;height:300px;background:{PURPLE};overflow:hidden">'
        f'<div style="position:absolute;right:4px;bottom:-30px">{tower(320, BLACK, 0.45)}</div></div>'
        f'<div style="position:absolute;left:0;right:0;top:220px;height:90px">{steps(512, 90, 61, BLACK, "bottom", 5, 18)}</div>'
        f'<div class="mono" style="position:absolute;left:34px;top:34px;font-size:20px;color:{BLACK}">COMING TO THE CENTER</div>'
        f'<div class="wm" style="position:absolute;left:30px;top:70px;font-size:74px;color:{BLACK};line-height:.95">Break<br>point</div>'
        f'<div class="wm" style="position:absolute;left:34px;top:262px;font-size:42px;color:{WHITE}">2026</div>'
        f'<div style="position:absolute;left:32px;right:32px;top:340px;height:150px;background:{N1};border:2px solid {N2}"></div>'
        f'<div class="mono" style="position:absolute;left:34px;bottom:96px;font-size:24px">LONDON</div>'
        f'<div class="mono" style="position:absolute;left:34px;bottom:62px;font-size:18px;color:{N4}">15–17 NOV · OLYMPIA</div>'
        f'<div style="position:absolute;left:0;right:0;bottom:0;height:34px">{steps(512, 34, 62, PURPLE, "bottom", 3, 10)}</div>',
        f'background:{BLACK};color:{WHITE}'))
    # the IRL attendee board (names are text on the list: 236 px from the top to 44 px from the bottom)
    p.append(cell('irl_board', R['irl_board'],
        f'<div class="row" style="position:absolute;left:34px;top:40px;gap:12px"><div style="width:14px;height:14px;background:{GOLD}"></div>'
        f'<div class="mono" style="font-size:20px;color:{GOLD}">LIVE AT OLYMPIA</div></div>'
        f'<div class="wm" style="position:absolute;left:30px;top:76px;font-size:96px">IRL here</div>'
        f'<div class="mono" style="position:absolute;left:34px;top:188px;font-size:15px;color:{N4}">PLAYERS CHECKED IN AT BREAKPOINT</div>'
        f'<div style="position:absolute;left:32px;right:32px;top:236px;bottom:44px;background:{N1};border:2px solid {N2}"></div>'
        f'<div style="position:absolute;left:0;right:0;bottom:0;height:30px">{steps(512, 30, 71, PURPLE, "bottom", 3, 10)}</div>',
        f'background:{BLACK};color:{WHITE}'))
    vtexts = [('Breakpoint', WHITE, BLACK), ('London', BLACK, PURPLE), ('15–17 Nov', WHITE, BLACK), ('2026', BLACK, PURPLE)]
    for i, (t, fg, bg) in enumerate(vtexts):
        edge = PURPLE if bg == BLACK else BLACK
        p.append(cell(f'vban_{i + 1}', R[f'vban_{i + 1}'],
            f'<div class="mono" style="position:absolute;left:0;right:0;top:26px;text-align:center;font-size:15px;color:{fg};opacity:.75">LDN · 26</div>'
            f'<div class="vt" style="color:{fg};font-size:{min(118, int(600 / (0.6 * len(t))))}px">{t}</div>'
            f'<div style="position:absolute;left:0;right:0;bottom:0;height:120px">{steps(192, 120, 80 + i, edge, "bottom", 6, 16)}</div>',
            f'background:{bg}'))
    for n, bg, fg, t in (('flag_a', PURPLE, BLACK, 'LDN 26'), ('flag_b', BLACK, WHITE, 'London')):
        edge = BLACK if bg == PURPLE else PURPLE
        p.append(cell(n, R[n],
            f'<div class="mono" style="position:absolute;left:0;right:0;top:26px;text-align:center;font-size:17px">BREAKPOINT</div>'
            f'<div class="vt" style="font-size:72px;top:56px;height:400px">{t}</div>'
            f'<div style="position:absolute;left:0;right:0;bottom:0;height:90px">{steps(192, 90, 90 + len(n), edge, "bottom", 5, 14)}</div>',
            f'background:{bg};color:{fg}'))
    p.append(cell('kiosk', R['kiosk'],
        f'<div class="mono" style="position:absolute;left:28px;top:30px;font-size:17px;color:{PURPLE}">KEYNOTE</div>'
        f'<div class="wm" style="position:absolute;left:26px;top:58px;font-size:52px">Trivia</div>'
        f'<div style="position:absolute;left:28px;top:138px;width:110px;height:110px;background:{PURPLE};color:{BLACK};font:600 96px Geist;text-align:center;line-height:110px">?</div>'
        f'<div class="mono" style="position:absolute;left:28px;top:276px;font-size:16px;line-height:1.6;color:{N4}">4 QUESTIONS<br>A · B · C · D</div>'
        f'<div class="mono" style="position:absolute;left:28px;right:28px;bottom:30px;height:62px;line-height:62px;text-align:center;background:{PURPLE};color:{BLACK};font-size:20px">TAP TO PLAY</div>',
        f'background:{BLACK};color:{WHITE}'))
    p.append(cell('stream', R['stream'],
        f'<div class="mono" style="position:absolute;left:28px;top:30px;font-size:17px;color:{PURPLE}">● LIVE</div>'
        f'<div class="wm" style="position:absolute;left:26px;top:58px;font-size:44px;line-height:1">Watch<br>the stream</div>'
        f'<div style="position:absolute;left:30px;top:178px;width:0;height:0;border-left:66px solid {PURPLE};border-top:40px solid transparent;border-bottom:40px solid transparent"></div>'
        f'<div class="mono" style="position:absolute;left:28px;top:284px;font-size:14px;line-height:1.6;color:{N4}">OPENS THE OFFICIAL<br>STREAM IN YOUR BROWSER</div>'
        f'<div class="mono" style="position:absolute;left:28px;right:28px;bottom:30px;height:62px;line-height:62px;text-align:center;background:{WHITE};color:{BLACK};font-size:20px">WATCH ↗</div>',
        f'background:{BLACK};color:{WHITE}'))
    for n, t, s in (('stage_l', 'Keynote', 101), ('stage_r', 'London', 102)):
        p.append(cell(n, R[n],
            f'<div class="mono" style="position:absolute;left:0;right:0;top:26px;text-align:center;font-size:15px;color:{PURPLE}">STAGE · 01</div>'
            f'<div class="vt" style="color:{WHITE};font-size:100px">{t}</div>'
            f'<div style="position:absolute;left:0;right:0;bottom:0;height:120px">{steps(192, 120, s, PURPLE, "bottom", 6, 16)}</div>',
            f'background:{BLACK}'))
    p.append(cell('nod', R['nod'],
        f'<div style="position:absolute;inset:0;border-radius:50%;background:{PURPLE};overflow:hidden">'
        f'<div style="position:absolute;left:0;right:0;bottom:0;height:90px">{steps(256, 90, 111, BLACK, "bottom", 5, 16)}</div></div>'
        f'<div class="nod"><div class="mono" style="font-size:20px;color:{BLACK}">LONDON</div><div class="wm" style="font-size:76px;color:{BLACK}">LDN</div>'
        f'<div class="mono" style="font-size:24px;color:{WHITE};margin-top:6px">2026</div></div>',
        f'border-radius:50%;background:{WHITE}'))
    p.append(cell('nod_back', R['nod_back'],
        f'<div style="position:absolute;inset:12px;border-radius:50%;background:{BLACK}"></div>'
        f'<div class="nod"><div class="mono" style="font-size:18px;color:{PURPLE}">EVENT NOD</div>'
        f'<div style="margin-top:12px;display:flex;gap:6px;align-items:flex-end">{bars(PURPLE)}</div></div>',
        f'border-radius:50%;background:{PURPLE}'))

    css = f"""
@font-face{{font-family:Geist;font-weight:600;src:url('file://{FONTS}/Events/Geist-SemiBold.ttf')}}
@font-face{{font-family:Geist;font-weight:500;src:url('file://{FONTS}/Events/Geist-Medium.ttf')}}
@font-face{{font-family:Geist;font-weight:700;src:url('file://{FONTS}/Events/Geist-Bold.ttf')}}
@font-face{{font-family:GeistMono;font-weight:500;src:url('file://{FONTS}/Events/GeistMono-Medium.ttf')}}
@font-face{{font-family:GeistMono;font-weight:600;src:url('file://{FONTS}/Events/GeistMono-SemiBold.ttf')}}
html,body{{margin:0;width:{W}px;height:{H}px;background:transparent;overflow:hidden}}
.c{{position:absolute;overflow:hidden;box-sizing:border-box;font-family:Geist}}
.row{{display:flex;align-items:center}}
.wm{{font-family:Geist;font-weight:600;letter-spacing:-.045em;line-height:1;white-space:nowrap}}
.mono{{font-family:GeistMono;font-weight:500;letter-spacing:.06em;text-transform:uppercase;white-space:nowrap}}
.vt{{position:absolute;left:0;right:0;top:60px;display:flex;justify-content:center;font-family:Geist;font-weight:600;letter-spacing:-.04em;
     writing-mode:vertical-rl;transform:rotate(180deg);white-space:nowrap;line-height:1;height:540px;align-items:center}}
.nod{{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center}}
"""
    html = f'<!doctype html><html><head><meta charset="utf-8"><style>{css}</style></head><body>{"".join(p)}</body></html>'
    open(os.path.join(HERE, 'atlas.html'), 'w').write(html)
    layout = {'width': W, 'height': H, 'rects': {n: list(r) for n, r in R.items()},
              'booths': [{'name': b[0], 'tagline': b[1], 'accent': b[2]} for b in BOOTHS]}
    json.dump(layout, open(os.path.join(HERE, 'layout.json'), 'w'), indent=1)
    print('atlas.html + layout.json', len(R), 'cells')


if __name__ == '__main__':
    build()
