# CT Life local companion website — 6 October 2026

Status: local implementation and browser review completed; not deployed or user-approved. Owned only `website/` and this report. No Unity, adb, runtime UI, contract or backend changes. No deployment/account/purchase.

## Direction and actual research

The user rejected the earlier restrained/mineral interface. This site carries the later tactile game direction: blue patterned world, outlined gold controls, colorful district medallions, Lilita One display type, Manrope supporting text and original diorama artwork. The confirmed in-game lobby remains in the native app; this is its companion site, not a second web game.

- [Brawl Stars official site](https://supercell.com/en/games/brawlstars/): opened in a separate Chrome tab and viewed its actual hero and video area. Large game artwork and phone evidence precede article/news content. Carousel arrows/pagination remain visually distinct from external video actions; clicked Next, but the captured immediate state did not establish a completed slide transition. No reference artwork copied.
- [Clash Royale official site](https://supercell.com/en/games/clashroyale/): opened through web reader to inspect the product/official media hierarchy. No new live screenshot review is claimed for this page.
- [Palia official site](https://palia.com/): viewed the character/world hero and World of Wonders in live Chrome. Clicked Mining; DOM and screenshot confirmed its description and selected heading changed from Fishing. This grounds the useful district-exploration interaction, rather than decorative tabs with no content changes.
- The seven user-supplied game/site references were directly viewed by root earlier; this agent received its detailed brief. They are not bundled or represented as independently downloaded files.

The design-critique skill is used for hierarchy, usability, consistency and accessibility review. The separate design-taste skill was inspected but not applied: its unrelated telemetry workflow was not run and its generic restrained-color heuristics are not the user's game direction.

## Implemented

- Responsive artwork-led hero and direct section navigation. Main action explores the city guide, with development/public-release status directly underneath.
- Four district tabs with unique content, icons, colors and explicit implemented/planned states; keyboard arrows/Home/End and ARIA tabs.
- Real Everyday/Breeze character studies with a look selector. Two whole looks are not presented as completed modular customization.
- Three unchanged actual Seeker build-L screenshots, labeled development/sample data; native dialog enlargement with Escape, backdrop dismissal, focus restoration and scroll lock.
- Current-client vs planned-feature tabs; ORE and SKR both visibly included without claiming working transactions.
- Explicit standard-free-creation plan vs optional paid-generation proposal; no invented provider, currency or price.
- Native FAQ disclosures; keyboard-accessible mobile navigation; reduced-motion CSS plus manual motion pause.
- All assets local; no packages, third-party embeds, analytics, accounts, payments or deployment. Local server binds only 127.0.0.1:4178.
- Credits page links full bundled OFL/MIT/CC BY notices and records modifications. `assets/manifest.json` preserves source paths and SHA256 for 29 copied files; no candidate with proprietary evaluation restrictions used.

## Verification

- `node --check website/app.js` passed.
- Real browser evidence and limitations follow. Screenshot resemblance alone is not accessibility or behavior verification.


## Actual browser review

The loopback server is running at `http://127.0.0.1:4178/`. Chrome review used a separate agent-created tab. No user tabs were repurposed. Temporary responsive viewport overrides were reset before handoff.

- Desktop **1468×892**, tablet **768×1024**, mobile **390×844** were viewed. A **320×740** narrow layout was also checked. At each tested width, `documentElement.scrollWidth === innerWidth`; no page-level horizontal overflow was observed. The mobile screenshot rail intentionally scrolls horizontally within its own container.
- No broken image elements were found in the inspected page. Local HTML validation passed for linked files, anchors, unique IDs and ARIA relationships on both pages. `node --check` and scoped `git diff --check` passed; CSS braces balance.
- District selection updates title, status, description, icon and color. Keyboard End selected Spaces; arrows/Home/End are implemented with roving tabindex. Look selection updates the copy and selected character frame. Development tabs replace their actual content, preserving explicit planned states.
- Mobile Menu opens/closes, closes after navigation and closes with Escape. The screenshot viewer opened the correct image/title; Escape closed it, restored focus to the originating screenshot button and removed the body scroll lock.
- The Game Cash FAQ opened its actual answer. Manual Pause Motion produced `animation-name:none` and `scroll-behavior:auto`. The OS reduced-motion media rule is implemented; no OS preference was changed to simulate it.
- Credits were opened and inspected at mobile width. Full local Snow/Rain/CC BY/OFL/MIT notices are linked and files exist.
- The app-origin warning/error log filter returned no records. Unrelated MetaMask extension content-script warnings were present in the browser; these are not attributed to the website.

### Corrections made after actual render

- Bounded the desktop island height so the illustration/caption stays composed beside the primary action.
- Added a tablet-specific title/action layout after the first 768px render split the headline and squeezed the CTA.
- Normalized character image stage heights to show head and feet consistently; moved tags into a separate top band after seeing a label cover part of a character's hair at mobile width.
- Added a slightly deeper **readable blue** token for text-bearing hero/footer surfaces. The original app blue remains a brand token. White on the readable-blue base calculates to **5.45:1**; the pattern alpha was reduced. Dark ink on gold calculates to **10.72:1**, dark ink on purple **6.37:1**, muted text on paper **5.20:1**. These sampled calculations are not a full WCAG conformance audit.
- Root's copy review removed internal handoff language from the consumer page. Detailed service/build caveats remain here, in README and the credits. Pre-release status, planned ORE/SKR and sample-data captions remain visible. Free standard creation and optional paid personal generation remain proposals rather than available purchases.

### Evidence

All files are real local browser screenshots, not mockups:

- [Desktop hero](../../website/evidence/desktop-hero.jpg), [desktop full page](../../website/evidence/desktop-full.jpg), [desktop character studies](../../website/evidence/desktop-characters.jpg).
- [Mobile hero](../../website/evidence/mobile-hero.jpg), [mobile full page](../../website/evidence/mobile-full.jpg), [mobile navigation](../../website/evidence/mobile-menu.jpg), [mobile viewer](../../website/evidence/mobile-viewer.jpg), [planned-state panel](../../website/evidence/mobile-status.jpg), [mobile credits](../../website/evidence/mobile-credits.jpg).
- [Tablet hero after correction](../../website/evidence/tablet-hero.jpg).
- Additional district/character snapshots in `website/evidence/` record intermediate interaction checkpoints. Final full-page captures and desktop character capture reflect the later consumer-copy pass.

## Remaining limits / handoff

- This is a local companion website, not a public release, wallet sign-in endpoint or browser game. No deployment, forms, analytics, accounts or money movement were added.
- No real mobile-browser hardware, Safari, screen reader, automated accessibility audit or constrained-network performance test was performed. Responsive evidence is Chrome viewport testing.
- Assets are copied unchanged from the repo; the copied asset set is 11.34 MiB. Large below-fold character/game images lazy-load; further shipping compression is still an optimization task, not a claimed completed performance result.
- The site reflects development build L screenshots. Their actual filenames/date remain in README/credits; later app fixes do not silently become evidence in these older captures.
- Separate Unity stress finding: root subsequently rendered the 20-W alias and found Settings pushed offscreen. The earlier static audit's lack of a confirmed defect did not validate rendered wrapping; root's device evidence supersedes that risk assessment. Root owns the app correction; this website work did not alter Unity.
