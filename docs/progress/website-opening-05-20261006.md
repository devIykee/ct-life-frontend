# Website opening 05

Superseded: the user rejected this candidate’s unsolicited card-color changes, type and CTA hierarchy. See `website-opening-06-20261006.md` and the current design decisions.

6 October 2026. Local preview: `http://127.0.0.1:49223/draft-01/`.

The user chose a light canvas with stronger game styling. This revision keeps the accepted three-character composition and replaces Lilita One with locally bundled League Spartan at weight 900. All text in the two-section preview now computes to black. Mint, yellow and cyan panels have thin black contours and shallow offset depth; the icon-free button uses the same treatment.

The selected green-T/cyan-purple-C mark now uses a transparent PNG. The CT LIFE header wordmark is removed. One directly visible navigation link, “A day in CT City”, and the “Explore the game” CTA both point to the existing second section. The previous “The game” and “The people” links and mobile menu are removed. Native anchors require no header script.

The second section retains its existing story and interactions; its heading font and text colors follow the new direction. No further sections were built.

## Verification

- Live Chrome inspection at 1468px desktop and 390px phone width, with additional overflow/layout checks at 320px, 768px and 1024px. No horizontal overflow was observed at those widths.
- League Spartan loaded successfully; the hero computed weight is 900. Visible text colors in both current sections computed to `rgb(0, 0, 0)`.
- Both header and hero links reached `#life`. Mobile story selection switched from Work to Trade, then keyboard ArrowRight selected Space. Exactly one panel was visible and the active mobile description updated.
- `node --check website/draft-01/day-story.js` passed. The two preview entry points are byte-identical. Referenced HTML files and anchor targets resolve locally. The current HTML/CSS/JS contains no em dashes.
- The 192px logo PNG has alpha values from 0 to 255, including 19,130 fully transparent pixels out of 36,864. Transparent 192px header and 48px favicon exports are saved alongside the generated source and exact prompt.
- League Spartan's official source, SHA-256 hash and SIL Open Font License are bundled with the font.
- Temporary browser viewport overrides were reset. The final desktop preview tab remains open as the deliverable.

## Evidence and boundaries

- `website/draft-01/evidence/opening-05-hero-desktop.jpg`
- `website/draft-01/evidence/opening-05-work-desktop.jpg`
- `website/draft-01/evidence/opening-05-mobile-full.jpg`
- `website/draft-01/assets/brand/transparent-background-edit.json`
- `website/draft-01/assets/fonts/LeagueSpartan-source.json`

An independent visual review prompted slightly looser headline tracking. The candidate still awaits the user's design review. Character art remains concept artwork and the day panels remain gameplay illustrations. Existing character PNG delivery optimization is outstanding. No public deployment, remote push, game implementation, payment or wallet change was performed. Flutter checks do not apply to this dependency-free static website.

Walrus recall and preference-saving were attempted in this turn but failed with a relayer HTTP 429 connection limit. No successful durable save was reported. The accepted visual decision is recorded in the repository's current design note; that note does not replace Walrus memory.
