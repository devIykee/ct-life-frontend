# Player cards: recovered direction and visual references

Research date: 2026-10-12. No UI or gameplay files changed by this research.

**[Inference] Design recommendation:** Use the user's Storm Breakers attachment as the main composition reference for the **player identity card**, adapted to landscape with the actual selected avatar. The original request for that card predates Hermes's origin-lottery redesign. The player identity card and the final origin result are two different things; improving one does not fulfil both requests.

## What the user actually said

### Claude: explicit player identity card request

2026-10-09, 03:29:12 Africa/Lagos (02:29:12 UTC). Source: `../ct-life-evidence/claude-recovery-20261009/main/dialogue/part-023.txt`, main transcript source line 8782, local text approximately lines 232–270.

> I'd come back to that meet fit chekc screen. so basically, it's a look of how yourself ba? and it can be a card.
>
> instead of containers like that? and then that I'm 18 or older can be normal text with the check box as it has normally.... ?

In the same message:

> then I want that color to be white(that one that wants to look creamish or you wanted it to be comical? nah!). and that your green, hope it's not because I'm Nigerian you're doing it

> pick two traits screen, please, take icons from icons8 or use chatgpt to generate thos icon images... that one thereis bland

> attached image of something that I think is way better, maybe you do something in our own theme

The attached image was a **Cartoon Buttons Set**: glossy, chunky yellow buttons with deep red lower edges, white highlights and simple pictograms. Inspected at `../ct-life-evidence/art-direction/refs/18-user-ref-cartoon-buttons.png`. Original extracted copy: `/tmp/ct-claude-read-20261009/images/main-8782-1.jpeg`. This supports dimensional, authored icon art in CT Life's theme; it is not a request to copy yellow/red or to place every field in a button.

Claude's response at main transcript line 8829 described one player card containing character, name, tribe badge, origin, dream and trait icons. **That exact field list is Claude's interpretation**, not seven separately stated user requirements. The current creator-before-lottery order means the origin is not yet known at first confirmation.

### Hermes: seven game-card / character-selection attachments

2026-10-10, 01:31:22 Africa/Lagos. Source: `/Users/mac/.hermes/state.db`, session `20261009_225051_144de5`, user message **495**. Message 708 at 01:32:04 repeats the request without the attachment paths.

> so basically yeah, I think you should work on that game card ui.
>
> it doesn't exactly look aesthetic
>
> I've attached example image concepts as context as to fine game cards, the only problem with those game cards is that they'd not work for us, as we're in landscape, and most of them are portrait, but you can learn from it, and not do that rubbish that you basically did

On character selection in the same message:

> those two buttons for full model view, and face model view, should be at the left. it's for the model, not for the character
>
> everything doesn't have to be in a card! that card is taking so much space
>
> you can see that that pick your character screen. those characters containers make it touch the next button

2026-10-10, 08:09:42 Africa/Lagos, message **2527**:

> waiting on my reference image in what context? how did claude generate the reference images before? I thought you know everything already

The attachments were present locally. Hermes initially said they were missing, then recovered them in tool call 2545. No additional independent card-reference attachments were found in this CT Life session's actual user messages. Generated compacted summaries were excluded as user evidence.

## Every relevant Hermes attachment, inspected

All seven originals are in `/Users/mac/Library/Application Support/Hermes/composer-images/`. They were visually inspected, including the blank attachment.

| Original file | Visible content | Specific transferable quality |
|---|---|---|
| `image_da4d7d.png` | **Storm Breakers** trading cards, NextDesigns. Black rounded card shells; cyan, yellow, magenta or red graphic art fields; large characters; bold white names; small rarity/type marks; footer stats. Presentation includes card backs and stacked cards. | Main reference. Character silhouette dominates. A small set of consistently positioned identity marks gives a collectible object its structure. No dashboard of bordered rows. |
| `image_3734ac.png` | Tiny black image. | No usable visible reference. Do not infer content. |
| `image_11f110.png` | **Thunder Thursday Tournament**. Zeus-like cartoon character in a blue cloud scene; thick white rim; folded yellow ribbon crossing the upper edge; foreground coins/dice/pillars; large promotional title. | Depth through silhouette overlap and one strong ribbon. Integrated art fills the card. Promotional copy density is not suitable for a player identity card. |
| `image_b51c7a.png` | Two illustrated reward/shop cards. Thick yellow rims; character and props break upper edge; art occupies most of each card; small benefit icons sit over the art; price CTA at bottom. | Art can be the body of the card, with compact metadata. The shop prices and offers are not onboarding features. |
| `image_7406a5.png` | Landscape **Character Select**. Five full characters; central dog in a blue lit tube; title upper left; currency upper right; selected name bottom centre; Back/Launch in opposite bottom corners. | Character selection has a clear central selected model and separated navigation. Do not turn the whole selection screen into one giant card. |
| `image_39579e.png` | **Characters / Avatars** board with seven large faces in individually framed portrait tiles on a navy/cream presentation. | Readable faces, distinct silhouettes and expressive portraits. Its cream presentation colour does not override the user's explicit white instruction. |
| `image_abeab5.png` | **Collect Managers** grid. Names including Elizabeth, Chester, Butch and Jen; large cartoon faces; rarity-coloured rims; profession icons; green Max Level ribbons/stars. | Name, face and one or two meaningful identity marks remain readable at thumbnail scale. This screenshot's game/creator provenance is unverified. |

