#!/usr/bin/env python3
"""Rekt Burger art studio (2026-10-25): original vector art for the parody fast-food job, rendered with headless Chrome.

Our own brand: the name, the mark (a burger cracked by a falling chart) and the palette (charcoal, burnt orange, mint,
cream) are original. No real chain's name, logo, mascot, colours or slogans (no arches, no red-and-yellow trade dress).
Lettering: Google Fonts under the SIL OFL (Bowlby One, Archivo Black, Inter).

Assets (id: size):
  logo                1024x384  wordmark + mark (UI)
  ingredients         bun_top, bun_bottom, patty, cheese, lettuce, tomato, onion, pickle   256x256
  items               fries, drink, burger_done   256x256
  ticket_bg           384x576   receipt paper (9-slice: 40 px borders)
  tray                512x320
  stickers            sticker_perfect, sticker_wrong   192x192
  sign_rekt_burger    1024x256  the storefront's lit sign board
  rb_signs            2048x1024 the restaurant's signs atlas (menu boards, order screen, logo wall)
"""
import json, math, os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out', 'html')
os.makedirs(OUT, exist_ok=True)

INK = '#17130f'
COAL, COAL2 = '#1b1d22', '#2b2e36'
ORANGE, ORANGE_D = '#ff6a2b', '#d4431a'
MINT, MINT_D = '#38e0a2', '#12a673'
CREAM, CREAM_D = '#fff3df', '#e9d3ad'
FONTS = '<link href="https://fonts.googleapis.com/css2?family=Bowlby+One&family=Archivo+Black&family=Inter:wght@600;800&display=block" rel="stylesheet">'


def lg(id_, stops, x1=0, y1=0, x2=0, y2=1):
    s = ''.join(f'<stop offset="{o}" stop-color="{c}"{"" if a is None else f" stop-opacity=\"{a}\""}/>' for o, c, a in stops)
    return f'<linearGradient id="{id_}" x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}">{s}</linearGradient>'


def rg(id_, stops, cx=.5, cy=.5, r=.5):
    s = ''.join(f'<stop offset="{o}" stop-color="{c}"{"" if a is None else f" stop-opacity=\"{a}\""}/>' for o, c, a in stops)
    return f'<radialGradient id="{id_}" cx="{cx}" cy="{cy}" r="{r}">{s}</radialGradient>'


COMMON = ('<filter id="drop" x="-20%" y="-20%" width="140%" height="160%"><feDropShadow dx="0" dy="6" stdDeviation="6" flood-color="#000" flood-opacity=".32"/></filter>'
          '<filter id="glow" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="8"/></filter>'
          + lg('spec', [(0, '#fff', .7), (.6, '#fff', 0)]))


def page(svg):
    return (f'<!doctype html><html><head><meta charset="utf-8">{FONTS}<style>html,body{{margin:0;background:transparent;overflow:hidden}}'
            f'svg{{display:block}}</style></head><body>{svg}</body></html>')


def svg(w, h, defs, body):
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}"><defs>{COMMON}{defs}</defs>{body}</svg>'


# ---------------------------------------------------------------- ingredient pieces (drawn in a 256 box, centred)
SW = 7   # outline width


def bun_top_g(cx=128, cy=150, w=200, h=92, seeds=True):
    d = lg('bunT', [(0, '#ffc77a', None), (.55, '#f0973b', None), (1, '#c46a1f', None)])
    x0, x1 = cx - w / 2, cx + w / 2
    path = f'M{x0} {cy} C{x0} {cy - h*1.15} {x1} {cy - h*1.15} {x1} {cy} C{x1} {cy + 14} {x0} {cy + 14} {x0} {cy} Z'
    b = f'<path d="{path}" fill="url(#bunT)" stroke="{INK}" stroke-width="{SW}" stroke-linejoin="round"/>'
    b += f'<path d="M{x0 + w*0.18} {cy - h*0.55} C{x0 + w*0.3} {cy - h*0.86} {x0 + w*0.5} {cy - h*0.92} {x0 + w*0.62} {cy - h*0.88}" fill="none" stroke="#fff" stroke-opacity=".55" stroke-width="10" stroke-linecap="round"/>'
    if seeds:
        for sx, sy, r in ((-52, -52, 20), (-18, -70, -15), (20, -66, 25), (54, -44, -30), (-34, -26, 10), (10, -34, -8), (46, -18, 18), (-66, -14, -22)):
            b += f'<ellipse cx="{cx + sx*w/200}" cy="{cy + sy*h/92}" rx="7" ry="4" fill="{CREAM}" stroke="{INK}" stroke-width="2.5" transform="rotate({r} {cx + sx*w/200} {cy + sy*h/92})"/>'
    return d, b


