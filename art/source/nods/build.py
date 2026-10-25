#!/usr/bin/env python3
"""Community nods art (R4, 2026-10-25): the painted nod's atlas, original vector art rendered with headless Chrome.

One painted disc per set (the community-eggs rules: our own drawings, no logos, no lettering, no real brand's likeness):
  memecoin_row     a grinning coin with a sparkle, on sunny yellow
  nft_gallery      a gilt picture frame round a pixel smile, on violet
  builders_row     a hammer crossed with a wrench over a brick, on orange
  defi_district    a water drop riding a wave with a looping arrow, on teal
  superteam_corner a pennant with a star over a string of bunting, on green
  event_hall       a ticket stub with a star and its perforation, on pink
Atlas nod_atlas 1536x1024: tiles of 512 in the order above (3 across, 2 down). In each tile the disc face fills the
circle (r 236, a cream rim ring); the corners are painted wood (the stake), the top-left corner a cream patch (the rim's
edge). client/Assets/Scripts/World/Season/NodField.cs builds the UVs to match.
"""
import json, os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out', 'html')
os.makedirs(OUT, exist_ok=True)

INK, CREAM, CREAM_D = '#2a211b', '#fff4dc', '#ecd8b2'
WOOD, WOOD_D, WOOD_L = '#9a6a3f', '#6f4a2a', '#b98a5c'
SETS = [('memecoin_row', '#f7c948', '#d99a12'), ('nft_gallery', '#8b6cf0', '#5b3fc4'), ('builders_row', '#f5813a', '#c85512'),
        ('defi_district', '#22b8a8', '#0f8076'), ('superteam_corner', '#3ec46a', '#1e8a43'), ('event_hall', '#ee5a9c', '#b8306c')]


def emblem(set_id, dark):
    if set_id == 'memecoin_row':
        return f'''<circle cx="256" cy="262" r="118" fill="#ffd95a" stroke="{dark}" stroke-width="14"/>
<circle cx="256" cy="262" r="92" fill="none" stroke="#e8b52a" stroke-width="10"/>
<circle cx="222" cy="240" r="13" fill="{INK}"/><circle cx="290" cy="240" r="13" fill="{INK}"/>
<path d="M208 286 Q256 330 304 286" fill="none" stroke="{INK}" stroke-width="12" stroke-linecap="round"/>
<path d="M368 142 l10 24 24 10 -24 10 -10 24 -10 -24 -24 -10 24 -10z" fill="{CREAM}"/>
<path d="M150 168 l6 14 14 6 -14 6 -6 14 -6 -14 -14 -6 14 -6z" fill="{CREAM}"/>'''
    if set_id == 'nft_gallery':
        px = ''.join(f'<rect x="{196 + 15 * c}" y="{196 + 15 * r}" width="15" height="15" fill="{INK}"/>'
                     for r, row in enumerate(['..XXXX..', '.X....X.', 'X.X..X.X', 'X......X', 'X.X..X.X', 'X..XX..X', '.X....X.', '..XXXX..'])
                     for c, ch in enumerate(row) if ch == 'X')
        return f'''<rect x="150" y="150" width="212" height="212" rx="10" fill="#e9b949" stroke="{dark}" stroke-width="12"/>
<rect x="176" y="176" width="160" height="160" fill="{CREAM}" stroke="#b9862a" stroke-width="8"/>{px}
<circle cx="150" cy="150" r="14" fill="#ffe08a"/><circle cx="362" cy="150" r="14" fill="#ffe08a"/>
<circle cx="150" cy="362" r="14" fill="#ffe08a"/><circle cx="362" cy="362" r="14" fill="#ffe08a"/>'''
    if set_id == 'builders_row':
        return f'''<rect x="168" y="300" width="176" height="64" rx="8" fill="#c44a2c" stroke="{INK}" stroke-width="8"/>
<path d="M168 332 H344 M226 300 V332 M286 332 V364" stroke="{CREAM_D}" stroke-width="6"/>
<g transform="rotate(-38 256 230)"><rect x="244" y="130" width="24" height="190" rx="8" fill="{WOOD_L}" stroke="{INK}" stroke-width="7"/>
<rect x="206" y="112" width="100" height="44" rx="8" fill="#9aa3ad" stroke="{INK}" stroke-width="8"/></g>
<g transform="rotate(38 256 230)"><rect x="246" y="150" width="20" height="170" rx="8" fill="#b7c0c9" stroke="{INK}" stroke-width="7"/>
<path d="M226 150 a30 30 0 1 1 60 0 l-14 0 0 -18 -32 0 0 18z" fill="#b7c0c9" stroke="{INK}" stroke-width="7"/></g>'''
    if set_id == 'defi_district':
        return f'''<path d="M256 140 C300 200 330 236 330 276 a74 74 0 0 1 -148 0 C182 236 212 200 256 140z" fill="#bff3ea" stroke="{INK}" stroke-width="10"/>
<path d="M226 270 a30 30 0 0 0 30 34" fill="none" stroke="#ffffff" stroke-width="10" stroke-linecap="round"/>
<path d="M130 362 q32 -26 64 0 t64 0 t64 0 t64 0" fill="none" stroke="{CREAM}" stroke-width="14" stroke-linecap="round"/>
<path d="M352 206 a64 64 0 0 1 -6 100" fill="none" stroke="{CREAM}" stroke-width="12" stroke-linecap="round"/>
<path d="M330 300 l20 12 2 -24z" fill="{CREAM}"/>'''
    if set_id == 'superteam_corner':
        flags = ''.join(f'<path d="M{150 + 44 * k} {352 + (4 if k % 2 else 0)} l22 34 22 -34z" fill="{c}" stroke="{INK}" stroke-width="4"/>'
                        for k, c in enumerate([CREAM, '#ffd95a', CREAM, '#ff8f6b', CREAM]))
        return f'''<path d="M140 350 Q256 372 372 350" fill="none" stroke="{INK}" stroke-width="5"/>{flags}
<rect x="196" y="128" width="14" height="210" rx="6" fill="{WOOD_D}"/>
<path d="M210 136 L350 186 L210 236z" fill="{CREAM}" stroke="{INK}" stroke-width="8" stroke-linejoin="round"/>
<path d="M262 160 l9 20 21 2 -16 14 5 21 -19 -11 -19 11 5 -21 -16 -14 21 -2z" fill="#ffd95a" stroke="{INK}" stroke-width="4"/>'''
    return f'''<g transform="rotate(-12 256 256)"><path d="M146 186 H366 V222 a26 26 0 0 0 0 68 V326 H146 V290 a26 26 0 0 0 0 -68z" fill="{CREAM}" stroke="{INK}" stroke-width="10" stroke-linejoin="round"/>
<path d="M300 196 V316" stroke="{dark}" stroke-width="6" stroke-dasharray="10 9"/>
<path d="M222 222 l12 26 28 3 -21 19 6 28 -25 -14 -25 14 6 -28 -21 -19 28 -3z" fill="#ffd95a" stroke="{INK}" stroke-width="5"/>
<circle cx="334" cy="256" r="12" fill="{dark}"/></g>'''


