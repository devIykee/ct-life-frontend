"""Breakpoint 2026 UI art for the Events app and the claim moment (our own design, Inter / Manrope OFL):
  hero.png        1200 x 720  the event card's poster (no Solana Foundation or Breakpoint logo)
  lanyard.png      720 x 960  the attendee lanyard (red strap, cream badge)        -> claim moment, wardrobe preview
  lanyard_gold.png 720 x 960  the IRL lanyard (gold strap, the "I WAS THERE" badge)
  irl_badge.png    256 x 256  the IRL badge for nameplates and the attendee board
Run: python3 -I art/source/events/bp26/ui.py   (writes into client/Assets/Resources/UI/Events/ through headless Chrome)
"""
import os, subprocess

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
FONTS = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'UI', 'Fonts')
OUT = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'UI', 'Events')
CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'
BLACK, N1, N2, N4, WHITE, PURPLE, GOLD, GOLD2 = '#000000', '#1e1e1e', '#353535', '#a2a2a2', '#ffffff', '#aa67fb', '#f2b33d', '#b9801c'

CSS = f"""
@font-face{{font-family:Geist;font-weight:600;src:url('file://{FONTS}/Events/Geist-SemiBold.ttf')}}
@font-face{{font-family:GeistMono;font-weight:500;src:url('file://{FONTS}/Events/GeistMono-Medium.ttf')}}
@font-face{{font-family:GeistMono;font-weight:600;src:url('file://{FONTS}/Events/GeistMono-SemiBold.ttf')}}
html,body{{margin:0;background:transparent;overflow:hidden}}
.wm{{font-family:Geist;font-weight:600;letter-spacing:-.045em;line-height:.95;white-space:nowrap}}
.cap{{font-family:GeistMono;font-weight:500;letter-spacing:.06em;text-transform:uppercase;white-space:nowrap}}
"""


import random


def steps(w, h, seed, color, side='bottom', rows=6, cell=20):
    rnd = random.Random(seed); out, x, level = [], 0, 1
    while x < w:
        bw = rnd.choice([1, 1, 2, 3, 5, 8]) * cell
        level = max(0, min(rows, level + rnd.choice([-1, 0, 0, 1, 1])))
        out.append(f'<div style="position:absolute;left:{x}px;{side}:0;width:{bw}px;height:{level * cell}px;background:{color}"></div>')
        if rnd.random() < 0.35 and level:
            out.append(f'<div style="position:absolute;left:{x + rnd.randint(0, max(0, bw - cell))}px;{side}:{(level + rnd.randint(1, 2)) * cell}px;width:{cell}px;height:{cell}px;background:{color}"></div>')
        x += bw
    return f'<div style="position:absolute;left:0;right:0;{side}:0;height:{h}px;overflow:hidden">{"".join(out)}</div>'


def tower(size, color, opacity):
    return (f'<svg width="{size * 0.42:.0f}" height="{size}" viewBox="0 0 42 100" style="opacity:{opacity};display:block">'
            f'<defs><pattern id="ht" width="1.6" height="1.6" patternUnits="userSpaceOnUse"><circle cx=".8" cy=".8" r=".55" fill="{color}"/></pattern></defs>'
            f'<g fill="url(#ht)"><path d="M21 0 L23 9 L25 14 L25 18 L17 18 L17 14 L19 9 Z"/><rect x="15" y="18" width="12" height="6"/>'
            f'<rect x="13" y="24" width="16" height="18"/><rect x="11" y="42" width="20" height="58"/><rect x="6" y="70" width="5" height="30"/><rect x="31" y="70" width="5" height="30"/></g>'
            f'<circle cx="21" cy="33" r="5.5" fill="none" stroke="{color}" stroke-width="1.2"/></svg>')


def hero():
    return 1200, 720, f"""<div style="position:absolute;inset:0;background:{BLACK};color:{WHITE};overflow:hidden">
 <div style="position:absolute;left:0;right:0;top:0;height:520px;background:linear-gradient(90deg,{PURPLE}55,{PURPLE} 60%)"></div>
 <div style="position:absolute;right:120px;top:40px">{tower(560, BLACK, 0.55)}</div>
 <div style="position:absolute;left:0;right:0;top:380px;height:160px">{steps(1200, 160, 7, BLACK, 'bottom', 6, 24)}</div>
 <div style="position:absolute;left:64px;top:60px" class="cap">LONDON · OLYMPIA</div>
 <div style="position:absolute;left:58px;top:150px;font-size:150px" class="wm">Breakpoint<br>2026</div>
 <div style="position:absolute;left:64px;bottom:60px;font-size:26px;line-height:1.5" class="cap">15–17 November 2026</div>
 <div style="position:absolute;left:520px;bottom:60px;font-size:26px;line-height:1.5;color:{N4}" class="cap">Olympia Convention Centre · London</div>
</div>"""


