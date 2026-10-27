# CT Life quality bar (read before any task; the coordinator rejects work that misses it)

The user has strong design taste and has called out real misses. Every agent works to this bar.

## Names: relatable, never invented
- Use the names people already know (user decision 2026-10-26: real brand names), not made-up words that need explaining.
  Police, not "Sleuth". McDonald's, not "Rekt Burger". Nobody should have to read a description to know what a place or
  thing is.
- Every brand or place name comes from ONE table (`contract/ids/names.json` once it exists), never hard-coded in UI
  strings, so any name can be swapped in one place.
- People don't read. Short labels, no jargon, no lore text where a plain word works.

## UI: one coherent system, clean, smart layouts
- One design system for every dialog, card, sheet and HUD piece. No one-off colours, fonts or layouts per feature.
- The reference style (user's examples, 2026-10-26): a single centred card; a bold display title; a short plain-language
  subtitle; information in a few clearly labelled tiles (e.g. "Duration · 1 min", "Earnings · 100"); one progress row;
  ONE big primary button; a consistent back button. Calm palette, strong contrast, generous spacing, nothing decorative
  that doesn't carry meaning.
- No muddy or clashing colours. Text must be readable over any background. No cluttered or redundant layouts.
- Build UI only from an approved direction or the approved design system. Screenshot every screen on the device and
  compare against it before reporting.

## Art: ready-made, open-source, real
- 3D props, furniture, kitchens, interiors: ready-made CC0/CC-BY models (Sketchfab, Poly Haven, Quaternius, Kenney…),
  vetted for licence + AI/rip provenance. NO greybox boxes or hand-built primitive stand-ins in anything shown to the user.
- Audio: real recordings (CC0/CC-BY). No synthesis.

## Motion: realistic
- Characters walk like people: correct foot spacing (legs never close or cross), no sliding, no stiff poses; transitions
  blend. Camera follow is smooth (damped, no jitter or snapping).
- Verify motion from screen RECORDINGS on the Seeker, frame by frame, never from stills.

## Characters
- NPC characters will be separate models from real-player characters (final builds). Don't couple NPC visuals to the
  player cast.

## Real money where it's real
- Things that actually involve money (coin launches, and the casino if it can be done legally) use real money from the
  player's own wallet, non-custodial. Don't fake what the user wants real; flag the legal constraints with sources.

## Process
- Research first, direction second (refs + brief + user OK), then build. Quality AND speed.
- Never present inferences as facts; label them.
