#!/usr/bin/env python3
"""Casino art studio (2026-10-24, renamed 2026-10-26: names from contract/ids/names.json): original vector art for the casino, rendered with headless Chrome.

Writes one HTML page per asset into out/html/, then (render.sh) screenshots each at its exact size with a transparent
background. All shapes are drawn here (original); lettering uses Google Fonts under the SIL OFL / Apache 2.0
(Luckiest Guy: Apache 2.0; Righteous, Yellowtail: OFL). No third-party marks, mascots or likenesses.

Assets (id: size):
  reel symbols  rocket, diamond_hands, ape, laser_eyes, rug, wild   512x512
  coins         coin_heads (bull), coin_tails (bear)                512x512
  crash         crash_rocket 256x256, crash_rug 512x512
  wordmark      rugroom_logo 1024x384 (neon)
"""
import html, json, math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', '..', '..', 'tools'))
import names_table as N   # noqa: E402  names from contract/ids/names.json (QUALITY-BAR.md), never hard-coded
CASINO = html.escape(N.upper('venue.rug_room'))
U = lambda k: html.escape(N.upper(k))
OUT = os.path.join(HERE, 'out', 'html')
os.makedirs(OUT, exist_ok=True)

OL = '#1a0f2c'          # outline ink
FONTS = '<link href="https://fonts.googleapis.com/css2?family=Luckiest+Guy&family=Righteous&family=Yellowtail&display=block" rel="stylesheet">'


def lg(id_, stops, x1=0, y1=0, x2=0, y2=1):
    s = ''.join(f'<stop offset="{o}" stop-color="{c}"{"" if a is None else f" stop-opacity=\"{a}\""}/>' for o, c, a in stops)
    return f'<linearGradient id="{id_}" x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}">{s}</linearGradient>'


def rg(id_, stops, cx=.5, cy=.5, r=.5, fx=None, fy=None):
    f = '' if fx is None else f' fx="{fx}" fy="{fy}"'
    s = ''.join(f'<stop offset="{o}" stop-color="{c}"{"" if a is None else f" stop-opacity=\"{a}\""}/>' for o, c, a in stops)
    return f'<radialGradient id="{id_}" cx="{cx}" cy="{cy}" r="{r}"{f}>{s}</radialGradient>'


COMMON = (
    '<filter id="drop" x="-20%" y="-20%" width="140%" height="150%"><feDropShadow dx="0" dy="10" stdDeviation="9" flood-color="#000" flood-opacity=".38"/></filter>'
    '<filter id="glow" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="10"/></filter>'
    '<filter id="glow2" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="4"/></filter>'
    + lg('spec', [(0, '#fff', .75), (.55, '#fff', 0)])
    + lg('specS', [(0, '#fff', .9), (1, '#fff', 0)])
)


def page(svg, w, h):
    return (f'<!doctype html><html><head><meta charset="utf-8">{FONTS}<style>html,body{{margin:0;background:transparent;overflow:hidden}}'
            f'svg{{display:block}}</style></head><body>{svg}</body></html>')


def svg(w, h, defs, body):
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}"><defs>{COMMON}{defs}</defs>{body}</svg>'


# ---------------------------------------------------------------- rocket (upright in a 512 box, rotated by the caller)
def rocket_parts(flame=True):
    d = (lg('rkBody', [(0, '#ffffff', None), (.45, '#e9edf5', None), (1, '#9aa4b8', None)], 0, 0, 1, 0)
         + lg('rkRed', [(0, '#ff6a5c', None), (1, '#c4142b', None)], 0, 0, 1, 1)
         + rg('rkGlass', [(0, '#bff3ff', None), (.5, '#33b6ff', None), (1, '#0b4f9e', None)], .38, .35, .7)
         + lg('rkFlameO', [(0, '#fff6c2', None), (.35, '#ffcf3a', None), (.75, '#ff7a1a', None), (1, '#ff3d1a', 0)])
         + lg('rkFlameI', [(0, '#ffffff', None), (.6, '#fff1a8', None), (1, '#ffd23a', 0)]))
    body = ''
    if flame:
        body += ('<path d="M216 384 C196 430 214 470 256 512 C298 470 316 430 296 384 Z" fill="url(#rkFlameO)"/>'
                 '<path d="M234 384 C224 420 236 448 256 476 C276 448 288 420 278 384 Z" fill="url(#rkFlameI)"/>')
    # fins (behind the body)
    body += (f'<path d="M206 286 C168 306 150 344 150 392 L210 370 Z" fill="url(#rkRed)" stroke="{OL}" stroke-width="10" stroke-linejoin="round"/>'
             f'<path d="M306 286 C344 306 362 344 362 392 L302 370 Z" fill="url(#rkRed)" stroke="{OL}" stroke-width="10" stroke-linejoin="round"/>')
    # nozzle
    body += f'<path d="M222 372 L290 372 L280 398 L232 398 Z" fill="#59627a" stroke="{OL}" stroke-width="10" stroke-linejoin="round"/>'
    # body
    hull = 'M256 40 C320 96 330 190 314 300 L306 378 L206 378 L198 300 C182 190 192 96 256 40 Z'
    body += f'<path d="{hull}" fill="url(#rkBody)" stroke="{OL}" stroke-width="10" stroke-linejoin="round"/>'
    # nose cone (red cap) clipped to the hull
    body += (f'<clipPath id="rkClip"><path d="{hull}"/></clipPath>'
             '<g clip-path="url(#rkClip)"><path d="M150 40 L362 40 L362 134 C320 120 192 120 150 134 Z" fill="url(#rkRed)"/>'
             '<path d="M150 336 L362 336 L362 400 L150 400 Z" fill="url(#rkRed)"/>'
             '<path d="M214 70 C206 140 206 240 216 330" fill="none" stroke="#fff" stroke-opacity=".85" stroke-width="16" stroke-linecap="round"/></g>')
    body += f'<path d="M193 134 C230 122 282 122 319 134" fill="none" stroke="{OL}" stroke-width="8"/>'
    body += f'<path d="M199 336 L313 336" fill="none" stroke="{OL}" stroke-width="8"/>'
    # porthole
    body += (f'<circle cx="256" cy="214" r="44" fill="#d6dbe6" stroke="{OL}" stroke-width="10"/>'
             f'<circle cx="256" cy="214" r="30" fill="url(#rkGlass)" stroke="{OL}" stroke-width="6"/>'
             '<path d="M236 204 A22 22 0 0 1 258 190" fill="none" stroke="#fff" stroke-width="7" stroke-linecap="round" stroke-opacity=".9"/>')
    # center fin
    body += f'<path d="M256 318 L256 392" stroke="{OL}" stroke-width="18" stroke-linecap="round"/><path d="M256 318 L256 392" stroke="url(#rkRed)" stroke-width="8" stroke-linecap="round"/>'
    return d, body