def tile(i, set_id, col, dark):
    x, y = (i % 3) * 512, (i // 3) * 512
    grain = ''.join(f'<path d="M0 {12 + 22 * k} Q128 {6 + 22 * k} 256 {14 + 22 * k} T512 {10 + 22 * k}" stroke="{WOOD_D}" stroke-opacity=".35" stroke-width="3" fill="none"/>' for k in range(24))
    brush = ''.join(f'<path d="M{60 + 9 * k} 70 Q{256} {120 + 13 * (k % 7)} {452 - 9 * k} 440" stroke="#ffffff" stroke-opacity=".05" stroke-width="18" fill="none"/>' for k in range(14))
    return f'''<g transform="translate({x} {y})"><rect width="512" height="512" fill="{WOOD}"/>{grain}
<rect x="0" y="0" width="72" height="72" fill="{CREAM}"/>
<circle cx="256" cy="256" r="236" fill="{CREAM}"/><circle cx="256" cy="256" r="214" fill="{col}"/>
<g clip-path="url(#disc)">{brush}</g><circle cx="256" cy="256" r="214" fill="none" stroke="{dark}" stroke-width="6" stroke-opacity=".6"/>
{emblem(set_id, dark)}</g>'''


def main():
    tiles = ''.join(tile(i, s, c, d) for i, (s, c, d) in enumerate(SETS))
    svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="1536" height="1024" viewBox="0 0 1536 1024"><defs><clipPath id="disc"><circle cx="256" cy="256" r="214"/></clipPath></defs>{tiles}</svg>'
    open(os.path.join(OUT, 'nod_atlas.html'), 'w').write(f'<!doctype html><html><body style="margin:0;background:transparent">{svg}</body></html>')
    json.dump({'nod_atlas': [1536, 1024]}, open(os.path.join(HERE, 'out', 'sizes.json'), 'w'))


if __name__ == '__main__':
    main()