def bun_bottom_g(cx=128, cy=128, w=200, h=46):
    d = lg('bunB', [(0, '#f6a85a', None), (1, '#c56c22', None)])
    x0, x1 = cx - w / 2, cx + w / 2
    path = f'M{x0} {cy - h/2} L{x1} {cy - h/2} C{x1} {cy + h*0.5} {x1 - 18} {cy + h*0.6} {cx} {cy + h*0.6} C{x0 + 18} {cy + h*0.6} {x0} {cy + h*0.5} {x0} {cy - h/2} Z'
    b = f'<path d="{path}" fill="url(#bunB)" stroke="{INK}" stroke-width="{SW}" stroke-linejoin="round"/>'
    b += f'<rect x="{x0 + 6}" y="{cy - h/2 + 3}" width="{w - 12}" height="9" rx="4" fill="{CREAM_D}"/>'
    return d, b


def patty_g(cx=128, cy=128, w=206, h=50):
    d = lg('pat', [(0, '#7a4428', None), (.5, '#5a2f1b', None), (1, '#3c1e10', None)])
    x0 = cx - w / 2
    b = f'<rect x="{x0}" y="{cy - h/2}" width="{w}" height="{h}" rx="{h/2}" fill="url(#pat)" stroke="{INK}" stroke-width="{SW}"/>'
    for k in range(5):
        x = x0 + 30 + k * 38
        b += f'<path d="M{x} {cy - h/2 + 10} L{x + 16} {cy + h/2 - 10}" stroke="#2a130a" stroke-width="7" stroke-linecap="round"/>'
    b += f'<path d="M{x0 + 22} {cy - h/2 + 9} L{x0 + w - 40} {cy - h/2 + 9}" stroke="#fff" stroke-opacity=".22" stroke-width="6" stroke-linecap="round"/>'
    return d, b


def cheese_g(cx=128, cy=128, w=200):
    d = lg('chz', [(0, '#ffd75c', None), (1, '#f5a623', None)])
    x0, x1 = cx - w / 2, cx + w / 2
    y = cy - 22
    path = (f'M{x0} {y} L{x1} {y} L{x1} {y + 30} L{x1 - 26} {y + 30} C{x1 - 34} {y + 70} {x1 - 60} {y + 70} {x1 - 66} {y + 30} '
            f'L{cx + 6} {y + 30} C{cx - 2} {y + 56} {cx - 26} {y + 56} {cx - 32} {y + 30} L{x0 + 40} {y + 30} C{x0 + 34} {y + 48} {x0 + 14} {y + 48} {x0 + 10} {y + 30} L{x0} {y + 30} Z')
    b = f'<path d="{path}" fill="url(#chz)" stroke="{INK}" stroke-width="{SW}" stroke-linejoin="round"/>'
    for hx, hy in ((-60, 6), (30, 4), (-10, 10)):
        b += f'<circle cx="{cx + hx}" cy="{y + hy}" r="4" fill="#e39014"/>'
    return d, b