def a_rocket():
    d, b = rocket_parts()
    stars = ''.join(f'<path d="M{x} {y-r} L{x+r*.28} {y-r*.28} L{x+r} {y} L{x+r*.28} {y+r*.28} L{x} {y+r} L{x-r*.28} {y+r*.28} L{x-r} {y} L{x-r*.28} {y-r*.28}Z" fill="#fff6c8"/>'
                    for x, y, r in [(92, 120, 18), (420, 410, 14), (440, 92, 10), (70, 380, 9)])
    return svg(512, 512, d, f'{stars}<g filter="url(#drop)"><g transform="translate(256 256) rotate(38) scale(.86) translate(-256 -270)">{b}</g></g>')


# ---------------------------------------------------------------- diamond hands
def a_diamond_hands():
    d = (lg('dmA', [(0, '#e8fdff', None), (1, '#7fe3ff', None)])
         + lg('dmB', [(0, '#7fdcff', None), (1, '#1f8fe0', None)])
         + lg('dmC', [(0, '#38b2ff', None), (1, '#0a4fb8', None)])
         + lg('dmD', [(0, '#b9f3ff', None), (1, '#3cc0ff', None)])
         + lg('hand', [(0, '#ffd2a6', None), (1, '#e99a62', None)])
         + lg('cuff', [(0, '#6d4cff', None), (1, '#3a1fb8', None)]))
    # brilliant cut, table on top: crown y 120..196, pavilion to 360
    T, M, B = 112, 190, 372
    pts = {'tl': (196, T), 'tr': (316, T), 'gl': (110, M), 'gr': (402, M), 'b': (256, B), 'c1': (226, M), 'c2': (286, M), 'tm': (256, T)}
    def P(*ks): return ' '.join(f'{pts[k][0]},{pts[k][1]}' for k in ks)
    gem = (f'<polygon points="{P("tl","tr","c2","c1")}" fill="url(#dmA)"/>'
           f'<polygon points="{P("tl","c1","gl")}" fill="url(#dmD)"/>'
           f'<polygon points="{P("tr","gr","c2")}" fill="url(#dmB)"/>'
           f'<polygon points="{P("gl","c1","b")}" fill="url(#dmB)"/>'
           f'<polygon points="{P("c1","c2","b")}" fill="url(#dmD)"/>'
           f'<polygon points="{P("c2","gr","b")}" fill="url(#dmC)"/>'
           f'<polygon points="{P("tl","tr","gr","b","gl")}" fill="none" stroke="{OL}" stroke-width="11" stroke-linejoin="round"/>'
           f'<polyline points="{P("gl","c1","c2","gr")}" fill="none" stroke="{OL}" stroke-width="6" stroke-linejoin="round"/>'
           f'<polyline points="{P("tl","c1","b","c2","tr")}" fill="none" stroke="{OL}" stroke-width="5" stroke-linejoin="round" stroke-opacity=".8"/>'
           '<polygon points="214,124 252,124 238,150" fill="#fff" opacity=".9"/>')
    # two cupped hands from below-left and below-right (mirror), fingers wrapping the pavilion
    def hand(m):
        g = (f'<g transform="translate(512 0) scale(-1 1)">' if m else '<g>')
        g += (f'<path d="M40 512 L52 420 C60 380 92 352 130 340 L176 326 C196 320 214 330 218 348 C222 366 210 380 192 386 L170 392 '
              f'C196 386 218 398 222 416 C226 434 212 446 194 450 L232 446 C252 444 264 458 262 474 C260 490 246 498 230 498 L170 512 Z" '
              f'fill="url(#hand)" stroke="{OL}" stroke-width="10" stroke-linejoin="round"/>'
              # thumb resting on the gem side
              f'<path d="M150 344 C150 300 170 260 204 246 C222 238 236 252 230 270 C222 296 212 318 196 338" fill="url(#hand)" stroke="{OL}" stroke-width="10" stroke-linejoin="round" stroke-linecap="round"/>'
              f'<path d="M174 392 L150 396 M196 450 L160 452" stroke="{OL}" stroke-width="6" stroke-linecap="round" fill="none" opacity=".7"/>'
              f'<path d="M70 420 C80 392 100 372 128 362" stroke="#fff" stroke-opacity=".55" stroke-width="10" stroke-linecap="round" fill="none"/>'
              f'<path d="M36 512 L46 452 L118 476 L104 512 Z" fill="url(#cuff)" stroke="{OL}" stroke-width="10" stroke-linejoin="round"/>'
              '</g>')
        return g
    sparkle = ''.join(f'<path d="M{x} {y-r} Q{x} {y} {x+r} {y} Q{x} {y} {x} {y+r} Q{x} {y} {x-r} {y} Q{x} {y} {x} {y-r}Z" fill="#fff"/>'
                      for x, y, r in [(410, 104, 26), (98, 150, 18), (432, 266, 12)])
    glow = '<ellipse cx="256" cy="230" rx="170" ry="150" fill="#5fd8ff" opacity=".35" filter="url(#glow)"/>'
    return svg(512, 512, d, f'{glow}<g filter="url(#drop)">{gem}{hand(False)}{hand(True)}</g>{sparkle}')