The user did not explicitly identify only the origin card in message 495. The “pick character” comments are separately labelled within that message. Earlier Claude wording is the direct evidence for the player identity card.

## What Hermes actually implemented

Hermes assistant message **2790** described its output as “The game card (origin lottery, screens 1–2)”: larger portrait origin cards, rarity ribbons, and a winning card opening sideways into a landscape result with art, outcome name/story and cash/home/debt.

Commit `49937420` is `feat(onboarding): origin lottery as real game cards — rarity ribbons, landscape origin card`; it changes lottery code/styles/capture/text. It does not add new card art. Hermes said the stronger pop-out character effect would need new cutout art. That was proposed work, not a completed or user-approved result.

Earlier evidence report: `../ct-life-evidence/onboarding-reorder-20261011/hermes-card-context.md`.

| Surface | Identity and data | Current source |
|---|---|---|
| Player identity / fit-check card | The player's actual chosen/customized avatar, alias, tribe, dream and two traits. Origin only after it is known. | `client/Assets/Scripts/UI/Screens/CharacterCreatorScreen.cs`, `BuildConfirmPage()` |
| Automatic lottery / origin reveal | Server-returned origin, its story and starting cash/home/debt. Insider Baby and Exit Liquidity Baby are origin illustrations, not the chosen player. | `client/Assets/Scripts/UI/Screens/LotteryScreen.cs` |

Historical identity screenshot inspected: `../ct-life-evidence/hermes-20261010/device-walk2/10-player-card-18.png`. It shows a purple header strip, narrow portrait column and wide white table of labelled origin/dream/traits rows. **[Inference] It implements the one-card concept literally but does not carry the art-led composition of the supplied references: the chosen character is secondary to the table.** This is a historical capture, not a claim about today's running build.

## Pinterest and primary game references checked now

### The exact user-selected visual anchor

