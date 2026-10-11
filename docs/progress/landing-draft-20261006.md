# CT Life landing-page draft — 6 October 2026

## Current review: hero 03

The user rejected the full first draft, then the full-bleed café hero, and requested one section at a time. The earlier outcome and checks below are historical records, not creative acceptance. Current candidate: `website/draft-01/hero-03.html`, also integrated into the header/hero of `index.html`.

- Reference: the user's supplied Supercell roster composition. Three original full-body character concepts emerge above subordinate colored panels; no café scene in the hero. The headline is one statement: “A life sim for Crypto Twitter”. The user-approved logo remains unchanged.
- Built-in image generation produced the Builder, Trencher and Caller. Original RGBA PNGs, prompt set, source paths and hashes are in `assets/hero-03/`. These are promotional concepts, not changes to game character models. Total image bytes: 5,148,191; responsive production compression remains outstanding.
- Independent visual review found that the panel/figure mechanism now follows the supplied reference. It also identified the Caller's possible singer/MC reading and small concept labels. Labels were enlarged; no further speculative art was commissioned before review.
- Standalone hero inspected at 320, 390, 768, 1024 and default 1468 CSS pixel widths. Narrow-phone character scaling was corrected. No page-level horizontal overflow was observed in the inspected layouts; all hero images loaded. Mobile navigation toggled, Escape closed it and restored focus. Primary CTA reached the existing `#life` section. No captured console errors on the standalone hero.
- `node --check` passed for `hero-03.js` and updated `app.js`. Static local-resource/anchor and duplicate-ID checks passed for standalone and integrated pages. No new dependency, Unity change, commit, push or deployment.
- Lower-page HTML remains byte-for-byte unchanged (SHA-256 `da4eba1f1127515f642c9e39ecae744cb6d201dd64d8772b5b3b4637e28aa1f4`). Navigation still accesses those earlier-draft sections; they remain unapproved and outside this hero iteration.
- Desktop/mobile evidence: `website/draft-01/evidence/hero-03-desktop.png` and `hero-03-mobile.png`. Local review URL: `http://127.0.0.1:49223/draft-01/hero-03.html`. Temporary browser viewport overrides were reset.
- Tooling note: default Python lacked Pillow (`ModuleNotFoundError: No module named 'PIL'`). PNG alpha was inspected read-only with `sips` and a stdlib PNG decoder, then verified on the light browser canvas. No package was installed and no image alpha was edited.

Next: user reviews this hero composition; refine this section before proceeding to the next. Production image delivery, final art and full accessibility/performance validation are still open.

## Outcome

Implemented a new local candidate at `website/draft-01/`, leaving the previous website and game code intact. The user approved the negative-space CT mark during this task; it is used in the navigation, favicon, platform section and footer. The mark's reference provenance is retained.

The page defines the game directly, introduces a planned day in CT City and all seven tribes, uses new generated café ensemble concept art, and separates that artwork from the existing authentic Seeker development screenshots. Three portraits use browser crops of the same ensemble. No fabricated release/download/community/signup destination was added.

The website roadmap is `docs/design/landing-page-roadmap-20261006.md`. It is a website plan, not a replacement or reduction of the game's six phases.

## Verification

- `node --check website/draft-01/app.js`: passed.
- Static HTML local-reference, anchor and duplicate-ID inspection: passed.
- Live Chrome desktop inspection at 1468 × 892, and phone inspection at 390 × 844.
- DOM width checks at 320, 360, 390, 768 and 1468 CSS pixels: no page-level horizontal overflow; no broken loaded images.
- All seven tribe buttons changed the selected state and corresponding text; ArrowRight and End keyboard navigation worked.
- All three capture selections changed the source image. The screenshot dialog opened, Escape closed it and focus returned to its trigger.
- Mobile navigation opened, and selecting a section closed it and moved to the correct anchor. The mobile character-card region also scrolled to the next card with ArrowRight (scrollLeft 295.5px at 390px viewport).
- The Game Cash FAQ disclosure expanded; the credits destination opened.
- Browser review found a repeated wide portrait crop, a clipped mobile strip phrase and an 8px artwork caption. Portraits were tightened, the mobile strip changed to three wrapping columns and the caption increased to 10px.
- No page-owned script errors were observed. Chrome's wallet extension emitted its own listener/orphan-stream warnings; these were outside this website's source.
- Browser screenshots are in `website/draft-01/evidence/`. These show website output, not new gameplay evidence.

This is first-draft verification, not a complete accessibility audit, measured Core Web Vitals run, fresh Seeker gameplay acceptance, public deployment or user approval of the page.

## Art and implementation

Built-in image generation created the café scene. The selected second rendering uses more stylized faces and surfaces than the discarded photographic first rendering. The selected source is preserved in `website/draft-01/assets/cafe-ensemble-source.png`; the delivery JPEG is 525,171 bytes. Generated mark/source files, prompts and hashes are retained in that folder.

No packages installed. Existing local Lilita One and Manrope fonts reused. HTML/CSS/JS are dependency-free. All application interactions remain local to the browser. No commit, push, Unity build, financial action or public deployment performed.

## Preview

Server started with:

```sh
python3 -u -m http.server 0 --bind 127.0.0.1 --directory website
```

The current process printed port `49223`; candidate URL: `http://127.0.0.1:49223/draft-01/`. The server is left running for review. On restart, use the newly printed port.

## Next

Review the draft with the user, then work through the website roadmap. Main outstanding production work: art refinement, better actual footage, an approved real CTA destination, responsive asset variants/performance measurements and final accessibility/publication checks.
