# CT Life website (v3, 25 October 2026)

Single-page site in the spirit of marathonthegame.com: one loud accent on black, mono data labels, attached panels and a hero that moves. Static HTML, CSS and JS with no build step and no dependencies. Not yet approved by the user.

## Preview

```sh
python3 -m http.server 4179 --bind 127.0.0.1 --directory website
```

Open <http://127.0.0.1:4179/>

## Files

- `index.html`, `site.css`, `site.js`: the page. Gameplay hero reel; a scroll-linked hero exit; parallax (`data-parallax`) and wipe reveals (`data-wipe`) on media frames; a drifting headline (`data-drift`); and the pinned "Inside the game" reel. That reel follows Marathon's pinned stack: a sticky frame whose vertical track moves one full screen per scroll step, with text cards crossfading.
- `map.js`: CT Island drawn from `assets/v2/map/island.json`.
- `credits.html`: provenance and licences.
- The title painting is banned by the user (2026-10-25) and was removed with its shader. Do not bring it back.

## Launch switch

`DOWNLOAD_URL` at the top of `site.js`. While it is empty, every "Get the game" button opens the truthful "Coming to your Seeker. There isn't a public download yet." dialog. Once it is set, the buttons link straight to the store.

## How the assets were made

- `assets/v3/`: `hero-reel(-720).mp4` and the five `p-*.mp4` reel panels are cut from Seeker recordings dated 23 to 25 October (casino-r3, breakpoint-r2 final, perf2 drive, home-shop van) plus two film plates. Each is centre-cropped so the HUD falls mostly outside the frame (device crop 1680x945+470+110), with a light grade. Recut with `python3 website/tools/cut_reel.py website/assets/v3` when better footage exists.
- `assets/v2/map/island.json` is exported from `ct-life-repair/tools/world/islandgen/out` (generated 2026-10-24). Contours every 6 m come from `height_dressed.r16`. The summit is 185 m above sea level.
- `assets/v2/clips/` are cut from the 20 October film's clean source clips (`ct-life-evidence/video-production-20261020/motion/assets`): 3:4 crops, silent H.264 with faststart. `teaser.mp4` is a web encode of `CT-Life-Teaser-1080p.mp4`.
- `assets/v2/timeline/` are crops of device captures from `breakpoint-r2-20261024`, `retention-r1-20261023` and `world-polish-20261023`.
- `assets/v2/tribes/` and `assets/v2/origin/` are converted from `client/Assets/Resources/UI/Onboarding`.
- Fonts: Archivo and Martian Mono variable fonts from google/fonts (OFL), Latin-subset to WOFF2.

## Product truth

All copy was checked against the repo (tribes, perks and lines, origin odds and values, districts, venues, in-game news lines). The origin roll uses the game's odds, 80% Exit Liquidity Baby and 20% Insider Baby; Seeker Baby is omitted because its weight is 0 in `contract/config/lottery.json`. Things that are built but blocked or unproven are not promised: casino games, voice between players, top-ups, paid calls and ORE staking. Market Mood shows SOL's real 24 hour change from CoinGecko, which is what the game's Market Mood follows.

## Checked

- Desktop 1402 px (Claude in Chrome) and a 390 px phone emulation (chrome-devtools).
- No console errors and no horizontal scroll.
- Lighthouse mobile: Accessibility 100, Best Practices 100, SEO 100.
- Reduced motion: motion paused, no boot screen, no bubbles.
- Not checked: physical devices, Safari or iOS, and frame-rate on low-end phones.
