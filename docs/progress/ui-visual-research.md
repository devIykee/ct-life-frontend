# CT Life visual research — 5 October 2026

**Status:** references inspected before the next presentation implementation. Recommendations below are design judgments, not user approval. The prior beige/forest palette is rejected. This research uses the design-critique framework (hierarchy, interaction clarity, consistency), actual Pinterest images and publisher screenshots—not descriptions alone.

## Five references actually viewed

| Reference | Visible evidence | What CT Life should take |
|---|---|---|
| [inZOI creator, Pinterest / Architectural Digest](https://www.pinterest.com/pin/127508233194989371/) | Full-height adult character dominates a neutral studio. Small utility controls sit at the edges; appearance alternatives are visual thumbnails. The pin itself is low resolution, so precise font/spacing claims would be unsupported. | Character first; quiet peripheral controls. Adapt to a portrait phone rather than shrinking the desktop rails. |
| [Betterfly, Pinterest / Further](https://www.pinterest.com/pin/1548181186834509/) ([original designer](https://further.group/work/betterfly)) | Very shallow currency row, large central dimensional artwork, one concentrated green/lime accent, regular outline navigation. It is a gamified app design, not evidence of open-world gameplay. | Restrained counters and selective dimensional emphasis. Its full-width app tab bar is not appropriate for the live CT world. |
| [NookPhone, Pinterest / Interface In Game](https://www.pinterest.com/pin/440015826099142901/) | Nine distinct, recognizable illustrated app silhouettes in a consistent 3×3 grid; one phone panel leaves the character and world visible. Current selection has a clear label. | A phone should feel like a useful object inside the game. Coherent icon family, consistent icon cells, short labels, visible city around it. Its childish art/proportions are not the character direction. |
| [Life Makeover, Archosaur publisher screenshots](https://store.steampowered.com/app/2626940/Life_Makeover/) | Inspected the original contact/makeup screenshot: face occupies most of the height, controls flank rather than cover it, alternatives use real visual samples, one obvious Save action. It is a landscape PC screenshot of a mobile-origin game—not proof of a portrait layout. | Large face inspection and image-based options. Do not copy its ornamental stars, serif display copy or dense desktop controls. |
| [The Sims 4 phone, EA official update screenshot](https://www.ea.com/fr/games/the-sims/the-sims-4/news/laundry-list-june-23-2026) | Consistent rounded-square app tiles contain specific illustrated objects; utilities sit separately at the top; page dots distinguish navigation from apps. The screenshot is an actual phone UI, not a marketing mockup. | Separate app choices from phone utilities. Use a single sourced icon family; avoid arbitrary per-app accent colors or identical generic chart symbols. |

Also viewed a Pinterest subculture RPG concept: its angular purple/lime panels, promotional banners and multiple competing rails are too visually busy for this task. Nintendo's Pocket Camp Complete publisher image was inspected but contained no HUD, so it is not used as UI evidence.

## Recommended direction

- **Game leads:** enlarge the visible character and city. Keep cash/location/status small at the upper edge. Put phone/map in a compact corner cluster; chat, emotes and mic stay reachable without becoming a full-width tab bar. All current actions remain accessible.
- **Character studio:** a continuous neutral stage with the real imported character. Body/face toggle, clear selected look and thumb-reachable confirmation. Show only appearance options backed by actual assets; creation remains gated by live accepted IDs.
- **Type:** bundled Manrope regular/semibold, moderate headings, sentence-case utility labels. Reserve a single large line for genuine entry/reveal moments. Stop repeating oversized bold headings, long motivational subtitles and all-caps eyebrows on every section.
- **Palette:** mineral/offwhite, deep ink/aubergine, one lime accent for primary/selected states. This is a proposed CT identity, not a palette extracted from one reference. Positive/negative financial colors remain semantic.
- **Icons:** one verified sourced family with consistent weight and optical size. Phosphor regular is the proposed implementation source; label phone apps. Small glyphs still sit in at least 44 logical-dp touch targets.
- **Phone/detail/sign-in:** common type scale, spacing and icon grammar; reduce nested cards. The phone grid, list/detail sheets and sign-in should read as parts of the same game.
- **Validate on device:** model visibility, face/body framing, world-touch pass-through, long labels, safe areas and thumb reach. Reference aesthetics do not establish actual Seeker usability or performance.

## Local reference files

Files are for research only; none are licensed for inclusion in the shipped game.

- `/tmp/ct-ui-references-20261005/inzoi-pin.jpg` — low-resolution source pin.
- `/tmp/ct-ui-references-20261005/betterfly.jpg` — source pin image.
- `/tmp/ct-ui-references-20261005/nookphone.jpg` — image downloaded through the opened Pinterest pin.
- `/tmp/ct-ui-references-20261005/life-makeover.jpg` — publisher Steam screenshot.
- `/tmp/ct-ui-references-20261005/sims4-phone.png` — EA-hosted phone screenshot.

Chrome Pinterest was inspected read-only in the agent's own tab. No pins were saved to an account, no account was created and no purchase was made. Audio review is owned by the coordinating agent; this document makes no claim of listening tests.