Current search recovered [Storm Breakers — Trading Cards, by Nukul Solanki / NextDesigns](https://www.behance.net/gallery/204584997/Storm-Breakers-Trading-Cards) and [the matching Pinterest pin](https://www.pinterest.com/pin/310537336829826715/). The creator page returned HTTP 403 through the research tool; Pinterest's direct page was inaccessible. Therefore creator attribution was located in search results and the supplied image, but **the current Pinterest and Behance page bodies were not inspected**. The complete original attachment was inspected directly and is the evidence for the visual analysis above. This is a portfolio card concept; it has not been verified as a shipped game.

Also inspected the user's historical Pinterest screenshots `../ct-life-evidence/art-direction/refs/09-pinterest-board-a.png` and `10-pinterest-board-b.png`. They show bright cartoon game UI, dimensional controls, illustrated characters, saturated accents and simple major actions. The browser address visible in both is `pinterest.com/pin/437552920045268493/`. They support broad art direction; they are not a single specific player-card design.

### Actual shipped-game identity cards, official sources

1. [Pokémon Sword and Shield: League Card](https://swordshield.pokemon.com/en-au/gameplay/league-card/). Official page opened. Players customize card background, frame, expression and pose; the cards also appear in Link Battles. Official screenshots were downloaded and inspected: `../ct-life-evidence/player-cards-20261012/references/pokemon-card-maker.jpg` and `pokemon-league-card-5.jpg`. The character occupies the card's main area; the battle presentation places names in a decisive bottom band. **[Inference] Use this as a functional precedent for a card that actually represents the selected player, not as a reason to add card trading or battle UI.**

2. [Final Fantasy XIV: Adventurer Plate](https://na.finalfantasyxiv.com/uiguide/callingcard/ap/ap_how_to.html). Official guide opened; it describes personalized character calling cards with portraits, profile information and visual customization. Official screenshot inspected at `../ct-life-evidence/player-cards-20261012/references/ffxiv-adventurer-plate-1.jpg`: a landscape plate with portrait and metadata treated as one authored composition; thematic decoration crosses internal boundaries. **[Inference] This is useful evidence that a player card can work in landscape; its dense MMORPG typography and grey panels should not become CT Life's aesthetic.**

3. [Animal Crossing: Pocket Camp Complete — Camper Cards](https://faq.complete.ac-pocketcamp.com/hc/en-us/articles/33978722886425-About-Camper-Cards). Official support page opened, updated December 5, 2025. It describes player-introduction cards with customized backgrounds and poses. No card screenshot was exposed in the page text, and direct HTML retrieval returned 403; **no visual analysis of that card is claimed**. Its sharing mechanism is outside this onboarding change.

Downloaded official screenshots are research evidence only, outside game resources. They are not assets to ship.

## Concrete design brief

**[Inference] The following is a recommendation derived from the user's words and inspected images, not a claim that the user approved every implementation detail.**

Use **Storm Breakers as the one composition anchor**, translated into the existing white/plum/green CT Life language and landscape. Borrow its hierarchy, large silhouette and concise identity markings. Do not blend all references into a new collection of unrelated boxes.

- Make the actual selected character the focal point of a single landscape collectible. Give its head/upper body substantial visual area and a deliberate pose/crop. Let the silhouette cross a graphic colour field or an internal boundary where technically clean; do not hide the face behind labels. The current narrow ID-photo column is the part to reconsider.
- Use the player's alias as the largest text, with one compact tribe badge/name nearby. Put the chosen dream in one short line and the two trait icons/names in a consistent footer area. Remove the full-width administrative header and unnecessary repeated field captions. All edit targets still need to work.
- Keep white page/card information surfaces where the existing UI uses them. Use the established plum/green and authored glossy icons as controlled accents. The black Storm Breakers shell is not an instruction to replace the user's explicit white direction with a dark theme.
- Keep the ordinary 18+ checkbox/text outside the card, as the user explicitly requested. Place the primary continuation action separately with adequate touch space. Do not add a second consent container.
- Before the automatic lottery, the identity card must not invent an origin, rarity, balance or stats. Hide unknown origin data cleanly. After the roll, the result card can show only the actual returned outcome and its starting resources.
- The final origin reveal should be a sibling composition: large authored origin art, one outcome title, a short useful description, compact cash/home/debt information and one continuation action. The origin illustration must not silently replace the player's chosen avatar on their identity card.
- Treat motion as a brief presentation of that card: arrival, reveal/settle, then readability. No indefinite shimmer or repeated animation that competes with the selected character. Respect the existing reduced-motion setting.
- Validate on the Seeker in landscape with long aliases, long dream labels, both human and meme avatars, two long trait labels, a known origin and the pre-roll state. The next button and checkbox must remain fully accessible without overlapping the card.

### Reuse / asset inventory

All paths below are relative to the repository; verified present when researched.

| Need | Existing source | Limitation |
|---|---|---|
| Chosen player appearance | `CharacterCreatorScreen` already assigns `_preview.Texture` to `_cardPortrait`. | Prefer this actual customized avatar over a generated generic person; adjust rendering/framing rather than discarding customization. |
| Static face thumbnails | `client/Assets/Resources/UI/Customizer/Portraits/head_*.png` | Fixed thumbnails may not reflect changed hair/clothes/skin; useful for selection, not automatically authoritative for the final player. |
| Tribe identity art | `client/Assets/Resources/UI/Onboarding/tribe-{anon,builder,perp_degen,caller,airdrop_farmer,prediction_shark,trencher}.png` | Already authored circular badges. |
| Two trait icons | `client/Assets/Resources/UI/Kit/Icons/{diamond_hands,tipster,deep_pockets,fast_talker,early_bird,rug_veteran}.png` | Use the actual selected traits and existing mappings. |
| Origin illustrations | `client/Assets/Resources/UI/Onboarding/origin-insider_baby.png`, `origin-exit_liquidity_baby.png`, `origin-card-back.png` | These are origin art. No Seeker-specific front image was present in this directory. |
| Supporting resource icons | `client/Assets/Resources/UI/Kit/Icons/{cash,debt,energy,sanity,check,back}.png` | Reuse where the data is actually shown; do not invent card stats to fill space. |

No source art, user references, Claude/Hermes work, or implementation files were deleted or replaced by this research.

### Follow-up: live Pinterest browser inspection

The coordinator subsequently opened https://www.pinterest.com/pin/310537336829826715/ in the signed-in Chrome browser and inspected both its rendered image and page text. It shows the Storm Breakers Archer Queen card, the family of matching character cards below it, and credits Nukul Solanki / Behance. The visible cards use a dominant character silhouette, a graphic colour window, a short name and a small aligned footer. This supersedes the earlier direct-fetch limitation for the Pinterest page (the Behance page itself remains uninspected).