# ---------------------------------------------------------------- ape (original, friendly: wide eyes, big grin)
def a_ape():
    d = (rg('fur', [(0, '#a86a3d', None), (.7, '#7a4422', None), (1, '#5a2f16', None)], .45, .35, .7)
         + rg('mask', [(0, '#ffe2bf', None), (.75, '#f2b98a', None), (1, '#d99567', None)], .5, .4, .6)
         + rg('eye', [(0, '#ffffff', None), (1, '#e6ecf5', None)], .4, .35, .7)
         + rg('iris', [(0, '#7a4a1c', None), (1, '#3b1f0a', None)])
         + lg('mouth', [(0, '#7a1630', None), (1, '#3e0717', None)]))
    b = (f'<circle cx="92" cy="262" r="58" fill="url(#fur)" stroke="{OL}" stroke-width="10"/><circle cx="92" cy="262" r="30" fill="#e9a77a"/>'
         f'<circle cx="420" cy="262" r="58" fill="url(#fur)" stroke="{OL}" stroke-width="10"/><circle cx="420" cy="262" r="30" fill="#e9a77a"/>'
         # head with a fur tuft
         f'<path d="M256 58 C246 40 230 34 214 40 C232 46 236 56 232 66 C150 80 100 150 100 250 C100 372 170 452 256 452 C342 452 412 372 412 250 C412 150 360 76 276 64 C284 50 296 44 312 44 C296 32 270 36 256 58 Z" fill="url(#fur)" stroke="{OL}" stroke-width="10" stroke-linejoin="round"/>'
         # face mask: heart-ish upper + muzzle
         f'<path d="M256 168 C232 136 160 132 146 192 C136 236 150 266 160 284 C130 316 134 400 256 424 C378 400 382 316 352 284 C362 266 376 236 366 192 C352 132 280 136 256 168 Z" fill="url(#mask)" stroke="{OL}" stroke-width="9" stroke-linejoin="round"/>'
         # brows
         f'<path d="M168 178 C190 160 222 162 238 178" stroke="{OL}" stroke-width="12" stroke-linecap="round" fill="none"/>'
         f'<path d="M274 178 C290 162 322 160 344 178" stroke="{OL}" stroke-width="12" stroke-linecap="round" fill="none"/>'
         # big round eyes
         f'<ellipse cx="206" cy="226" rx="34" ry="38" fill="url(#eye)" stroke="{OL}" stroke-width="8"/>'
         f'<ellipse cx="306" cy="226" rx="34" ry="38" fill="url(#eye)" stroke="{OL}" stroke-width="8"/>'
         f'<circle cx="214" cy="232" r="19" fill="url(#iris)"/><circle cx="298" cy="232" r="19" fill="url(#iris)"/>'
         '<circle cx="214" cy="232" r="9" fill="#120a04"/><circle cx="298" cy="232" r="9" fill="#120a04"/>'
         '<circle cx="221" cy="223" r="6" fill="#fff"/><circle cx="305" cy="223" r="6" fill="#fff"/>'
         # nose
         f'<path d="M226 286 C236 272 276 272 286 286 C290 300 270 306 256 306 C242 306 222 300 226 286 Z" fill="#b8704a" stroke="{OL}" stroke-width="7"/>'
         f'<ellipse cx="243" cy="292" rx="7" ry="5" fill="{OL}"/><ellipse cx="269" cy="292" rx="7" ry="5" fill="{OL}"/>'
         # grin with teeth
         f'<path d="M180 330 C214 348 298 348 332 330 C330 380 296 404 256 404 C216 404 182 380 180 330 Z" fill="url(#mouth)" stroke="{OL}" stroke-width="9" stroke-linejoin="round"/>'
         '<path d="M190 336 C222 350 290 350 322 336 L318 352 C290 362 222 362 194 352 Z" fill="#fff"/>'
         '<path d="M218 384 C236 372 276 372 294 384 C282 398 230 398 218 384 Z" fill="#ff6b8a"/>'
         f'<path d="M168 324 C176 336 186 340 192 338 M344 324 C336 336 326 340 320 338" stroke="{OL}" stroke-width="7" stroke-linecap="round" fill="none"/>'
         # fur sheen
         '<path d="M150 130 C180 96 222 82 262 80" stroke="#fff" stroke-opacity=".35" stroke-width="16" stroke-linecap="round" fill="none"/>')
    return svg(512, 512, d, f'<g filter="url(#drop)" transform="translate(0 6)">{b}</g>')


# ---------------------------------------------------------------- laser eyes (an original stern face with red beams)
def a_laser_eyes():
    d = (rg('lzHead', [(0, '#4b4f7a', None), (.7, '#262848', None), (1, '#15162b', None)], .4, .3, .75)
         + lg('beam', [(0, '#ffffff', None), (.15, '#ff3b3b', None), (1, '#ff1a1a', 0)], 0, 0, 1, 0)
         + rg('lzEye', [(0, '#ffffff', None), (.35, '#ffd0d0', None), (.6, '#ff2a2a', None), (1, '#ff0000', 0)]))
    beams = ''
    for ex in (200, 312):
        side = -1 if ex < 256 else 1
        x2 = ex + side * 330
        beams += (f'<path d="M{ex} 236 L{x2} {236-90} L{x2} {236+120} Z" fill="#ff2020" opacity=".55" filter="url(#glow)"/>'
                  f'<g transform="translate({ex} 236) scale({side} 1)"><path d="M0 -12 L330 -70 L330 88 L0 12 Z" fill="url(#beam)"/>'
                  f'<path d="M0 -4 L330 -20 L330 30 L0 4 Z" fill="#fff" opacity=".85"/></g>')
    head = (f'<ellipse cx="112" cy="262" rx="26" ry="40" fill="#2c2e52" stroke="{OL}" stroke-width="9"/><ellipse cx="400" cy="262" rx="26" ry="40" fill="#2c2e52" stroke="{OL}" stroke-width="9"/>'
            f'<path d="M256 76 C352 76 404 146 404 246 C404 360 340 440 256 440 C172 440 108 360 108 246 C108 146 160 76 256 76 Z" fill="url(#lzHead)" stroke="{OL}" stroke-width="10"/>'
            # hood-free hair swoop
            f'<path d="M118 210 C120 120 200 64 290 84 C350 98 394 146 400 206 C360 160 300 140 250 150 C200 160 150 186 118 210 Z" fill="#111226" stroke="{OL}" stroke-width="8" stroke-linejoin="round"/>'
            # brows, angry
            f'<path d="M160 200 L236 222" stroke="#0b0b18" stroke-width="16" stroke-linecap="round"/>'
            f'<path d="M352 200 L276 222" stroke="#0b0b18" stroke-width="16" stroke-linecap="round"/>'
            # mouth: set smirk
            f'<path d="M206 356 C236 368 282 366 312 348" stroke="#0b0b18" stroke-width="12" stroke-linecap="round" fill="none"/>'
            f'<path d="M236 286 C244 300 268 300 276 286" stroke="#0b0b18" stroke-width="9" stroke-linecap="round" fill="none"/>'
            '<path d="M140 300 C150 360 190 410 240 428" stroke="#000" stroke-opacity=".25" stroke-width="18" stroke-linecap="round" fill="none"/>'
            '<path d="M150 130 C180 104 214 92 250 90" stroke="#fff" stroke-opacity=".28" stroke-width="14" stroke-linecap="round" fill="none"/>')
    eyes = ''.join(f'<circle cx="{ex}" cy="236" r="46" fill="url(#lzEye)"/><circle cx="{ex}" cy="236" r="14" fill="#fff"/>' for ex in (200, 312))
    return svg(512, 512, d, f'<g filter="url(#drop)">{head}</g>{beams}{eyes}')


