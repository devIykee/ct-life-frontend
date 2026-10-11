# CT Life website campaign, 8 October 2026

> Status, 9 October 2026: the user rejected this delivered website direction as falling short of the brief. Functional test results below remain historical evidence; they do not establish design acceptance. This page is a rejected iteration, not the approved visual baseline.


This is the implemented website direction, not user approval of the artwork or a game-release acceptance. Design rationale below is judgment, not measured audience preference or a promise of virality. The latest user instruction for a polished website supersedes the older companion-site styling direction for this website only.

## Brief and source boundaries

The user asked for an original, elegant, very clean CT Life website, with intentional animation, better cultural copy and proactive agent orchestration. The user supplied [Designeer](https://www.designeer.xyz/) as a craft reference. The approved glossy CT mark and “Get the game” CTA were retained. Main gameplay remains landscape; this work does not alter the Unity client.

The exported Claude build spec, contract, current brief and recorded game evidence were reviewed. The complete Claude conversation titled “CT lifestyle simulation game for solana mobile” was not available. A request for its URL remains optional; no claim is made that the whole chat was read.

The team divided work into root art direction/implementation, original artwork plus capture viewer, motion/interaction review, and culture/copy/claim review. Existing backend and game changes were left with their owners. The pre-campaign site was backed up before replacement.

## Research actually performed

Designeer was fetched publicly and rendered in isolated desktop/mobile Chromium. It is a resource directory: quiet white canvas, fine dividers, restrained typography, clear hierarchy. Its directory structure was not copied for a game campaign. Public links led to [Interface Craft](https://www.interfacecraft.dev/), [Animations.dev](https://www.animations.dev/), [60fps](https://60fps.design/) and [Codrops](https://tympanus.net/codrops/2024/04/02/on-scroll-expanding-image-animation-within-typography/). Paid course/library content was not accessed. 60fps example descriptions were read, not their embedded recordings. Opus Major's introductory browser screen and Pudgy World's public site were inspected as further references; no claim is made of reviewing their complete gameplay or all animation sequences.

Useful primary motion guidance was [Good vs Great Animations](https://emilkowal.ski/ui/good-vs-great-animations), [You Don't Need Animations](https://emilkowal.ski/ui/you-dont-need-animations), [web.dev's animation guide](https://web.dev/articles/animations-guide), [WCAG interaction animation guidance](https://www.w3.org/WAI/WCAG22/Understanding/animation-from-interactions.html), and the [native dialog API](https://developer.mozilla.org/en-US/docs/Web/API/HTMLDialogElement/showModal). The website uses native scrolling and dialog behavior, short selection feedback and a static reduced-motion presentation. No scroll-jacking, autoplay media or external animation framework was added.

Culture research used [Solana Seeker](https://solanamobile.com/seeker), [Superteam Earn](https://superteam.fun/earn/), [Jupiter's community account](https://discuss.jup.ag/t/nurturing-our-jupiverse-together-catdets-and-cats-of-culture/33158) and [X Spaces help](https://help.x.com/en/using-x/spaces). These support familiar work, community and conversation references; they are not evidence of CT Life demand, completed integrations or player earnings. Solana Mobile's [MONOLITH winners article](https://solanamobile.com/blog/solana-mobile-monolith-hackathon-winners-announced) describes Pumpville, so this campaign avoids a first/only crypto-town claim.

## Direction and copy

“Get a life. A CT life.” is immediately followed by a literal explanation: a 3D multiplayer game for Solana Seeker where people make friends, work jobs, trade made-up coins and build projects together. The design judgment is to foreground people and familiar social behavior instead of token prices or generic feature cards.

The sequence is: character ensemble; activity-led interactive city guide; seven CT personality cards with copyable bios; everyday life at home; actual-build viewer; practical questions; availability. Humor is specific but sparse: muted conviction, the builder's final change, the anon's landlord and paying rent while staying bullish.

Copy review corrected a material venue error: café proximity conversation is distinct from hand-raising in Spaces Studio. The social panel now names both. Concrete activity copy is visible before interacting with the guide. Serif emphasis was reduced to three moments rather than repeated in every heading.

Markets use fictional coins and non-withdrawable in-game cash. There are no invented user counts, profits, endorsements, partnerships, download URLs or release dates. Intended multiplayer/project/home language is not completed production acceptance; the availability dialog and FAQ clearly state that there is no public download.

## Art and delivery

The hero ensemble was generated in two iterations. Version 2 deliberately uses a stylized sculpted game-art rendering, rather than the more photographic first version. A new after-hours rooftop scene carries the same cast. Both are original campaign concepts, labeled accordingly. The approved logo was not redesigned. The existing original city diorama was compressed to WebP. Prompts, source PNGs, SHA256s, inspection limits and conversion provenance are preserved under `website/assets/campaign-20261008/`.

The real-build viewer uses actual 2670×1200 Seeker captures dated 7 October 2026: V7 Timeline Square and Clip Cut, and V6 Terminal Café. Embedded development/sample-data labels remain visible. No cropping or content edits were applied; WebP compression was the only conversion. The viewer is explicit that these are local development previews, not live multiplayer proof.

Local fonts were converted to WOFF2 with the already installed `woff2_compress`; original font files and OFL licenses remain. No production package was added. All assets load from the same origin. The three proof images, totaling 765,568 bytes, are not requested until the visitor opens/selects them.

## Corrections made in actual browser review

- Removed desktop/mobile horizontal overflow from transformed hero art.
- Corrected the city image's intrinsic height, then bounded the desktop guide to 680px.
- Preserved the full rooftop composition on desktop after review exposed cropped heads.
- Increased supporting copy, controls and source-label sizes on phones; corrected low-contrast flat-background text pairs.
- Corrected missing spaces where responsive hard line breaks were hidden.
- Canceled active UI Web Animations when manual pause or live OS reduced motion changes.
- Kept focus in the city after the dialog's Explore City action. Ordinary dismissals return to the exact trigger.
- Reviewed gallery error/retry and stale image loading; retry no longer leaves focus on a hidden control.

## Validation and practical limits

The final validation receipt and browser screenshots are in the sibling `ct-life-evidence/website/campaign-20261008/` directory. A full record is retained outside production assets. Validation includes responsive widths, link/image checks, JavaScript-disabled availability, keyboard selectors, real browser clipboard success/denial, touch-input emulation, native dialogs and motion preferences.

The interaction agent passed 29 integrated Chromium assertions, plus targeted motion interruption and corrected navigation-focus retests. The capture viewer passed 37 isolated Chromium checks including 320px portrait and 844px landscape, image retry and late responses. Root then verified its integration and the final active entry point. These counts are specific test coverage, not a complete accessibility certification.

WebKit was attempted but its installed Playwright executable was missing. No browser download was performed. Safari, screen-reader speech and physical Seeker website performance remain untested. The new page was not publicly deployed, and this work does not change backend/game completion status. The lab performance receipt reports its explicit throttling and cannot establish field performance or conversion.

### Final lab and enlarged-text follow-up

After responsive WebP variants were added, the same single-run mobile lab measured LCP 2.048s (previous run 3.852s), CLS 0 and 648,856 transferred bytes (previous run 1,211,659). Conditions were Chromium, 390×844, cold cache, 4× CPU slowdown, 1.6 Mbps and 150ms latency against localhost. This is one controlled comparison, not field performance. Two long tasks remained, totaling 295ms, so this does not establish a frame-rate guarantee.

At doubled computed text size, the hero, headings and personality quotes initially clipped. Header/CTA wrapping, minimum sizing and long-word wrapping were corrected. The final 320/390px enlarged-text run had no lost text outside the deliberately scrollable personality tab strip; all tested controls worked, including selecting its last tab. The detector still reports offscreen strip items before scrolling, which are reachable through touch and keyboard. This approximates text-only enlargement; it is not native Firefox text zoom.

The first root image audit flagged an intentionally empty, unopened gallery image as broken. The fixture was corrected to inspect only images with a source. The final seven-width check then passed with no missing images or page/request errors. A separate visual review exposed the gallery's brief decode gap, which was addressed before final capture.


The final active `/` entry point was exercised after promotion: city and personality selection, gallery open/next/decoded image, Escape, availability modal and manual motion pause passed with zero page errors. The gallery also passed 12 additional controlled decode-delay/rejection/stale-owner checks after its 37-case regression rerun. A real Chromium walkthrough is saved as `ctlife-website-walkthrough.webm`; it is a recording of this local website, not a gameplay video.
