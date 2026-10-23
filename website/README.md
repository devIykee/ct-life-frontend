# CT Life website

The 9 October 2026 local candidate uses [Marathon](https://www.marathonthegame.com/) as its single primary composition reference. The complete reference was inspected on desktop and mobile. This is a new design candidate, not a user-approved final design.

The previous 8 October campaign was rejected and remains at `campaign.html` for historical comparison. Its layout and artwork are not the approved baseline.

## Preview

```sh
python3 -m http.server 4178 --bind 127.0.0.1 --directory website
```

Open http://127.0.0.1:4178/ . The dated entry is http://127.0.0.1:4178/reference-01.html . This is a dependency-free static website; no package install or build step is required.

## Deployment

Hosted on Vercel (project `ctlife`) at https://ctlife.fun, with `www.ctlife.fun` redirecting to it. The project is linked to this GitHub repository with root directory `website`: pushes to `dev` deploy to production and other branches get preview deployments.

The Android build is too large for a Vercel deployment, so it is attached to the `ctlife-v1` GitHub release. `vercel.json` redirects `/ctlife-v1.apk` to that release asset; to ship a new build, upload it to a release and update the redirect destination.

## Current files

- `index.html` / `reference-01.html`: matching local entry points.
- `reference-01.css`: black, white and Solana-green composition, responsive type, attached information panels, current-build viewer and dialog styling.
- `reference-01.js`: availability dialog, keyboard-operated build selector, loading/failure/retry and image decoding.
- `reference-motion.js`: event-driven scroll motion, normal-flow typography entry and full-width city image reveal. No continuous frame loop, scroll interception or animation dependency. Pause and system reduced motion restore static presentation.
- `campaign-preview.js` / `.css`: retained current-build gallery behavior, restyled by the new page. Native modal with image error/retry, focus return and keyboard navigation.
- `reference-credits.html`: source and artwork explanation.
- `assets/reference-20261009/`: two original generated scenes in responsive WebP sizes, plus full prompts/provenance. Original PNGs are outside the repository in sibling `ct-life-evidence/website/reference-20261009/`.

## Reference translation

The opening uses an image stage, a tall identity rail and an attached information block. Oversized type leads to a morning city scene, offset editorial panel, selectable real-build captures and a ruled FAQ/footer. Mobile places the headline under the scene, uses the horizontal header identity and keeps world imagery in a full landscape frame.

The exact headline is “Welcome to Crypto Twitter”; the main CTA is “Get the game”. The approved CT mark is unchanged. No Marathon images, brand assets, fonts, copy or source code are used.

## Product truth

The studio and morning-city illustrations are concept art. Actual Seeker captures retain their development/sample-data labels. They do not demonstrate live multiplayer. Clip Cut is a local practice round, with no posting or payout.

The "Get the game" buttons download the current development build from `/ctlife-v1.apk`. The capture link opens the actual image without JavaScript, and inactive JS-only controls are hidden.

No invented player counts, endorsements, release date, or real trading returns. No analytics, external fonts, signup, wallet or payment flow.

## Review and limits

See `../docs/design/website-reference-20261009.md` for reference mapping, independent visual criticism and corrections. Browser checks use isolated Chromium desktop and mobile emulation. They do not establish physical-device or Safari performance, game quality or user design approval. No Unity/game/backend/audio code was changed by this website task.