# ---------------------------------------------------------------- rug (a patterned rug half rolled, with fringe)
def rug_flat(id_, w=300, h=200):
    """A flat rug in its own frame (0..w, 0..h) with border, field, medallion and corner motifs."""
    return (f'<g><rect x="0" y="0" width="{w}" height="{h}" rx="6" fill="#8e1b2b" stroke="{OL}" stroke-width="9"/>'
            f'<rect x="14" y="14" width="{w-28}" height="{h-28}" rx="3" fill="none" stroke="#f2c14e" stroke-width="7"/>'
            f'<rect x="28" y="28" width="{w-56}" height="{h-56}" fill="#1f2a5c"/>'
            f'<path d="M28 {h/2} L{w/2} 28 L{w-28} {h/2} L{w/2} {h-28} Z" fill="#b8263a"/>'
            f'<path d="M{w*0.3} {h/2} L{w/2} {h*0.26} L{w*0.7} {h/2} L{w/2} {h*0.74} Z" fill="#f2c14e"/>'
            f'<path d="M{w*0.4} {h/2} L{w/2} {h*0.38} L{w*0.6} {h/2} L{w/2} {h*0.62} Z" fill="#1f2a5c"/>'
            f'<circle cx="{w/2}" cy="{h/2}" r="7" fill="#f7e2a0"/>'
            + ''.join(f'<path d="M{cx-14} {cy} L{cx} {cy-14} L{cx+14} {cy} L{cx} {cy+14} Z" fill="#f2c14e"/>' for cx, cy in [(48, 48), (w-48, 48), (48, h-48), (w-48, h-48)])
            + ''.join(f'<rect x="{6+i*((w-12)/23):.1f}" y="18" width="4" height="4" fill="#f7e2a0"/>' for i in range(0))
            + '</g>')


def a_rug():
    d = (lg('roll', [(0, '#c23246', None), (.5, '#8e1b2b', None), (1, '#4e0b16', None)])
         + lg('rugShade', [(0, '#000', 0), (1, '#000', .35)], 0, 0, 1, 0))
    fringe = ''.join(f'<path d="M{-14} {8+i*13} L0 {8+i*13}" stroke="#f3e3b5" stroke-width="5" stroke-linecap="round"/>' for i in range(15))
    flat = rug_flat('r', 300, 200)
    # the flat part in perspective: skew; the roll sits at the right end
    body = (f'<g transform="translate(70 168) skewY(-12) scale(1 1)">'
            f'<g>{fringe}</g>{flat}<rect x="0" y="0" width="300" height="200" fill="url(#rugShade)"/></g>'
            # roll (cylinder) at the right
            f'<g transform="translate(372 104) rotate(-12)">'
            f'<rect x="-34" y="0" width="68" height="214" rx="6" fill="url(#roll)" stroke="{OL}" stroke-width="9"/>'
            f'<ellipse cx="0" cy="0" rx="34" ry="16" fill="#a52235" stroke="{OL}" stroke-width="8"/>'
            f'<path d="M0 0 m-22 0 a22 10 0 1 0 44 0 a16 7 0 1 0 -32 0 a10 4 0 1 0 20 0" fill="none" stroke="#f2c14e" stroke-width="4"/>'
            f'<path d="M-34 60 L34 52 M-34 120 L34 112 M-34 180 L34 172" stroke="#f2c14e" stroke-width="6"/>'
            '<path d="M-20 20 L-20 200" stroke="#fff" stroke-opacity=".35" stroke-width="10" stroke-linecap="round"/>'
            + ''.join(f'<path d="M{-30+i*10} 214 L{-30+i*10} 232" stroke="#f3e3b5" stroke-width="5" stroke-linecap="round"/>' for i in range(7))
            + '</g>')
    # a pull arrow (the rug is being pulled)
    arrow = (f'<path d="M110 120 C150 70 230 54 300 70" stroke="{OL}" stroke-width="26" fill="none" stroke-linecap="round"/>'
             f'<path d="M110 120 C150 70 230 54 300 70" stroke="#ffd23a" stroke-width="12" fill="none" stroke-linecap="round"/>'
             f'<path d="M284 40 L336 74 L284 104 Z" fill="#ffd23a" stroke="{OL}" stroke-width="9" stroke-linejoin="round"/>')
    return svg(512, 512, d, f'<g filter="url(#drop)">{body}</g>{arrow}')


# ---------------------------------------------------------------- wild badge
def a_wild():
    d = (lg('wPurp', [(0, '#b86bff', None), (1, '#5a1fd1', None)])
         + lg('wGold', [(0, '#fff3b0', None), (.45, '#ffd23a', None), (1, '#e08a00', None)])
         + lg('wTxt', [(0, '#fffbe6', None), (.5, '#ffe066', None), (1, '#ffae00', None)]))
    import math
    pts = []
    for i in range(24):
        r = 230 if i % 2 == 0 else 196
        a = -math.pi / 2 + i * math.pi / 12
        pts.append(f'{256 + r*math.cos(a):.1f},{256 + r*math.sin(a):.1f}')
    burst = ' '.join(pts)
    b = (f'<polygon points="{burst}" fill="url(#wGold)" stroke="{OL}" stroke-width="10" stroke-linejoin="round"/>'
         f'<circle cx="256" cy="256" r="172" fill="url(#wPurp)" stroke="{OL}" stroke-width="10"/>'
         '<circle cx="256" cy="256" r="152" fill="none" stroke="#e7cfff" stroke-width="5" stroke-dasharray="2 14" stroke-linecap="round"/>'
         '<path d="M104 236 A152 152 0 0 1 408 236 Q256 190 104 236 Z" fill="#fff" opacity=".22"/>'
         f'<text x="256" y="300" text-anchor="middle" font-family="Luckiest Guy" font-size="128" fill="none" stroke="{OL}" stroke-width="22" stroke-linejoin="round" letter-spacing="4">WILD</text>'
         '<text x="256" y="300" text-anchor="middle" font-family="Luckiest Guy" font-size="128" fill="url(#wTxt)" letter-spacing="4">WILD</text>'
         + ''.join(f'<path d="M{x} {y-r} Q{x} {y} {x+r} {y} Q{x} {y} {x} {y+r} Q{x} {y} {x-r} {y} Q{x} {y} {x} {y-r}Z" fill="#fff"/>' for x, y, r in [(190, 160, 16), (338, 352, 12)]))
    return svg(512, 512, d, f'<g filter="url(#drop)">{b}</g>')