def lettuce_g(cx=128, cy=128, w=214):
    d = lg('let', [(0, '#7cf0a8', None), (1, MINT_D, None)])
    x0 = cx - w / 2
    pts = []
    n = 9
    for k in range(n + 1):
        x = x0 + w * k / n
        pts.append(f'{x:.1f} {cy + (26 if k % 2 else 6)}')
    path = f'M{x0} {cy - 22} C{x0 + 40} {cy - 40} {x0 + w - 40} {cy - 40} {x0 + w} {cy - 22} L' + ' L'.join(pts[::-1]) + ' Z'
    b = f'<path d="{path}" fill="url(#let)" stroke="{INK}" stroke-width="{SW}" stroke-linejoin="round"/>'
    b += f'<path d="M{x0 + 20} {cy - 12} C{cx - 30} {cy - 2} {cx + 30} {cy - 2} {x0 + w - 20} {cy - 12}" fill="none" stroke="#d9ffe9" stroke-opacity=".7" stroke-width="4" stroke-linecap="round"/>'
    return d, b


def tomato_g(cx=128, cy=128, r=74):
    d = rg('tom', [(0, '#ff8a6a', None), (1, '#e0311c', None)])
    b = f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="url(#tom)" stroke="{INK}" stroke-width="{SW}"/>'
    b += f'<circle cx="{cx}" cy="{cy}" r="{r*0.68}" fill="#ff5a3c" stroke="#b81f10" stroke-width="3"/>'
    for k in range(6):
        a = k * math.pi / 3
        b += (f'<ellipse cx="{cx + math.cos(a)*r*0.38:.1f}" cy="{cy + math.sin(a)*r*0.38:.1f}" rx="{r*0.16:.1f}" ry="{r*0.25:.1f}" fill="#ffd2c2" '
              f'transform="rotate({math.degrees(a) + 90:.0f} {cx + math.cos(a)*r*0.38:.1f} {cy + math.sin(a)*r*0.38:.1f})"/>')
    b += f'<circle cx="{cx}" cy="{cy}" r="{r*0.12}" fill="#ffb59c"/>'
    return d, b


def onion_g(cx=128, cy=128):
    b = ''
    for k, r in enumerate((78, 58, 38)):
        b += f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="none" stroke="{INK}" stroke-width="{SW + 10}"/>'
        b += f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="none" stroke="#f3e2ff" stroke-width="{SW + 2}"/>'
        b += f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="none" stroke="#c8a3e6" stroke-width="3" stroke-dasharray="40 20"/>'
    return '', b


def pickle_g(cx=128, cy=128):
    d = rg('pk', [(0, '#c6e86a', None), (1, '#5f9a2a', None)])
    b = ''
    for dx, dy, r in ((-42, -18, 46), (40, 20, 46)):
        x, y = cx + dx, cy + dy
        b += f'<circle cx="{x}" cy="{y}" r="{r}" fill="url(#pk)" stroke="{INK}" stroke-width="{SW}"/>'
        b += f'<circle cx="{x}" cy="{y}" r="{r*0.7}" fill="none" stroke="#e8f7b0" stroke-width="3" stroke-dasharray="6 7"/>'
        for k in range(5):
            a = k * 2 * math.pi / 5
            b += f'<circle cx="{x + math.cos(a)*r*0.35:.1f}" cy="{y + math.sin(a)*r*0.35:.1f}" r="5" fill="#f4ffcf"/>'
    return d, b


def icon(fn, *a, **k):
    d, b = fn(*a, **k)
    return svg(256, 256, d, f'<g filter="url(#drop)">{b}</g>')


def burger_done():
    parts = [bun_bottom_g(cy=196, w=196), patty_g(cy=164, w=204, h=44), cheese_g(cy=146, w=196), lettuce_g(cy=128, w=210),
             tomato_slice(), bun_top_g(cy=104, w=200, h=80)]
    d = ''.join(p[0] for p in parts)
    b = ''.join(p[1] for p in parts)
    return svg(256, 256, d, f'<g filter="url(#drop)">{b}</g>')


def tomato_slice():
    return '', f'<rect x="44" y="104" width="168" height="18" rx="9" fill="#ff5a3c" stroke="{INK}" stroke-width="{SW}"/>'


