# CT Life website: culture, story and visual direction

6 October 2026 · Research and recommendation, not an approved redesign.

[Inference] This report includes design judgments and proposed copy. Source observations are identified separately. Audience resonance has not been tested. No website or game code was changed for this research.

## Recommendation

[Inference] Build the website around recognizable CT people doing things together. Use clear, mostly flat typography and layout, supported by a small number of expressive 3D scenes and character cutouts. The first production task should be a storyboard and a logo study, not another website implementation.

[Inference] The existing page already has dimensional city art, tactile buttons and tilted character cards. Its central problem is the subject matter: a pleasant city, garment descriptions and development status occupy space that could explain the social game. More 3D by itself would repeat this problem.

## What was actually inspected

- The complete Downloads handoff: `/Users/mac/Downloads/CT-Life-Agent-Handoff-2026-10-06.md`.
- Current workspace brief, README and relevant sections of the product export: [build spec](../spec/build-spec.md), [workspace brief](../AGENTS-BRIEF.md).
- The live [Claude artifact](https://claude.ai/artifact/UbjVYEkz6Us6JtG7vBxz5F#ac0e0a4b-4fa7), including its full Build Spec text.
- The artifact's originating Claude session. The accessible ending contains the user's question about NFTs having their own places and Claude's proposed galleries/clubhouses. Earlier history remained at “Loading...” after scrolling. This is not a complete transcript review. The proposed NFT mechanics were not treated as approved or implemented.
- The local website at `http://127.0.0.1:4178/index.html`, including live hero and district screenshots, page text, source, and the existing `website/evidence/desktop-characters.jpg`.
- The five references attached to the user's request.
- Direct Chrome browsing of X searches and selected individual posts, plus a [Grok research conversation](https://grok.com/c/2d0f66ad-d1a8-446a-9c3d-c2771ed8a4c3). Grok was used for leads; its opinions were not accepted as audience evidence.
- Live visual inspection of Pudgy World, Party Animals, Brawl Stars, Discord, Doodles and Abstract. A second agent opened official reference pages independently. Claynosaurz text was available to the web reader, but both its homepage and media gallery returned a server error in Chrome; its layout was not visually accepted in this review.

No new game builds, financial actions, social posts, deployments or package installs were performed. This was design research, not an implementation or accessibility acceptance test.

## Product truth before the pitch

The [spec](../spec/build-spec.md) defines a native Seeker game: a 3D multiplayer life simulation of a parody Crypto Twitter city. Its intended activities include jobs, fictional memecoin trading, voice rooms, projects, social status, homes and city events. Its seven playable identities are Trencher, Perp Degen, Prediction Shark, Airdrop Farmer, Caller/KOL, Builder and Anon. The city consists of separate venues connected by a map.

The [handoff](/Users/mac/Downloads/CT-Life-Agent-Handoff-2026-10-06.md) records incomplete implementation and integration. It identifies Timeline Square as the only bundled authored venue at that checkpoint and does not establish end-to-end acceptance of multiplayer, wallet authentication, calls, payments, ORE staking or SKR spending. A feature in the spec is not evidence it is currently playable.

[Inference] The website should frame the intended game clearly as in development, then tell its story with concrete scenes. Keep one explicit status frame and identify concept art versus actual development footage. Do not turn every paragraph into a construction update. Preserve ORE and SKR in scope, but introduce them after the visitor understands the game.

## Cultural observations and their limits

This is a purposive qualitative sample, not a census of X or representative market research. The searches themselves bias what appears. Recent posts can be promotional, exaggerated or performative. Seeing a post verifies the statement was posted; it does not verify the author's underlying financial claims.

| Primary source | Observed content | Design hypothesis |
| --- | --- | --- |
| [Superteam Nigeria, 3 Oct](https://x.com/SuperteamNG/status/2106369933550514674) — individual post and replies opened | Startup Village invitation names builders, founders, creators, innovators and researchers. Replies discuss meeting developers, showing up and missing the event because of work. | [Inference] A hacker house, pitch room and friends gathering belong in the cast alongside traders. Social ambition includes building and connection. |
| [Miyagi, 2 Oct](https://x.com/MistaMiyagisWay/status/2106018354565922971) — individual post opened | Prefers a group-chat dynamic over sponsored Spaces and InfoFi posting. | [Inference] Show a small group interacting. The feeling of being in the room can matter more than a huge platform claim. |
| [nwabishop, 6 Oct](https://x.com/nwabishop/status/2107461203815452978) — individual post opened | Uses Lagos Life to criticize hypothetical web3 promotion built around excessive Spaces and fake token-balance screenshots. | [Inference] Lead with the game and evidence of play. Token-heavy spectacle could trigger the exact skepticism the product is trying to overcome. This is one opinion, not consensus. |
| [ZayanXBT, 6 Oct](https://x.com/ZayanXBT/status/2107426722442596743) — full short post visible in X search; detail page later failed | Wants CT to celebrate a shared win and fair allocation rather than one person benefiting while followers lose. | [Inference] Include collective success and belonging, not only predatory insiders and losses. |
| [BuildrJ, 6 Oct](https://x.com/BuildrJ/status/2107476779804004798) — visible search excerpt | Describes traders as both excited and exhausted. | [Inference] The contrast between bravado and fatigue is a useful character/story cue; it is not evidence of a preferred visual style. |
| [William Belfort, 6 Oct](https://x.com/WilliamBelfort_/status/2107459061960577385) — visible search excerpt | Announces departure from the trenches and describes substantial losses while addressing crypto friends. Financial amounts were not independently verified. | [Inference] Loss humor needs humanity. A world consisting only of ridicule and permanent winning would miss part of the emotional range. |

Grok also supplied leads about reply work, Spaces hosting and airdrop fatigue. The attempted [reply-work post](https://x.com/realjolinx07/status/2107002521319268785) returned an X error. Those leads are not independently verified findings here.

[Inference] Do not accept Grok's assertion that neon or mascots inherently signal inauthenticity. Posts about people and phones do not test aesthetic preference. The strongest supported direction is specificity of situations, not a ban on a rendering style.

## Diagnosis of the current website

| Observed element | Assessment | Proposed response |
| --- | --- | --- |
| “The timeline has a new address” followed by “The group chat got a city” | [Inference] Two metaphors postpone the literal explanation. The first line can work as a campaign line. | Put “multiplayer life-sim of Crypto Twitter” immediately underneath or use it as the headline. |
| Peaceful island hero with a café, fountain and small residents | [Inference] It communicates a city, but almost none of CT's particular behavior. | Stage people reacting to the same event, with readable gestures and props. |
| District selector represented by icons and explanatory text | [Inference] The page asks readers to imagine places without showing what happens there. | Give each featured venue one small incident, an image and a plain activity sentence. |
| Everyday/Breeze cards with neutral poses and garment descriptions | [Inference] These are asset studies rather than characters to recognize. | Introduce roles and motives using varied people, expressive poses and concise behavioral lines. |
| Repeated “taking shape,” “planned,” “in development” | [Inference] The building process dominates the narrative. | Keep honest availability labels, but organize the page around the player experience being built. |
| ORE “More than a cameo,” SKR “Part of the city” | [Inference] These sound like reassurance to the project owner instead of an explanation for the visitor. | State the planned function directly in a later platform/economy section. |

[Inference] Preserve the legible hierarchy, spacing, explicit development status and separation between illustrated art and actual screenshots. The current blue/gold palette is not itself proof of failure. Changing colors before changing the story would be arbitrary.

## What the supplied references teach

[Inference] Their common thread is deliberate composition, not exclusively 3D:

1. The yellow/purple arcade poster uses typography and contrast as its main event.
2. The fantasy ensemble shows characters sharing one situation; the viewer can imagine joining them.
3. The three character profiles work through silhouettes, pose, colored backgrounds and concise identity.
4. The Supercell-style card reference lets characters cross card boundaries; the art has physical presence.
5. The trading-card composition makes a cast feel collectible and systematic.

[Inference] Apply those principles to CT's existing roles, not to borrowed fantasy fighters. A builder, caller or anon needs a personality and readable behavior. Merely changing clothes or placing a phone in every hand is insufficient.

## Live website references, with specific uses

These are references for selected decisions, not complete templates to copy. The first six were visually inspected in Chrome on 6 October.

| Website | Specific section | What to borrow for CT Life | Boundary |
| --- | --- | --- | --- |
| [Pudgy World](https://www.pudgyworld.com/) / [quest section](https://www.pudgyworld.com/#quest) | Hero, “Your World / Your Penguins,” missing-Polly quest panel, world tour | [Inference] Best overall narrative reference: invite the visitor into a world, explain activities, then give them a specific situation. The quest panel combines a fictional poster, a short story and gameplay imagery. | Its illustrated hero is evidence that a 3D game does not need an all-3D website. Its audience and tone are different. |
| [Party Animals](https://www.partyanimalsgame.com/) | Hero, cast presentation, bottom character lineup | [Inference] Best reference for expressive 3D characters, physical title treatment, scenes of characters interacting and a footer that still belongs to the game. | Borrow staging and personality, not animals or the costume. Some scrolled transitions briefly appeared blank during this visit; do not imitate the motion behavior unquestioningly. |
| [Brawl Stars](https://supercell.com/en/games/brawlstars/) | Ensemble hero and “3V3 and Battle Royale” explanation below | [Inference] A small cast shares one action and one setting. The next section names the format plainly. | The supplied Supercell-card image was not verified as the current official site. This is the live official reference. |
| [Discord](https://discord.com/) | Hero, “Hop in when you're free,” “See who's around to chill” | [Inference] Best social-copy reference. Explain what friends actually do, then show the interface supporting it. | Do not copy its broad platform scope or claim CT Life's planned voice features are ready. |
| [Doodles](https://www.doodles.app/) | Current Toy Factory hero, “Animation & Stories,” toy lineup, sparse footer | [Inference] Expressive artwork can live inside quiet navigation and generous space. The wordmark remains recognizable beside different art treatments. Useful for artwork versus interface balance. | Its homepage currently leads with Toy Factory. Older Doodles screenshots are not the current site. Its general company copy is not a model for CT Life's explanation. |
| [Abstract](https://abs.xyz/) | Hero and navigation | [Inference] A restraint reference: short hierarchy, calm negative space and a vivid image do not require every element to be loud. | Its current audience is builders and its product is infrastructure. Borrow composition only; this is not the overall CT Life direction. |
| [Claynosaurz](https://www.claynosaurz.com/) / [media gallery](https://www.claynosaurz.com/media) | Cast and concept-art/illustration/video grouping | [Inference] Secondary art-development reference for consistent characters across media. | Official text opened through the research tool; Chrome returned a server-side error on both pages. No full visual-layout claim is made. |

[Inference] Recommended combination: Pudgy World's sequence, Party Animals' character staging, Discord's explanatory discipline, and Doodles' quiet supporting layout. This is an allocation of design jobs, not permission to collage four brands together.

The scout also found reasons to exclude familiar recommendations: [Zentry](https://zentry.com/) currently leads with Human-Agentic OS language, and [Nifty Island](https://www.niftyisland.com/) foregrounds wagering/prizes. [Inference] Their current positioning would pull this brief toward either abstraction or casino signaling. They are not recommended foundations.

## Proposed story before artwork production

[Inference] Use a short day-in-the-city narrative. Each beat should name an action, show a relationship and advance the visitor's understanding. These scenes describe planned scope, not current gameplay acceptance.

| Beat | Story content | Visual task |
| --- | --- | --- |
| 1. Understand the game | Crypto Twitter becomes a multiplayer life-sim for Seeker. | One central social scene; clear literal descriptor. |
| 2. Recognize yourself | Introduce a few of the approved tribes, then make the others discoverable. | Character cards with pose, motive and role; varied demographics. |
| 3. Live a little CT | Work a mod shift, follow a questionable fictional call, react with friends. | Two or three sequential moments, not an unrelated feature grid. |
| 4. There is more than the chart | Build a project, join a Space, go home, show what your character cares about. | A warm gathering with personality and traces of city life. |
| 5. See the actual game | Development footage and a concise explanation of what is available versus planned. | Clearly labeled real capture. |
| 6. Leave with the world in mind | A supported next action, platform information and essential links. | Cast along the footer edge; restrained utility navigation. |

[Inference] Initial key-art concept: a late café gathering. A caller enthusiastically points at a fictional chart, one friend reacts, a builder turns a laptop to show a demo, and an anon observes. Introduce one readable incident, not all seven tribes and 28 NPCs simultaneously. Use adult proportions and soft stylization consistent with the current user brief. CT props must be legible at phone size.

[Inference] The exact public CTA remains unresolved: no verified release URL, official community destination or functioning waitlist was supplied. A story preview can be designed now; “Play now,” a release date or a waitlist promise should wait for a real destination.

## Copy direction to test

This is draft copy for the intended product under an explicit development label, not approved publication copy:

> **Crypto Twitter, as a multiplayer life-sim.**
>
> CT Life is a 3D game in development for Solana Seeker. Work a mod shift, trade fictional memecoins, build a project with friends, and hang out in Spaces around CT City.

> The markets use Game Cash, which cannot be withdrawn. Real-wallet features are separate.

[Inference] A more playful headline can sit above the literal explanation, but it should earn its place after the definition is understood. Prefer concrete verbs and situations to “a new era,” “limitless possibilities,” “make a little chaos” or repeated “your story” phrasing. Keep CT slang where the joke requires it; do not use slang as the explanation itself.

## 3D decision

[Inference] Use 3D where it communicates acting, relationships, movement or a place to inhabit. Keep navigation, text, status information and ordinary controls simple. Start with still compositions and only animate beats that improve the story.

[Inference] Use 2D illustration if the available 3D characters cannot produce a convincing expression or pose. A strong drawing can tell the situation better than a stiff model. No evidence gathered here proves one medium converts better for CT Life. The appropriate comparison is the same scene and copy rendered in both styles, not a polished 3D image against a weak 2D placeholder.

[Inference] “For the streets” should not automatically become grime, graffiti, identical hoodies or a male-only trader cast. Cultural recognition should come from the relationships and situations rather than a costume applied to everyone.

## Logo and branding research

### User correction after the first icon studies — 6 October 2026

The user explicitly rejected a CT lettermark for the app icon, especially the chunky orange/cream monogram studies. The earlier monogram recommendation and Pinterest monogram query below are superseded. The user wants a symbol or character that represents CT culture, informed by closer study of App Icons and actual game app icons. No replacement identity has been approved.

Subsequent visual review covered roughly twenty icons on [App Icons](https://www.appicons.store/), including cropped character faces, abstract smiling mascots, graphic eyes, and tactile object symbols. A [purple mascot detail](https://www.appicons.store/store-chtVZ/p/6pr69gsewm888gx9kjzrhgykajbg4g) was opened individually. Actual icon images from the official Google Play listings were downloaded and visually inspected for [Brawl Stars](https://play.google.com/store/apps/details?id=com.supercell.brawlstars), [Among Us](https://play.google.com/store/apps/details?id=com.innersloth.spacemafia), [Clash Royale](https://play.google.com/store/apps/details?id=com.supercell.clashroyale), [Toca Boca World](https://play.google.com/store/apps/details?id=com.tocaboca.tocalifeworld), and [PK XD](https://play.google.com/store/apps/details?id=com.movile.playkids.pkxd). These are a purposive visual sample, not evidence of icon performance.

[Inference] Character expression and a recognizable main subject offer a stronger starting point than initials for this brief. Two preview-only image concepts were generated: an expressive anonymous persona in a contemporary hoodie, connected to the specified Anon identity; and an asymmetric talking-bubble character, connected to replies and social interaction. The first was refined to remove a fantasy-style hood. The anonymous persona risks overemphasizing one tribe; the talking bubble risks looking like a messaging app. Neither has passed audience testing or production icon-size validation. The prior blanket exclusion of mascots/faces was unjustified.

The older proposal below is retained as research history, not current direction.

[Inference] Start with a custom CT LIFE wordmark. Give it a confident, compact silhouette with a few distinctive letter decisions. Test it in solid black before adding dimensional extrusion or shine. A compact CT monogram can be explored as the app/social mark, but it should not require a skyline, candle chart, speech bubble and coin all at once.

[Inference] The current tilted yellow CT tile is an implemented mark, not evidence of an approved identity. A logo study should compare wordmark concepts independently from the hero illustration. Avoid choosing an unrelated animal just because several references have successful mascots.

Pinterest searches — exact phrases to paste:

| Board | Searches | What to collect |
| --- | --- | --- |
| Logo and lettering | `custom condensed game title wordmark`; `playful bold wordmark black white`; `urban culture magazine typography logo`; `interlocking CT monogram negative space` | Distinctive letterforms, compact silhouettes, readable one-color marks. |
| Characters and scenes | `stylized adult character ensemble key art`; `3D character group narrative poster composition`; `game character personality pose sheet`; `character breaking frame card illustration` | Gestures, interaction, silhouettes, lighting and character hierarchy. |
| World and graphic details | `stylized urban cafe diorama`; `fictional city ephemera graphic design`; `game world posters receipts tickets`; `editorial gaming website bold typography color blocking` | Places people inhabit, fictional notices, job flyers, event posters and clear layouts. |

[Inference] Keep the boards separate. Collect roughly 4–6 logo references and 4–6 scene/character references initially. Write what is useful about each pin. A good illustration should not silently choose the logo, palette and whole UI.

## Next research/design checkpoint

[Inference] Before implementation, compare a one-page storyboard, a black-and-white wordmark sheet, and two treatments of the same key-art scene (expressive 3D and graphic 2D). Then test whether a CT participant can explain what the game is, name something they would do, and recognize who the scene is about after a brief look. This is a proposed comprehension test, not evidence that the direction has already passed.

Unresolved: final logo, approved cast treatment, final palette, public CTA, complete original conversation access, audience response, and the launch-state promise. None has been silently decided by this report.