# ---------------------------------------------------------------- coins
def coin(face):
    d = (rg('cGold', [(0, '#fff2a8', None), (.45, '#ffcf3a', None), (.85, '#d78a00', None), (1, '#9c5a00', None)], .4, .35, .75)
         + lg('cRim', [(0, '#ffe680', None), (.5, '#c98000', None), (1, '#7a4500', None)])
         + rg('cIn', [(0, '#ffe27a', None), (.8, '#f0aa14', None), (1, '#c47c00', None)], .45, .4, .7))
    reeds = ''.join(f'<line x1="256" y1="30" x2="256" y2="44" stroke="#8a5200" stroke-width="5" stroke-linecap="round" transform="rotate({i*6} 256 256)"/>' for i in range(60))
    base = (f'<circle cx="256" cy="256" r="232" fill="url(#cRim)" stroke="{OL}" stroke-width="10"/>{reeds}'
            '<circle cx="256" cy="256" r="196" fill="url(#cIn)" stroke="#8a5200" stroke-width="6"/>'
            '<circle cx="256" cy="256" r="176" fill="none" stroke="#fff1b0" stroke-width="4" stroke-opacity=".7"/>'
            + ''.join(f'<circle cx="256" cy="94" r="5" fill="#a86400" transform="rotate({i*30} 256 256)"/>' for i in range(12)))
    emb_dark, emb_lite = '#a35f00', '#fff3b8'
    if face == 'heads':   # a bull head, front view
        e = ('M256 356 C222 356 204 330 200 300 C176 292 160 270 158 246 C140 244 118 232 104 208 C130 214 150 210 168 200 '
             'C182 184 206 176 226 176 L286 176 C306 176 330 184 344 200 C362 210 382 214 408 208 C394 232 372 244 354 246 '
             'C352 270 336 292 312 300 C308 330 290 356 256 356 Z')
        horns = ('M190 186 C160 170 140 140 142 104 C160 132 186 146 214 154 Z M322 186 C352 170 372 140 370 104 C352 132 326 146 298 154 Z')
        detail = (f'<ellipse cx="226" cy="246" rx="10" ry="12" fill="{emb_dark}"/><ellipse cx="286" cy="246" rx="10" ry="12" fill="{emb_dark}"/>'
                  f'<path d="M230 316 C242 330 270 330 282 316" stroke="{emb_dark}" stroke-width="8" fill="none" stroke-linecap="round"/>'
                  f'<ellipse cx="238" cy="306" rx="8" ry="6" fill="{emb_dark}"/><ellipse cx="274" cy="306" rx="8" ry="6" fill="{emb_dark}"/>')
        emb = (f'<g transform="translate(4 6)" opacity=".55"><path d="{horns}" fill="{emb_dark}"/><path d="{e}" fill="{emb_dark}"/></g>'
               f'<path d="{horns}" fill="url(#cGold)" stroke="{emb_dark}" stroke-width="6" stroke-linejoin="round"/>'
               f'<path d="{e}" fill="url(#cGold)" stroke="{emb_dark}" stroke-width="6" stroke-linejoin="round"/>'
               f'<path d="M170 204 C190 188 212 182 232 182" stroke="{emb_lite}" stroke-width="6" fill="none" stroke-linecap="round"/>{detail}')
        label = 'BULL'
    else:                 # a bear head, front view
        e = ('M256 362 C196 362 154 322 154 266 C154 214 196 178 256 178 C316 178 358 214 358 266 C358 322 316 362 256 362 Z')
        ears = 'M182 214 C158 204 150 172 168 154 C186 138 214 146 222 170 Z M330 214 C354 204 362 172 344 154 C326 138 298 146 290 170 Z'
        detail = (f'<ellipse cx="256" cy="304" rx="46" ry="36" fill="{emb_lite}" opacity=".55"/>'
                  f'<path d="M240 292 C246 284 266 284 272 292 C272 302 262 306 256 306 C250 306 240 302 240 292 Z" fill="{emb_dark}"/>'
                  f'<path d="M256 306 L256 318 M238 324 C248 332 264 332 274 324" stroke="{emb_dark}" stroke-width="7" fill="none" stroke-linecap="round"/>'
                  f'<path d="M206 244 L232 256 M306 244 L280 256" stroke="{emb_dark}" stroke-width="9" stroke-linecap="round"/>'
                  f'<ellipse cx="222" cy="262" rx="8" ry="9" fill="{emb_dark}"/><ellipse cx="290" cy="262" rx="8" ry="9" fill="{emb_dark}"/>')
        emb = (f'<g transform="translate(4 6)" opacity=".55"><path d="{ears}" fill="{emb_dark}"/><path d="{e}" fill="{emb_dark}"/></g>'
               f'<path d="{ears}" fill="url(#cGold)" stroke="{emb_dark}" stroke-width="6" stroke-linejoin="round"/>'
               f'<path d="{e}" fill="url(#cGold)" stroke="{emb_dark}" stroke-width="6" stroke-linejoin="round"/>'
               f'<path d="M176 240 C186 210 214 192 244 188" stroke="{emb_lite}" stroke-width="6" fill="none" stroke-linecap="round"/>{detail}')
        label = 'BEAR'
    txt = (f'<path id="arc" d="M120 300 A140 140 0 0 0 392 300" fill="none"/>'
           f'<text font-family="Righteous" font-size="30" letter-spacing="10" fill="{emb_dark}" text-anchor="middle"><textPath href="#arc" startOffset="50%">{label}</textPath></text>')
    shine = '<path d="M90 190 A180 180 0 0 1 300 70 L290 100 A150 150 0 0 0 116 204 Z" fill="#fff" opacity=".35"/>'
    return svg(512, 512, d, f'<g filter="url(#drop)">{base}<g transform="translate(0 -14)">{emb}</g>{txt}{shine}</g>')


def a_crash_rocket():
    d, b = rocket_parts()
    return svg(256, 256, d, f'<g filter="url(#drop)"><g transform="translate(128 128) rotate(45) scale(.45) translate(-256 -260)">{b}</g></g>')


def a_crash_rug():
    d = (lg('roll', [(0, '#c23246', None), (.5, '#8e1b2b', None), (1, '#4e0b16', None)]))
    # a rug whipping up and away, bent in a wave, with speed lines
    flat = rug_flat('c', 300, 200)
    lines = ''.join(f'<path d="M{40+i*8} {120+i*70} L{150+i*8} {100+i*70}" stroke="#ffffff" stroke-opacity=".7" stroke-width="{10-i*2}" stroke-linecap="round"/>' for i in range(4))
    fr = ''.join(f'<path d="M{8+i*20.3:.1f} 200 L{8+i*20.3:.1f} 222" stroke="#f3e3b5" stroke-width="6" stroke-linecap="round"/>' for i in range(15))
    fl = ''.join(f'<path d="M{8+i*20.3:.1f} 0 L{8+i*20.3:.1f} -22" stroke="#f3e3b5" stroke-width="6" stroke-linecap="round"/>' for i in range(15))
    body = f'<g transform="translate(150 300) rotate(-24) skewX(-20)">{fr}{fl}{flat}</g>'
    return svg(512, 512, d, f'{lines}<g filter="url(#drop)">{body}</g>')