def fries():
    d = (lg('box', [(0, ORANGE, None), (1, ORANGE_D, None)]) + lg('fry', [(0, '#ffe08a', None), (1, '#f2b23a', None)]))
    b = ''
    for k, (x, h, r) in enumerate(((78, 112, -12), (98, 132, -5), (118, 140, 2), (138, 130, 6), (158, 118, 12), (108, 120, -2), (148, 124, 4))):
        b += f'<rect x="{x}" y="{176 - h}" width="20" height="{h}" rx="5" fill="url(#fry)" stroke="{INK}" stroke-width="5" transform="rotate({r} {x + 10} 176)"/>'
    box = 'M58 108 L198 108 L182 236 L74 236 Z'
    b += f'<path d="{box}" fill="url(#box)" stroke="{INK}" stroke-width="{SW}" stroke-linejoin="round"/>'
    b += f'<path d="M64 132 L192 132" stroke="{COAL}" stroke-width="10"/>'
    b += mark_glyph(128, 182, 30, ink=CREAM)
    return svg(256, 256, d, f'<g filter="url(#drop)">{b}</g>')


def drink():
    d = lg('cup', [(0, COAL2, None), (1, COAL, None)]) + lg('lid', [(0, '#f5f5f2', None), (1, '#cfd2d6', None)])
    b = f'<path d="M176 30 L156 92" stroke="{MINT}" stroke-width="12" stroke-linecap="round"/><path d="M176 30 L156 92" stroke="{INK}" stroke-width="3" stroke-linecap="round" stroke-opacity=".4"/>'
    b += f'<path d="M72 96 L184 96 L170 236 L86 236 Z" fill="url(#cup)" stroke="{INK}" stroke-width="{SW}" stroke-linejoin="round"/>'
    b += f'<rect x="62" y="80" width="132" height="24" rx="10" fill="url(#lid)" stroke="{INK}" stroke-width="{SW}"/>'
    b += f'<path d="M80 150 L176 150 L172 190 L84 190 Z" fill="{ORANGE}"/>'
    b += mark_glyph(128, 170, 22, ink=CREAM)
    b += '<path d="M90 112 L96 220" stroke="#fff" stroke-opacity=".18" stroke-width="10" stroke-linecap="round"/>'
    return svg(256, 256, d, f'<g filter="url(#drop)">{b}</g>')


def mark_glyph(cx, cy, s, ink=CREAM):
    """The small mark: a falling-chart zigzag with an arrow head (used on packaging)."""
    p = [(-1, -0.6), (-0.35, 0.05), (0.05, -0.35), (0.7, 0.45)]
    pts = ' '.join(f'{cx + x*s:.1f},{cy + y*s:.1f}' for x, y in p)
    ax, ay = cx + 0.7 * s, cy + 0.45 * s
    return (f'<polyline points="{pts}" fill="none" stroke="{ink}" stroke-width="{max(4, s*0.22):.1f}" stroke-linecap="round" stroke-linejoin="round"/>'
            f'<path d="M{ax - s*0.42:.1f} {ay - s*0.02:.1f} L{ax + s*0.08:.1f} {ay + s*0.08:.1f} L{ax - s*0.02:.1f} {ay - s*0.44:.1f}" fill="none" stroke="{ink}" stroke-width="{max(4, s*0.22):.1f}" stroke-linecap="round" stroke-linejoin="round"/>')


