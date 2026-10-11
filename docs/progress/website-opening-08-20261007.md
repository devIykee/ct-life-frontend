# Website opening 08

7 October 2026. Local preview only.

The user requested targeted changes after restoration of the earlier visual baseline. This revision removes the hero divider and the named footer slogan/quest action, uses the existing transparent logo/favicon, changes the primary button to “Get the game” with its previously pending destination, and introduces literal draft hero copy.

Headline: “A city game with real people”. Descriptor: “Get a job, trade pretend coins and hang out with friends.” The intended jobs, fictional coins and social city are in the build specification; this local marketing draft does not verify availability of every game system.

A centered “See how you play” native anchor reaches `#life`. Its decorative chevron animates by 4px for three 1.4-second cycles, then stops. The arrow sits beside the label so it is visible in the 390×844 phone review. Reduced-motion CSS removes its animation. The primary acquisition button is separate and has no invented download URL or decorative icon.

The existing Lilita/Manrope fonts, text colors, panel colors and three-character composition remain. The index had intervening class/quest wording that differed from the review alias before this turn. That remaining index copy was preserved, rather than overwritten with the old alias. Both preview pages and the companion story fragment are now synchronized. The before-change page and styles are preserved in `opening-08-before-archive.*`.

## Validation

- Live browser checks at 320, 390, 768, 1024 and 1468px showed no document-level horizontal overflow; headline spans fit and hero images loaded. The scroll link measured 44px high.
- Desktop activation of the scroll link reached `#life` with the second section at the viewport top.
- The accessibility tree reports the “Get the game” button as disabled and “See how you play” as a separate link.
- The logo PNG is 192×192, color type6 (RGBA), and native `sips -g hasAlpha` reports yes. Its rendered background is transparent. The existing asset was used without an image edit.
- Both entry points are byte-identical; IDs are unique and local file/anchor references resolve. Removed copy and em dashes are absent. CSS includes the reduced-motion rule.
- `node --check` passed for the existing header and day-story scripts. No JavaScript logic was changed.
- An initial optional pixel audit could not import Pillow from system Python. Validation was rerun with the standard library and native sips; no package was installed. The replacement check establishes an alpha channel, not a fresh count of transparent pixels.

Current proof: `website/draft-01/evidence/opening-08-hero-desktop.jpg`, `opening-08-hero-mobile.jpg`, `opening-08-responsive-checks.json` and `opening-08-static-checks.json`.

No new dependency, game build, commit, remote push, deployment, wallet or payment change. The new copy remains a review candidate. The real game-acquisition URL remains pending by the prior user decision.