def a_logo():
    W, H = 1024, 384
    d = lg('plate', [(0, '#2a0f3a', .0), (1, '#2a0f3a', .0)])
    def neon(txt, x, y, size, font, col, ls=0, sw=7):
        common = f'x="{x}" y="{y}" text-anchor="middle" font-family="{font}" font-size="{size}" letter-spacing="{ls}"'
        return (f'<text {common} fill="none" stroke="{col}" stroke-width="{sw*4}" opacity=".55" filter="url(#glow)">{txt}</text>'
                f'<text {common} fill="none" stroke="{col}" stroke-width="{sw*1.8}" opacity=".9" filter="url(#glow2)">{txt}</text>'
                f'<text {common} fill="none" stroke="{col}" stroke-width="{sw}" stroke-linejoin="round">{txt}</text>'
                f'<text {common} fill="none" stroke="#fff" stroke-width="{sw*0.38}" stroke-linejoin="round" opacity=".95">{txt}</text>')
    # a thin neon frame with a diamond at each end
    frame = (f'<rect x="24" y="24" width="{W-48}" height="{H-48}" rx="40" fill="none" stroke="#ffcf4a" stroke-width="14" opacity=".45" filter="url(#glow)"/>'
             f'<rect x="24" y="24" width="{W-48}" height="{H-48}" rx="40" fill="none" stroke="#ffcf4a" stroke-width="5"/>'
             f'<rect x="24" y="24" width="{W-48}" height="{H-48}" rx="40" fill="none" stroke="#fff6d6" stroke-width="1.8"/>')
    body = (frame
            + neon(CASINO, 512, 250, 190 if len(CASINO) <= 7 else int(1330 / len(CASINO)), 'Righteous', '#ff3fa4', 10, 8))
    return svg(W, H, d, body)


ASSETS = {
    'rocket': (a_rocket, 512, 512), 'diamond_hands': (a_diamond_hands, 512, 512), 'ape': (a_ape, 512, 512),
    'laser_eyes': (a_laser_eyes, 512, 512), 'rug': (a_rug, 512, 512), 'wild': (a_wild, 512, 512),
    'coin_heads': (lambda: coin('heads'), 512, 512), 'coin_tails': (lambda: coin('tails'), 512, 512),
    'crash_rocket': (a_crash_rocket, 256, 256), 'crash_rug': (a_crash_rug, 512, 512), 'rugroom_logo': (a_logo, 1024, 384),
}



# ================================================================ slot cabinet panels (casino_slot_retexture.py pastes them
# into the cs_slot_machine atlas). Canvas sizes carry the panel's real aspect on the cabinet (the atlas tiles are square and
# get stretched); the retexture script resizes each page to its atlas box and flips it to the atlas's top-down storage.
SYM = lambda k: f'../png/{k}.png'
THEMES = {'a': dict(bg1='#3a0d4a', bg2='#12041c', ray='#ff3fa4', rim='#ffcf4a', neon='#ff3fa4', acc='#ffcf4a'),
          'b': dict(bg1='#08343c', bg2='#02121a', ray='#29e3ff', rim='#ffcf4a', neon='#29e3ff', acc='#ffcf4a')}


def _neon_text(txt, x, y, size, font, col, ls=0, sw=4, anchor='middle'):
    c = f'x="{x}" y="{y}" text-anchor="{anchor}" font-family="{font}" font-size="{size}" letter-spacing="{ls}"'
    return (f'<text {c} fill="none" stroke="{col}" stroke-width="{sw*4}" opacity=".55" filter="url(#glow)">{txt}</text>'
            f'<text {c} fill="none" stroke="{col}" stroke-width="{sw*1.6}" opacity=".9" filter="url(#glow2)">{txt}</text>'
            f'<text {c} fill="{col}" fill-opacity=".18" stroke="{col}" stroke-width="{sw}" stroke-linejoin="round">{txt}</text>'
            f'<text {c} fill="none" stroke="#fff" stroke-width="{sw*0.4}" stroke-linejoin="round">{txt}</text>')


def _bg(t, W, H, rays=True):
    import math
    r = ''
    if rays:
        for i in range(36):
            a0, a1 = i * math.pi / 18, (i + 0.5) * math.pi / 18
            R = max(W, H) * 1.2
            r += f'<path d="M{W/2} {H*0.42} L{W/2 + R*math.cos(a0):.0f} {H*0.42 + R*math.sin(a0):.0f} L{W/2 + R*math.cos(a1):.0f} {H*0.42 + R*math.sin(a1):.0f}Z" fill="{t["ray"]}" opacity=".07"/>'
    return (rg('bgr', [(0, t['bg1'], None), (1, t['bg2'], None)], .5, .4, .8)
            , f'<rect width="{W}" height="{H}" fill="url(#bgr)"/>{r}')


def slot_top(th):
    t = THEMES[th]; W, H = 665, 512
    d, bg = _bg(t, W, H)
    rows = [('rocket', '30'), ('wild', '25'), ('laser_eyes', '15'), ('diamond_hands', '10'), ('ape', '5')]
    pt = ''
    y0 = 300
    for i, (k, m) in enumerate(rows):
        col = i % 2; row = i // 2
        if i == 4: col, row = 0.5, 2
        x = 40 + col * 300; y = y0 + row * 62
        pt += ''.join(f'<image href="{SYM(k)}" x="{x + j*44}" y="{y}" width="52" height="52"/>' for j in range(3))
        pt += f'<text x="{x + 150}" y="{y + 40}" font-family="Luckiest Guy" font-size="40" fill="{t["acc"]}" stroke="{OL}" stroke-width="2">{m}x</text>'
    body = (bg
            + f'<rect x="10" y="10" width="{W-20}" height="{H-20}" rx="18" fill="none" stroke="{t["rim"]}" stroke-width="6"/>'
            + f'<image href="../png/rugroom_logo.png" x="{(W-560)/2}" y="22" width="560" height="210"/>'
            + f'<rect x="28" y="246" width="{W-56}" height="{H-266}" rx="14" fill="#000" fill-opacity=".45" stroke="{t["rim"]}" stroke-opacity=".6" stroke-width="3"/>'
            + f'<text x="{W/2}" y="282" text-anchor="middle" font-family="Righteous" font-size="26" letter-spacing="6" fill="#fff">THREE OF A KIND PAYS</text>'
            + pt)
    return svg(W, H, d, body)


def slot_glass(th):
    """The reel glass: 650 x 390 mm on the cabinet; atlas tile 512 -> canvas 850 x 512 (x * 850/512). The three reel
    windows must stay where the atlas alpha has them (stored x 84-183, 201-309, 327-430; y 136-430 -> model y 82-376)."""
    t = THEMES[th]; W, H = 850, 512; k = W / 512
    d, bg = _bg(t, W, H, rays=False)
    wins = [(84, 183), (201, 309), (327, 430)]
    frames = ''
    for x0, x1 in wins:
        frames += (f'<rect x="{x0*k-10:.0f}" y="72" width="{(x1-x0)*k+20:.0f}" height="314" rx="10" fill="{t["rim"]}"/>'
                   f'<rect x="{x0*k-4:.0f}" y="78" width="{(x1-x0)*k+8:.0f}" height="302" rx="6" fill="#140818"/>'
                   f'<rect x="{x0*k:.0f}" y="82" width="{(x1-x0)*k:.0f}" height="294" fill="#dfe7f2"/>')
    pay = (f'<rect x="{84*k-40:.0f}" y="223" width="{430*k-84*k+80:.0f}" height="6" fill="{t["neon"]}" opacity=".9"/>'
           f'<rect x="{84*k-40:.0f}" y="223" width="{430*k-84*k+80:.0f}" height="6" fill="{t["neon"]}" filter="url(#glow2)"/>')
    def lcd(x, w, label, val):
        return (f'<rect x="{x}" y="398" width="{w}" height="56" rx="8" fill="#07040a" stroke="{t["rim"]}" stroke-width="3"/>'
                f'<text x="{x + w/2}" y="438" text-anchor="middle" font-family="Righteous" font-size="34" fill="{t["neon"]}">{val}</text>'
                f'<text x="{x + w/2}" y="484" text-anchor="middle" font-family="Righteous" font-size="22" letter-spacing="3" fill="#fff">{label}</text>')
    top = ''   # the cabinet's bezel covers the strip above the reels
    body = (bg + f'<rect x="8" y="8" width="{W-16}" height="{H-16}" rx="16" fill="none" stroke="{t["rim"]}" stroke-width="5"/>'
            + top + frames + pay + lcd(80, 200, 'CREDITS', '1,000') + lcd(325, 200, 'BET', '100') + lcd(570, 200, 'WIN', '0'))
    return svg(W, H, d, body)