def badge(cx, cy, r):
    """The Rekt Burger mark: a charcoal disc with an orange rim, a stacked burger, and a mint falling chart cracking the
    top bun (the crack carries on as the chart line)."""
    d = (rg('bd', [(0, '#353943', None), (1, COAL, None)], .45, .35, .75) + lg('rim', [(0, '#ff8b52', None), (1, ORANGE_D, None)]))
    s = r / 128
    b = f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="url(#rim)" stroke="{INK}" stroke-width="{6*s}"/>'
    b += f'<circle cx="{cx}" cy="{cy}" r="{r*0.84}" fill="url(#bd)" stroke="{INK}" stroke-width="{4*s}"/>'
    g = f'<g transform="translate({cx - 128*s} {cy - 128*s + 6*s}) scale({s})">'
    # burger stack inside
    g += f'<path d="M62 168 L194 168 C194 196 180 204 128 204 C76 204 62 196 62 168 Z" fill="#e9963f" stroke="{INK}" stroke-width="7" stroke-linejoin="round"/>'
    g += f'<rect x="56" y="140" width="144" height="30" rx="15" fill="#5a2f1b" stroke="{INK}" stroke-width="7"/>'
    g += f'<path d="M54 132 L202 132 L196 146 L60 146 Z" fill="{MINT}" stroke="{INK}" stroke-width="6" stroke-linejoin="round"/>'
    top = 'M58 128 C58 58 198 58 198 128 Z'
    g += f'<path d="{top}" fill="#f7a748" stroke="{INK}" stroke-width="7" stroke-linejoin="round"/>'
    # the crack: a falling chart through the bun
    g += f'<polyline points="70,76 104,104 126,86 168,124" fill="none" stroke="{INK}" stroke-width="13" stroke-linecap="round" stroke-linejoin="round"/>'
    g += f'<polyline points="70,76 104,104 126,86 168,124" fill="none" stroke="{CREAM}" stroke-width="6" stroke-linecap="round" stroke-linejoin="round"/>'
    g += f'<path d="M150 124 L172 128 L170 106" fill="none" stroke="{INK}" stroke-width="13" stroke-linecap="round" stroke-linejoin="round"/>'
    g += f'<path d="M150 124 L172 128 L170 106" fill="none" stroke="{CREAM}" stroke-width="6" stroke-linecap="round" stroke-linejoin="round"/>'
    for sx, sy in ((92, 74), (146, 72), (178, 98)):
        g += f'<ellipse cx="{sx}" cy="{sy}" rx="5" ry="3" fill="{CREAM}"/>'
    g += '</g>'
    return d, b + g


def logo():
    W, H = 1024, 384
    d, b = badge(192, 192, 150)
    b = f'<g filter="url(#drop)">{b}</g>'
    b += (f'<text x="372" y="210" font-family="Bowlby One" font-size="150" fill="{ORANGE}" stroke="{INK}" stroke-width="10" paint-order="stroke" letter-spacing="2">REKT</text>'
          f'<text x="378" y="300" font-family="Archivo Black" font-size="84" fill="{CREAM}" stroke="{INK}" stroke-width="9" paint-order="stroke" letter-spacing="10">BURGER</text>'
          f'<rect x="378" y="318" width="560" height="10" rx="5" fill="{MINT}"/>')
    return svg(W, H, d, b)