def lanyard(gold):
    strap, strap2, accent, chipc = (GOLD, GOLD2, GOLD, BLACK) if gold else (PURPLE, '#7e3fd6', PURPLE, BLACK)
    title = 'I WAS THERE' if gold else 'ATTENDEE'
    return 720, 960, f"""<svg width="720" height="960" viewBox="0 0 720 960" style="position:absolute;inset:0">
 <defs><linearGradient id="s" x1="0" x2="1"><stop offset="0" stop-color="{strap2}"/><stop offset=".5" stop-color="{strap}"/><stop offset="1" stop-color="{strap2}"/></linearGradient>
 <filter id="d" x="-20%" y="-20%" width="140%" height="140%"><feDropShadow dx="0" dy="18" stdDeviation="20" flood-color="#000" flood-opacity=".35"/></filter></defs>
 <path d="M150 0 L300 420 L338 420 L210 0Z" fill="url(#s)"/><path d="M570 0 L420 420 L382 420 L510 0Z" fill="url(#s)"/>
 <g fill="{BLACK}" font-family="GeistMono" font-weight="500" font-size="19" letter-spacing="3">
  <text transform="translate(196 40) rotate(70)">BREAKPOINT 2026 · LONDON · BREAKPOINT 2026</text>
  <text transform="translate(548 36) rotate(109.6)">BREAKPOINT 2026 · LONDON · BREAKPOINT 2026</text></g>
 <rect x="330" y="400" width="60" height="46" rx="6" fill="#9aa0a8"/><rect x="342" y="430" width="36" height="40" rx="4" fill="#c9ced6"/>
 <g filter="url(#d)"><rect x="170" y="452" width="380" height="490" rx="14" fill="{BLACK}"/></g>
 <rect x="320" y="474" width="80" height="14" rx="7" fill="{N2}"/>
 <rect x="170" y="502" width="380" height="140" fill="{accent}"/>
 <g fill="{BLACK}"><rect x="170" y="612" width="40" height="30"/><rect x="250" y="592" width="60" height="50"/><rect x="350" y="622" width="40" height="20"/><rect x="430" y="602" width="80" height="40"/><rect x="290" y="562" width="20" height="20"/></g>
 <text x="196" y="538" fill="{BLACK}" font-family="GeistMono" font-weight="500" font-size="18" letter-spacing="2">LONDON · 15–17 NOV</text>
 <text x="196" y="716" fill="{WHITE}" font-family="Geist" font-weight="600" font-size="62" letter-spacing="-2.5">Breakpoint</text>
 <text x="196" y="790" fill="{accent}" font-family="Geist" font-weight="600" font-size="62" letter-spacing="-2.5">2026</text>
 <rect x="196" y="846" width="328" height="56" rx="3" fill="{accent}"/>
 <text x="360" y="882" fill="{chipc}" text-anchor="middle" font-family="GeistMono" font-weight="600" font-size="22" letter-spacing="3">{title}</text>
</svg>"""


def irl_badge():
    return 256, 256, f"""<svg width="256" height="256" viewBox="0 0 256 256" style="position:absolute;inset:0">
 <defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ffe29a"/><stop offset=".55" stop-color="{GOLD}"/><stop offset="1" stop-color="{GOLD2}"/></linearGradient></defs>
 <circle cx="128" cy="128" r="120" fill="url(#g)"/><circle cx="128" cy="128" r="98" fill="{BLACK}"/>
 <text x="128" y="150" fill="{GOLD}" text-anchor="middle" font-family="Geist" font-weight="600" font-size="78" letter-spacing="-3">IRL</text>
 <text x="128" y="190" fill="{WHITE}" text-anchor="middle" font-family="GeistMono" font-weight="500" font-size="17" letter-spacing="2">LONDON · 26</text>
</svg>"""


def shot(name, w, h, body):
    os.makedirs(OUT, exist_ok=True)
    html = os.path.join(HERE, 'ui_' + name + '.html')
    open(html, 'w').write(f'<!doctype html><html><head><meta charset="utf-8"><style>{CSS}html,body{{width:{w}px;height:{h}px}}</style></head>'
                          f'<body><div style="position:relative;width:{w}px;height:{h}px">{body}</div></body></html>')
    out = os.path.join(OUT, name + '.png')
    subprocess.run([CHROME, '--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
                    '--default-background-color=00000000', '--virtual-time-budget=4000', f'--window-size={w},{h}',
                    '--screenshot=' + out, 'file://' + html], check=True, capture_output=True)
    print('wrote', out)


if __name__ == '__main__':
    shot('hero', *hero())
    shot('lanyard', *lanyard(False))
    shot('lanyard_gold', *lanyard(True))
    shot('irl_badge', *irl_badge())