def slot_belly(th):
    """The belly glass under the buttons: 670 x 160 mm; canvas 2144 x 512."""
    t = THEMES[th]; W, H = 2144, 512
    d, bg = _bg(t, W, H, rays=False)
    sy = ''.join(f'<image href="{SYM(k)}" x="{x}" y="96" width="320" height="320"/>' for k, x in [('rocket', 70), ('diamond_hands', 400), ('ape', 1424), ('laser_eyes', 1754)])
    body = (bg + f'<rect x="14" y="14" width="{W-28}" height="{H-28}" rx="30" fill="none" stroke="{t["rim"]}" stroke-width="10"/>'
            + sy + f'<image href="../png/rugroom_logo.png" x="{W/2-400}" y="106" width="800" height="300"/>')
    return svg(W, H, d, body)


STRIPS = [['rocket', 'ape', 'wild', 'diamond_hands', 'rug'], ['laser_eyes', 'rug', 'rocket', 'ape', 'diamond_hands'],
          ['diamond_hands', 'wild', 'ape', 'rug', 'laser_eyes']]


def slot_strip():
    """Three reel strips side by side (atlas 410 x 512, five cells each); canvas 595 x 512 (cells read 1.45x wider)."""
    W, H = 595, 512; cw = W / 3; ch = H / 5
    d = lg('reelShade', [(0, '#000', .28), (.18, '#000', 0), (.82, '#000', 0), (1, '#000', .28)], 0, 0, 1, 0)
    b = f'<rect width="{W}" height="{H}" fill="#f4f1ea"/>'
    for c, col in enumerate(STRIPS):
        for r, k in enumerate(col):
            s = min(cw, ch) * 0.86
            b += f'<image href="{SYM(k)}" x="{c*cw + (cw - s)/2:.1f}" y="{r*ch + (ch - s)/2:.1f}" width="{s:.1f}" height="{s:.1f}"/>'
        b += f'<rect x="{c*cw:.1f}" y="0" width="{cw:.1f}" height="{H}" fill="url(#reelShade)"/>'
    return svg(W, H, d, b)


def slot_buttons():
    """The button caps (atlas column 91 px wide, rows 1542-1634, 1639-1712, 1714-1789, 1792-1864, 1867-1939), at 2x."""
    W, H = 182, 794
    rows = [(0, 184, 'INFO', '#2b2f3a', '#0e1016', '#9fd8ff'), (194, 340, 'SPIN', '#ff3fa4', '#a3105c', '#fff'),
            (344, 494, 'MAX', '#ffcf4a', '#c48a00', '#1a0f2c'), (500, 644, 'BET', '#ffcf4a', '#c48a00', '#1a0f2c'),
            (650, 794, 'AUTO', '#29e3ff', '#0a7c96', '#04202a')]
    d = ''; b = ''
    for i, (y0, y1, txt, c1, c2, ink) in enumerate(rows):
        d += lg(f'bt{i}', [(0, c1, None), (1, c2, None)])
        b += (f'<rect x="4" y="{y0+4}" width="{W-8}" height="{y1-y0-8}" rx="22" fill="url(#bt{i})"/>'
              f'<rect x="14" y="{y0+12}" width="{W-28}" height="{(y1-y0)*0.38:.0f}" rx="16" fill="#fff" opacity=".22"/>'
              f'<text x="{W/2}" y="{(y0+y1)/2 + 16}" text-anchor="middle" font-family="Righteous" font-size="38" fill="{ink}">{txt}</text>')
    return svg(W, H, d, b)



def sign_board():
    """The Rug Room's facade board (IslandV2Shells sign, 4:1, lit by the sign's emission). The top 8 px rows are the pink
    neon tube colour and the bottom 8 px the gold one: the facade's neon quads sample them."""
    W, H = 1024, 256
    d = rg('sbg', [(0, '#3a0f48', None), (1, '#14051c', None)], .5, .45, .8)
    bulbs = ''.join(f'<circle cx="{x}" cy="{y}" r="5" fill="#fff3c4"/><circle cx="{x}" cy="{y}" r="9" fill="#ffcf4a" opacity=".45" filter="url(#glow2)"/>'
                    for x in range(40, W - 20, 32) for y in (26, H - 26))
    body = (f'<rect width="{W}" height="{H}" fill="url(#sbg)"/>'
            f'<rect x="12" y="12" width="{W-24}" height="{H-24}" rx="10" fill="none" stroke="#c9a24e" stroke-width="5"/>'
            + bulbs
            + _neon_text(CASINO, 512, 172, 132 if len(CASINO) <= 7 else int(920 / len(CASINO)), 'Righteous', '#ff3fa4', 14, 6)
            + f'<rect x="0" y="0" width="{W}" height="8" fill="#ff6fbc"/><rect x="0" y="{H-8}" width="{W}" height="8" fill="#ffd36a"/>')
    return svg(W, H, d, body)


# ================================================================ the casino floor (casino_room.py): carpet and the signs atlas
def carpet():
    """A seamless 1024 tile (2 m on the floor): an Art-Deco scallop of fans in burgundy and gold on deep navy."""
    W = 1024; c = 128          # 8 x 8 fans
    fan = ''
    for i in range(9):
        for j in range(19):
            x = i * c + (c / 2 if j % 2 else 0); y = j * c * 0.5
            fan += (f'<g transform="translate({x} {y})">'
                    f'<path d="M{-c/2} {c/2} A{c/2} {c/2} 0 0 1 {c/2} {c/2} Z" fill="#4a1022" stroke="#b8933f" stroke-width="3"/>'
                    f'<path d="M{-c*0.34} {c/2} A{c*0.34} {c*0.34} 0 0 1 {c*0.34} {c/2}" fill="none" stroke="#b8933f" stroke-width="2"/>'
                    f'<path d="M{-c*0.18} {c/2} A{c*0.18} {c*0.18} 0 0 1 {c*0.18} {c/2} Z" fill="#141a33" stroke="#b8933f" stroke-width="2"/>'
                    + ''.join(f'<line x1="0" y1="{c/2}" x2="{c/2*math.cos(math.radians(a)):.1f}" y2="{c/2 - c/2*math.sin(math.radians(a)):.1f}" stroke="#7a1f36" stroke-width="2"/>' for a in (30, 60, 90, 120, 150))
                    + '</g>')
    body = f'<rect width="{W}" height="{W}" fill="#141a33"/><g transform="translate(0 {-c/2})">{fan}</g>'
    return svg(W, W, '', body)