def ticket_bg():
    W, H = 384, 576
    zig = lambda y, up: ' '.join(f'{x},{y + (0 if (x // 16) % 2 == 0 else (-10 if up else 10))}' for x in range(0, W + 1, 16))
    body = f'M0 14 L{zig(14, True).replace(" ", " L")} L{W} {H - 14} L' + zig(H - 14, False).replace(' ', ' L')[::-1]
    # simpler: build points list
    top = [(x, 14 - (10 if (x // 16) % 2 else 0)) for x in range(0, W + 1, 16)]
    bot = [(x, H - 14 + (10 if (x // 16) % 2 else 0)) for x in range(W, -1, -16)]
    pts = ' '.join(f'{x},{y}' for x, y in top + bot)
    d = lg('pap', [(0, '#fffdf7', None), (1, '#f4ecdc', None)])
    b = f'<polygon points="{pts}" fill="url(#pap)"/>'
    for y in range(110, H - 60, 44):
        b += f'<line x1="36" y1="{y}" x2="{W - 36}" y2="{y}" stroke="#d9cdb6" stroke-width="2" stroke-dasharray="6 6"/>'
    b += f'<rect x="0" y="22" width="{W}" height="12" fill="{ORANGE}" opacity=".9"/>'
    return svg(W, H, d, b)


def tray():
    W, H = 512, 320
    d = lg('tr', [(0, '#3a3e47', None), (1, '#202329', None)]) + lg('liner', [(0, '#e9fff4', None), (1, '#c9f3de', None)])
    b = f'<rect x="10" y="20" width="{W - 20}" height="{H - 36}" rx="34" fill="url(#tr)" stroke="{INK}" stroke-width="8"/>'
    b += f'<rect x="40" y="46" width="{W - 80}" height="{H - 92}" rx="20" fill="url(#liner)"/>'
    for k in range(6):
        b += mark_glyph(90 + k * 66, 160 + (k % 2) * 40, 16, ink='#9fdcbf')
    b += f'<rect x="30" y="28" width="{W - 60}" height="10" rx="5" fill="#fff" opacity=".12"/>'
    return svg(W, H, d, f'<g filter="url(#drop)">{b}</g>')


def sticker(kind):
    W = 192
    if kind == 'perfect':
        pts = ' '.join(f'{96 + math.cos(a)*(84 if k % 2 == 0 else 70):.1f},{96 + math.sin(a)*(84 if k % 2 == 0 else 70):.1f}'
                       for k, a in enumerate(i * math.pi / 12 for i in range(24)))
        b = (f'<polygon points="{pts}" fill="{MINT}" stroke="{INK}" stroke-width="6" stroke-linejoin="round"/>'
             f'<circle cx="96" cy="96" r="58" fill="none" stroke="{CREAM}" stroke-width="4" stroke-dasharray="4 6"/>'
             f'<text x="96" y="92" text-anchor="middle" font-family="Archivo Black" font-size="26" fill="{INK}">PERFECT</text>'
             f'<path d="M66 108 L86 128 L128 84" fill="none" stroke="{INK}" stroke-width="12" stroke-linecap="round" stroke-linejoin="round"/>')
    else:
        b = (f'<g transform="rotate(-12 96 96)"><rect x="18" y="56" width="156" height="80" rx="14" fill="none" stroke="{ORANGE_D}" stroke-width="9"/>'
             f'<text x="96" y="114" text-anchor="middle" font-family="Archivo Black" font-size="44" fill="{ORANGE_D}">WRONG</text></g>')
    return svg(W, W, '', f'<g filter="url(#drop)">{b}</g>')


def sign_board():
    """The storefront's lit board (IslandV2Shells sign, 4:1, lit by emission): charcoal with the logo; the top 8 rows are
    the orange neon-tube colour and the bottom 8 the mint one (the facade's light strips sample them)."""
    W, H = 1024, 256
    d0, mark = badge(140, 128, 104)
    d = d0 + lg('sb', [(0, '#2d3038', None), (1, '#16181c', None)])
    b = (f'<rect width="{W}" height="{H}" fill="url(#sb)"/><rect x="10" y="10" width="{W-20}" height="{H-20}" rx="12" fill="none" stroke="{ORANGE}" stroke-width="5"/>'
         + mark
         + f'<text x="268" y="150" font-family="Bowlby One" font-size="100" fill="{ORANGE}" letter-spacing="2">REKT</text>'
         + f'<text x="600" y="148" font-family="Archivo Black" font-size="74" fill="{CREAM}" letter-spacing="4">BURGER</text>'
         + f'<text x="276" y="206" font-family="Inter" font-weight="800" font-size="30" fill="{MINT}" letter-spacing="6">FLIPPING SINCE THE LAST TOP</text>'
         + f'<rect x="0" y="0" width="{W}" height="8" fill="{ORANGE}"/><rect x="0" y="{H-8}" width="{W}" height="8" fill="{MINT}"/>')
    return svg(W, H, d, b)


MENU = [('Rug Pull Burger', '$4.20'), ('Liquidation Fries', '$1.99'), ('Diamond Hands Shake', '$3.69'),
        ('Exit Liquidity Combo', '$6.90'), ('Paper Hands Nuggets', '$2.49'), ('Bag Holder Wrap', '$3.33'),
        ('Gas Fee Soda', '$0.99'), ('Rekt Melt', '$4.04')]


def rb_signs():
    """2048 x 1024 atlas for the restaurant (rekt_room.py maps its quads onto these boxes):
    menu board A (0, 0, 1024, 512); menu board B (1024, 0, 2048, 512); logo wall (0, 512, 1024, 896);
    order screen (1024, 512, 1536, 896); KITCHEN header (1536, 512, 2048, 704); hours plaque (1536, 704, 2048, 896)."""
    W, H = 2048, 1024
    d0, mark = badge(170, 704, 130)
    d = d0 + lg('mb', [(0, '#24272e', None), (1, '#15171b', None)]) + lg('scr', [(0, '#0f2a22', None), (1, '#071510', None)])
    b = ''
    for bx, items, title in ((0, MENU[:4], 'BURGERS & COMBOS'), (1024, MENU[4:], 'SIDES & SIPS')):
        b += f'<rect x="{bx}" y="0" width="1024" height="512" fill="url(#mb)"/><rect x="{bx + 10}" y="10" width="1004" height="492" rx="14" fill="none" stroke="{ORANGE}" stroke-width="6"/>'
        b += f'<text x="{bx + 512}" y="84" text-anchor="middle" font-family="Archivo Black" font-size="52" letter-spacing="6" fill="{ORANGE}">{title}</text>'
        for k, (name, price) in enumerate(items):
            y = 170 + k * 88
            b += (f'<text x="{bx + 60}" y="{y}" font-family="Inter" font-weight="800" font-size="44" fill="{CREAM}">{name}</text>'
                  f'<text x="{bx + 964}" y="{y}" text-anchor="end" font-family="Archivo Black" font-size="44" fill="{MINT}">{price}</text>'
                  f'<line x1="{bx + 60}" y1="{y + 24}" x2="{bx + 964}" y2="{y + 24}" stroke="#3a3e47" stroke-width="2"/>')
        b += f'<text x="{bx + 512}" y="490" text-anchor="middle" font-family="Inter" font-weight="600" font-size="24" fill="#8a8f99">Prices in Cash. Cash is game money and can never be withdrawn.</text>'
    # logo wall
    b += f'<rect x="0" y="512" width="1024" height="384" fill="{COAL}"/>' + mark
    b += (f'<text x="340" y="700" font-family="Bowlby One" font-size="140" fill="{ORANGE}">REKT</text>'
          f'<text x="346" y="800" font-family="Archivo Black" font-size="84" letter-spacing="10" fill="{CREAM}">BURGER</text>'
          f'<rect x="346" y="822" width="600" height="10" rx="5" fill="{MINT}"/>')
    # order screen
    b += f'<rect x="1024" y="512" width="512" height="384" fill="url(#scr)"/>'
    b += f'<text x="1280" y="580" text-anchor="middle" font-family="Archivo Black" font-size="38" fill="{MINT}">NOW SERVING</text>'
    for k, n in enumerate((' #042', ' #069', ' #420')):
        b += f'<text x="1280" y="{660 + k*72}" text-anchor="middle" font-family="Archivo Black" font-size="60" fill="{CREAM}">{n.strip()}</text>'
    # kitchen header + plaque
    b += (f'<rect x="1536" y="512" width="512" height="192" fill="{ORANGE}"/><text x="1792" y="636" text-anchor="middle" font-family="Archivo Black" font-size="72" letter-spacing="8" fill="{COAL}">KITCHEN</text>'
          f'<rect x="1536" y="704" width="512" height="192" fill="{CREAM}"/><text x="1792" y="782" text-anchor="middle" font-family="Archivo Black" font-size="34" fill="{COAL}">OPEN 24/7</text>'
          f'<text x="1792" y="836" text-anchor="middle" font-family="Inter" font-weight="800" font-size="19" fill="{ORANGE_D}">WE NEVER CLOSE. NEITHER DO MARKETS.</text>')
    b += f'<rect x="0" y="896" width="2048" height="128" fill="{COAL}"/>'
    return svg(W, H, d, b)


def rb_floor():
    """A seamless 1024 tile (2 m on the floor): warm cream terrazzo with burnt-orange, charcoal and mint chips."""
    import random
    rnd = random.Random(20261025)
    W = 1024
    chips = ''
    cols = ['#e2632c', '#2b2e36', '#38c993', '#c9b08a', '#8f8476']
    for _ in range(900):
        x, y = rnd.uniform(0, W), rnd.uniform(0, W)
        r = rnd.choice([3, 4, 5, 6, 8, 10, 13]) * rnd.uniform(0.7, 1.2)
        c = rnd.choices(cols, [3, 4, 1.5, 4, 3])[0]
        k = rnd.randint(3, 6)
        pts = []
        for j in range(k):
            a = 2 * math.pi * j / k + rnd.uniform(-0.4, 0.4)
            rr = r * rnd.uniform(0.6, 1.2)
            pts.append((rr * math.cos(a), rr * math.sin(a)))
        for dx in (-W, 0, W):
            for dy in (-W, 0, W):
                if -20 < x + dx < W + 20 and -20 < y + dy < W + 20:
                    chips += '<polygon points="' + ' '.join(f'{x + dx + px:.1f},{y + dy + py:.1f}' for px, py in pts) + f'" fill="{c}"/>'
    body = f'<rect width="{W}" height="{W}" fill="#ece3d3"/>{chips}'
    return svg(W, W, '', body)


def rb_tile():
    """A seamless 1024 tile (1 m on the wall): pale mint glazed subway tiles (10 x 20 cm), light grout, a soft glaze sheen."""
    W = 1024; th, tw = 102.4, 204.8
    d = lg('gl', [(0, '#ffffff', .55), (.5, '#ffffff', 0), (1, '#000000', .06)])
    b = f'<rect width="{W}" height="{W}" fill="#d9ddd8"/>'
    for r in range(10):
        off = 0 if r % 2 == 0 else tw / 2
        for c in range(-1, 6):
            x = c * tw + off
            b += f'<rect x="{x + 4:.1f}" y="{r * th + 4:.1f}" width="{tw - 8:.1f}" height="{th - 8:.1f}" rx="6" fill="#cdeee0"/>'
            b += f'<rect x="{x + 4:.1f}" y="{r * th + 4:.1f}" width="{tw - 8:.1f}" height="{th - 8:.1f}" rx="6" fill="url(#gl)"/>'
    return svg(W, W, d, b)


ASSETS = {
    'logo': (logo, 1024, 384),
    'bun_top': (lambda: icon(bun_top_g), 256, 256), 'bun_bottom': (lambda: icon(bun_bottom_g), 256, 256),
    'patty': (lambda: icon(patty_g), 256, 256), 'cheese': (lambda: icon(cheese_g), 256, 256),
    'lettuce': (lambda: icon(lettuce_g), 256, 256), 'tomato': (lambda: icon(tomato_g), 256, 256),
    'onion': (lambda: icon(onion_g), 256, 256), 'pickle': (lambda: icon(pickle_g), 256, 256),
    'fries': (fries, 256, 256), 'drink': (drink, 256, 256), 'burger_done': (burger_done, 256, 256),
    'ticket_bg': (ticket_bg, 384, 576), 'tray': (tray, 512, 320),
    'sticker_perfect': (lambda: sticker('perfect'), 192, 192), 'sticker_wrong': (lambda: sticker('wrong'), 192, 192),
    'sign_rekt_burger': (sign_board, 1024, 256), 'rb_signs': (rb_signs, 2048, 1024),
    'rb_floor': (rb_floor, 1024, 1024), 'rb_tile': (rb_tile, 1024, 1024),
}

if __name__ == '__main__':
    import sys
    only = sys.argv[1:] or list(ASSETS)
    for k in only:
        f, w, h = ASSETS[k]
        open(os.path.join(OUT, k + '.html'), 'w').write(page(f()))
    json.dump({k: ASSETS[k][1:] for k in ASSETS}, open(os.path.join(HERE, 'out', 'sizes.json'), 'w'))
    print(len(only), 'pages')
