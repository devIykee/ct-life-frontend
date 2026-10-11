# Website opening revision 04

6 October 2026. Local website work only.

## Delivered for review

- The current preview at `website/draft-01/index.html` and its synchronized `hero-03.html` entry point contains two sections only. The earlier full-page draft is preserved at `full-draft-archive.html`.
- Hero headline is exactly “Welcome to Crypto Twitter”, with large Lilita One display lettering. The descriptor is “A 3D multiplayer life sim on Solana.” Removed the eyebrow, availability status, development navigation CTA and decorative CTA arrow. The browser title uses a vertical bar; no em dash remains in current page copy.
- The selected logo is the earlier separate green T/cyan-purple C from the user's attachment. A built-in image-generation edit changes its background to black. Original and edited source, 192px navigation and 48px favicon exports, and the exact edit prompt are in `assets/brand/`. The prior negative-space logo remains archived.
- The new second section is “A day in CT City”, with working Work a shift, Trade the call and Join a Space tabs. It uses HTML/CSS gameplay illustrations, with an SVG fictional chart and portraits from the existing concept cast. No gameplay capture was fabricated. Game Cash is identified as fictional and nonwithdrawable in the market illustration.
- Desktop has a short narrative rail beside one large illustration. Mobile has compact title tabs and one active description. Panels use consistent minimum heights to avoid the observed layout jump.
- Current user decisions and launch intent are recorded in `docs/design/website-current-direction-20261006.md`. The roadmap and preview README were updated. The draft has not been published and has no fabricated download or signup destination.

## Verification

- `node --check` passed for both `hero-03.js` and `day-story.js`.
- Static checks passed: both entry points synchronized, 18 unique IDs each, 19 local references checked, no missing file/anchor, no repeated ID, no removed eyebrow/development text or em dash in the current preview.
- Visual review at the default desktop width of 1468px, phone width 390px and narrow 320px. No page-level horizontal overflow observed; loaded images were valid.
- CTA navigated to the second section. All three story panels rendered. Desktop End selected the last tab; phone ArrowRight and Home worked, active-description text changed, and mobile ARIA orientation was horizontal. Market and Space panels measured 470px high on the phone.
- A final mobile alignment correction uses flex columns for tab numbers and titles. The existing mobile screenshot predates that small alignment-only correction if the attempted recapture timed out; do not claim a new screenshot without inspecting its file timestamp.
- Desktop proof files: `website/draft-01/evidence/opening-04-hero-desktop.png` and `opening-04-work-desktop.png`. The final browser log query returned no errors. Temporary viewport overrides were reset and the main preview tab was left open.
- A shell response stalled but its file writes completed and were inspected through independent Node filesystem access. The delayed shell call eventually returned exit code 0. A browser call timed out and reset its kernel; the existing tab was rebound, desktop viewport restored and final desktop screenshots saved afterward.

No new package, game build, game-model change, commit, push or public deployment. Full production accessibility/performance review and compressed responsive character images remain future work. This receipt does not claim user acceptance of the second section.