def rr_signs():
    """2048 x 1024 atlas for the floor's signs (casino_room.py maps its quads onto these boxes):
    logo board (0, 0, 1024, 384); crash screen (1024, 0, 2048, 576); plaque (0, 384, 1024, 640);
    SLOTS (0, 640, 512, 896); COINFLIP (512, 640, 1024, 896); CRASH header (1024, 576, 2048, 832)."""
    W, H = 2048, 1024
    d = (rg('lb', [(0, '#2c0b38', None), (1, '#0e0414', None)], .5, .5, .8)
         + lg('brass', [(0, '#f2d48a', None), (.5, '#c9a24e', None), (1, '#8a6a26', None)])
         + lg('scr', [(0, '#0b1230', None), (1, '#04060f', None)])
         + lg('line', [(0, '#29e3ff', None), (1, '#ff3fa4', None)], 0, 0, 1, 0)
         + lg('fill', [(0, '#ff3fa4', .35), (1, '#ff3fa4', 0)]))
    b = ''
    # logo board
    b += (f'<rect x="0" y="0" width="1024" height="384" fill="url(#lb)"/><rect x="10" y="10" width="1004" height="364" rx="16" fill="none" stroke="url(#brass)" stroke-width="10"/>'
          f'<image href="../png/rugroom_logo.png" x="40" y="12" width="944" height="354"/>')
    # crash screen
    grid = ''.join(f'<line x1="{1024 + 60 + k*90}" y1="40" x2="{1024 + 60 + k*90}" y2="536" stroke="#1d2a55" stroke-width="2"/>' for k in range(11))
    grid += ''.join(f'<line x1="1084" y1="{60 + k*80}" x2="2008" y2="{60 + k*80}" stroke="#1d2a55" stroke-width="2"/>' for k in range(7))
    pts = []
    for k in range(60):
        t = k / 59
        pts.append((1084 + t * 760, 520 - (math.exp(2.6 * t) - 1) / (math.exp(2.6) - 1) * 400))
    path = 'M' + ' L'.join(f'{x:.1f} {y:.1f}' for x, y in pts)
    area = path + f' L{pts[-1][0]:.1f} 520 L1084 520 Z'
    b += (f'<rect x="1024" y="0" width="1024" height="576" fill="url(#scr)"/>{grid}'
          f'<path d="{area}" fill="url(#fill)"/><path d="{path}" fill="none" stroke="url(#line)" stroke-width="10" stroke-linecap="round"/>'
          f'<image href="../png/crash_rocket.png" x="{pts[-1][0]-70:.0f}" y="{pts[-1][1]-110:.0f}" width="150" height="150"/>'
          f'<text x="1300" y="200" text-anchor="middle" font-family="Luckiest Guy" font-size="130" fill="#ffffff" stroke="#ff3fa4" stroke-width="4">2.48x</text>'
          f'<text x="1300" y="270" text-anchor="middle" font-family="Righteous" font-size="38" letter-spacing="6" fill="#29e3ff">CASH OUT IN TIME</text>')
    # plaque
    b += (f'<rect x="0" y="384" width="1024" height="256" fill="url(#brass)"/><rect x="14" y="398" width="996" height="228" rx="10" fill="none" stroke="#6b4f18" stroke-width="5"/>'
          f'<text x="512" y="478" text-anchor="middle" font-family="Righteous" font-size="46" letter-spacing="8" fill="#3b2a08">{CASINO}</text>'
          f'<text x="512" y="552" text-anchor="middle" font-family="Righteous" font-size="52" fill="#2a1d05">Play money. No cash value. 18+</text>'
          f'<text x="512" y="602" text-anchor="middle" font-family="Righteous" font-size="26" fill="#4a3810">Cash is game money and can never be withdrawn.</text>')
    # SLOTS / COINFLIP neon signs
    for x0, word, col in ((0, U('game.slots'), '#ff3fa4'), (512, U('game.coinflip'), '#ffcf4a')):
        b += (f'<rect x="{x0}" y="640" width="512" height="256" fill="#0e0414"/><rect x="{x0+8}" y="648" width="496" height="240" rx="14" fill="none" stroke="url(#brass)" stroke-width="6"/>'
              + _neon_text(word, x0 + 256, 800, 96 if len(word) < 6 else 78, 'Righteous', col, 4, 5))
    # Crash header
    b += (f'<rect x="1024" y="576" width="1024" height="256" fill="#0e0414"/><rect x="1032" y="584" width="1008" height="240" rx="14" fill="none" stroke="url(#brass)" stroke-width="6"/>'
          + _neon_text(U('game.crash'), 1536, 745, 120, 'Righteous', '#29e3ff', 12, 5))
    b += f'<rect x="0" y="896" width="2048" height="128" fill="#0e0414"/><rect x="1024" y="832" width="1024" height="64" fill="#0e0414"/>'
    return svg(W, H, d, b)

ASSETS.update({
    'carpet': (carpet, 1024, 1024), 'rr_signs': (rr_signs, 2048, 1024),
    'sign_rug_room': (sign_board, 1024, 256),
    'slot_top_a': (lambda: slot_top('a'), 665, 512), 'slot_top_b': (lambda: slot_top('b'), 665, 512),
    'slot_glass_a': (lambda: slot_glass('a'), 850, 512), 'slot_glass_b': (lambda: slot_glass('b'), 850, 512),
    'slot_belly_a': (lambda: slot_belly('a'), 2144, 512), 'slot_belly_b': (lambda: slot_belly('b'), 2144, 512),
    'slot_strip': (slot_strip, 595, 512), 'slot_buttons': (slot_buttons, 182, 794),
})


if __name__ == '__main__':
    import sys
    only = sys.argv[1:] or list(ASSETS)
    sizes = {}
    for k in only:
        f, w, h = ASSETS[k]
        open(os.path.join(OUT, k + '.html'), 'w').write(page(f(), w, h))
        sizes[k] = [w, h]
    json.dump({k: ASSETS[k][1:] for k in ASSETS}, open(os.path.join(HERE, 'out', 'sizes.json'), 'w'))
    print(len(only), 'pages')
