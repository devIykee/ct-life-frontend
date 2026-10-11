# Website restoration to opening 04

7 October 2026. Local preview only.

The user clarified that “undo the last three changes” means restore the page immediately before the message asking for cleaner styling, black text, a transparent logo and removal of the CT LIFE wordmark. Opening 04 is now the active baseline.

## Restored

Saved opening-04 HTML in both current entry points; Lilita display type and earlier purple/gray text; selected black-backed CT logo and CT LIFE wordmark; original header navigation and mobile menu; “Explore the game” story anchor; original three-card mobile arrangement and count caption; footer divider and story link. The second section's original font, colors and chart markup are also restored. Its story fragment is synchronized with the page.

The outgoing opening07 candidate and its exact live styles/script were preserved as a separate archive. Opening04 now also has separate archived stylesheets. Generated artwork and later fonts remain retained but are not loaded by the restored hero.

## Recovery evidence and limit

The two original stylesheets were recovered from complete, untruncated pre-opening05 file reads in the design-review subagent's tool history. This is recovered source text, rather than screenshot-based reconstruction. No historical hashes exist to certify byte identity. The newly recorded source hashes identify this restoration only. The recovery notes are preserved below.

## Verification

- Live Chrome desktop and 390px mobile inspection showed the restored headline font/colors, header, three cards, caption and divider. All three hero images loaded; no document-level horizontal overflow was observed at these widths.
- The mobile menu opened and its “The game” link reached the second section and closed the menu.
- Selecting Trade showed only the market panel and updated the mobile description. ArrowRight selected Space; Home returned to Work. The chart accent computed to the original purple.
- Both review entry points are byte-identical. IDs are unique; referenced files, fonts and anchors resolve locally. Current page copy contains no em dash.
- `node --check` passed for `hero-03.js` and `day-story.js`.
- Evidence: `website/draft-01/evidence/restored-opening-04-desktop.jpg`, `restored-opening-04-mobile.jpg`, `restored-opening-04-static-checks.json` and `restored-opening-04-source-hashes.json`.

No new dependencies, game changes, remote push or deployment. This restoration does not claim user acceptance of every detail of opening04. The roadmap and current design notes now identify this baseline.

# Opening 04 stylesheet recovery

Recovered on 2026-10-07 by subagent `/root/challenge_direction`, in scratch storage only.

## Source

Both files were transcribed from a complete read-only tool response in this subagent's conversation. The response came from reading `website/draft-01/hero-03.css` followed by `website/draft-01/day-story.css` in `/Users/mac/Documents/codes/projects/ct-life`. It was captured during the preflight for the League Spartan opening 05 revision, before that revision replaced the Lilita styles. The original read was not truncated.

The recovered state includes HeroLilita, the purple/gray ink tokens, the original mint/lavender/blue panels, the hero footer divider, the day-panel minimum heights of 560/550/470px, and the final mobile day-tab flex-column alignment adjustment. These are consistent with the opening 04 progress receipt.

## Limits

No historical hash of either original stylesheet was captured. These files reproduce the captured stylesheet text; byte-for-byte identity with the historical files cannot be independently certified. Any hash taken now identifies this recovery, not the original files. I cannot verify whether any additional edits occurred between the captured read and the opening 05 replacement.

The repository's `opening-04-archive.html` references live CSS and is not a self-contained frozen snapshot. The website directory was untracked when inspected, and no separate opening 04 stylesheet snapshot was found in the website or the relevant temporary directories searched.

This recovery is from source text, not a reconstruction inferred from screenshots. The captured response contains the beginnings and endings of both files, including their final reduced-motion media queries. No omitted segment is known.

## Scope

Only these scratch files were written. No repository file was changed, and no restore has been performed. Relative asset paths in the hero stylesheet still assume placement in `website/draft-01`; this scratch directory is not a standalone preview. HTML, JavaScript, and assets are outside this recovery.
