# CT Life: a social city worth returning to

**6 October 2026 · research and proposed direction, not product approval**

[Inference] This report combines checked sources, direct inspection of the current game image, and design proposals. Proposals below are hypotheses to test; they are not evidence of demand, backend completion, legal clearance, or future virality. The six-phase build spec remains the baseline. ORE and SKR remain included.

## The strongest objection

**“A multiplayer crypto town” is already occupied.** Solana Mobile's 14 July 2026 MONOLITH announcement names Pumpville as a grand-prize winner and describes a native Seeker social multiplayer game with short daily sessions. Pumpville's own current documentation describes resource gathering, crafting, combat, avatar collections and social play. Neither page establishes its retention or revenue. [Solana Mobile announcement](https://solanamobile.com/blog/solana-mobile-monolith-hackathon-winners-announced), [Pumpville documentation](https://pumpville.gitbook.io/docs).

[Inference] CT Life needs a recognisable reason to exist beyond a wallet and attractive buildings: **a playable daily CT sitcom in which your friends, your reputation and the city's ridiculous incidents create the story.** The existing spec already contains the ingredients. They need to be present in the world, with clear consequences and good comedic timing.

## What the current evidence actually shows

I opened the handoff, workspace brief, build spec, contract prose, and the M4 device [city capture](../progress/screens/quality-review-20261006m4/quality-review-20261006m4/city.png). That capture visibly contains extensive empty paving, a small player character, a top status cluster and six large bottom-right action buttons. Its on-screen footer says sample data. It does not demonstrate a populated live server, completed districts, voice quality or enjoyable movement.

[Inference] The presentation problem is composition and activity density as much as asset quality. Bringing in a detailed bridge cannot make an empty square socially interesting. A production scene needs intentional sightlines, destinations, distinctive people and small readable things happening within the same camera view.

The current imported `contract/realtime/messages.ts` accepts six city incident IDs: liquidation, rug, coin launch, mood change, exploit and ORE motherlode. The client already publishes incoming city messages to its local bus. The old toast presenter also contains four additional event cases absent from this imported enum. That is not proof those additional events can arrive. The incident work attached to this research uses only the six imported IDs.

## Sourced findings and their limits

| Checked source | What it supports | What it does not support |
| --- | --- | --- |
| [X Spaces help](https://help.x.com/en/using-x/spaces), opened 6 Oct | Existing Spaces offers public listening, invitations, host-selected speakers, reactions and moderation controls. | That users would migrate their established audience to CT Life. |
| [Roblox retention guidance](https://create.roblox.com/docs/production/analytics/retention), opened 6 Oct | Separate D1/D7/D30 cohorts; fast access to the core loop; progression and social systems as distinct retention work. | Applying Roblox's audience or any numerical retention benchmark to Seeker. |
| [CLOCK IN announcement](https://solanamobile.com/blog/clock-in-the-solana-mobile-hackathon), 8 Sept 2026 | Deadline 8 Oct; functional APK, source, demo and presentation; polish, repeat use and innovation are judging criteria; $10k creative/meaningful SKR integration prize. Winners receive store placement and launch support. | Winning; automatic distribution for all entrants; the exact deadline timezone. The article now says early-November results and a reasonable publication timeframe, not a precise 10 Nov cutoff. |
| [dApp Store submission guide](https://docs.solanamobile.com/dapp-store/submit-new-app), opened 6 Oct | The official publishing route exists. | That this APK is ready, submitted or approved. |
| [LiveKit subscriptions](https://docs.livekit.io/transport/media/subscribe/), opened 6 Oct | Media subscription, active-speaker information and selective subscription are supported concepts. | Working end-to-end proximity audio, privacy boundaries or cost at CT Life's target concurrency. |

## Fresh primary CT sample — 6 October follow-up

**Method and limit.** This follow-up read public X pages in the signed-in Chrome UI on 6 October 2026. It used the requested named accounts plus three community posts already discovered by root; it is a small purposive sample, not a representative survey. Post bodies, links and displayed dates were inspected. No post, reply, follow, like, subscription or message was sent. Family news and unrelated recommendations were excluded. Engagement counters changed during reading and were not used as retention, willingness-to-pay or viral-growth evidence.

The initial date-bounded query used old handles `@0xMert_` and `@aeyakovenko` and returned no matching results for those accounts. X's visible account search identified current `@mert` (profile identifies Helius CEO), while current Toly posts use `@toly`. **Empty results from stale handles were a research error source, not negative evidence about their activity.** The earlier failed browser attempt described in this report has now been superseded by these directly observed posts. This is still not a completed audience study.

| Dated primary observation | What was actually visible | Bounded implication for CT Life |
| --- | --- | --- |
| [Mert, 5 Oct 2026](https://x.com/mert/status/2107025939750334830), dedicated post timestamp 9:31 AM | An arrival at the wrong Singapore hotel is retold using Solana/game vocabulary. Replies extend the mundane mishap into transaction and bridge jokes. | [Inference] A recognisable everyday situation can carry crypto humour without a trading screen. The visible reply chain suggests a format people can extend; it does not prove they will play a game using it. |
| [Mert, 4 Oct](https://x.com/mert/status/2106745924672495856), read on his public profile | Praises technical progress across Solana, Zcash and NEAR rather than one ecosystem alone. | [Inference] Builders and collaboration belong in the cast. Portraying all CT as scammers or tribal price shouters would omit a visible part of this sample. The post's technology claims were not independently benchmarked. |
| [Toly, 6 Oct 2026](https://x.com/toly/status/2107505099086156115), dedicated timestamp 5:15 PM | Dry engineering commentary about Agave gossip is paired with a quoted exaggerated breaking-news treatment of a testnet change. Replies include uncertainty about what the engineering means. | [Inference] Useful comedy can come from a quiet technical improvement receiving absurd ceremony. A legible visual gag needs to work before a player understands the jargon. This source establishes the conversation, not the claimed performance ranking. |
| [Toly, observed 6 Oct](https://x.com/toly/status/2107504838326227252), visible search post marked 35 minutes old | A maple-leaf reaction quotes a tokenized maple-syrup announcement. | [Inference] Physical objects plus financial language offer visual incongruity: an over-serious booth for an ordinary object. This does not verify the quoted product's functionality or demand. |
| [CZ, observed 6 Oct](https://x.com/cz_binance/status/2107486252127969473), visible post marked one hour old | Uses market support/resistance language in response to a follower-count dip. | [Inference] Social status can be the joke without money being the reward. A character anxiously polishing an empty reserved chair is more visually readable than another ticker. |
| [CZ, observed 6 Oct](https://x.com/cz_binance/status/2107188601130045708), visible post marked 21 hours old | Makes a self-deprecating reply about a bullish bias and only slightly better than half accuracy. | [Inference] Let an overconfident fictional character be wrong and recover gracefully. Do not reproduce the line as authorised CZ dialogue or treat it as a verified trading record. |
| [Miyagi, latest edit 2 Oct 2026, 2:47 PM](https://x.com/MistaMiyagisWay/status/2106018354565922971) | One author expresses enjoyment of small group chats and relief from sponsored Spaces/info-fi. | **Counterevidence to the pitch:** [Inference] Some people may want less sponsored broadcasting, not a 3D version of it. One small post cannot establish a community-wide shift. Keep private-feeling friend tables and activities central; test sponsorship tolerance separately. |
| [ETO / Earnathon, 6 Oct 2026, 2:21 PM](https://x.com/nwabishop/status/2107461203815452978) | Criticises a hypothetical web3 version of Lagos Life being promoted through many Spaces and token-balance screenshots rather than usefulness. | **Counterevidence to token-led launch marketing:** [Inference] Show actual movement, decisions and shared play first. This is one person's criticism, not proof that Lagos Life succeeds or that CT Life fails. |
| [Superteam Nigeria, 3 Oct 2026, 2:05 PM](https://x.com/SuperteamNG/status/2106369933550514674) | Announces an Abuja builder/creator gathering for 5–9 October and offers attendee-card generation. A [reply dated 4 Oct](https://x.com/Decentral_Diva/status/2106649455453777958) asks attendees for pictures/video while unable to attend because of work. | [Inference] A named host, time, place and optional identity card are a concrete invitation structure; a recap may serve absent friends too. Neither an announcement nor these replies measures attendance or willingness to migrate into CT Life. |

### Original content directions from those observations

All lines below are original proposals for fictional characters, **not quotes, impersonations, live facts, permission to use a likeness, or implemented server features**. Keep them out of any real-person NPC until that person's participation is agreed.

| Proposal | Short playable situation and original payoff | Existing scope / implementation boundary |
| --- | --- | --- |
| **Wrong venue, perfect confidence** | An NPC arrives with a suitcase and a giant conference badge at the laundrette. The player checks the destination sign and points them toward the right entrance. NPC: “Same logo. Very different conference.” | Phase 1/4 world humour and navigation teaching, inspired by the Mert mishap format. Authored NPC behaviour can be local; any reward/objective completion needs an agreed server path. Do not deliberately make actual game wayfinding misleading. |
| **Mod Desk: protect the conversation** | One funny message, one genuine criticism, one scam and a limited clock; distinguish them with Ban/Allow and immediate explanations. A calm moderator treats a sceptical builder as a person, not an enemy. | Already-supported career score/settlement contract; the staged 45-second desk is the immediate playable candidate. Its cases are authored fiction. Current practice must never masquerade as a paid server shift. |
| **The quiet launch party** | A builder fixes a real in-game project milestone. A tiny ribbon is cut, one confetti cannon coughs, and the terminal says “One less thing on fire.” | Phase 5 project progression, inspired by engineering celebration. Only trigger the milestone payoff from a confirmed game event; ambient set dressing may show an explicitly fictional rehearsal. |
| **Room-temperature conviction** | A fictional optimist uses the same heroic pose after a win and a loss; after a loss they quietly rotate their framed chart upside down. Original line: “The thesis needs a different angle.” | Existing market/liquidation incidents in phase 2 can trigger the reaction. No copied CZ dialogue, implied real-person endorsement, trading advice or prediction. |
| **After-hours table** | Friends sit at a small table, take turns telling the funniest thing that happened in their actual session, and optionally pose for a captioned game image. Original host prompt: “What did the city do to you today?” | Phase 3 voice plus phase 4 place/identity; mirrors the group-chat and hosted-event observations without replacing the agreed backend. Captures need user initiation and factual outcomes; microphone recording is not assumed. |

[Recommendation] The next coherent playable slice is **arrive → understand one nearby situation → complete Mod Desk → see the server-confirmed result when online → rejoin friends in the square**. Practice is useful for touch/play testing while the backend is unavailable. That is a concrete interaction to polish now, not an argument to delete later phases. Build humour around behaviour, place and consequence. The sample does not support a claim that celebrity skins, advertising inventory or ORE/SKR rewards alone create a return habit.

### Negative evidence still governs the commercial decision

- The competitor objection remains: [Pumpville's official documentation](https://pumpville.gitbook.io/docs) and [Solana Mobile's MONOLITH announcement](https://solanamobile.com/blog/solana-mobile-monolith-hackathon-winners-announced) establish an existing Seeker social-game proposition; they do not publish verified retention, revenue or an uncontested CT Life market.
- Recent public enthusiasm, a joke's likes, an attendee card and an in-game screenshot are different signals. None here establishes paying demand for likenesses, advertiser budgets, voice migration or the first 1,000 installs.
- The two critical community posts actively weaken a launch thesis centred on promotional Spaces and token balances. They do not invalidate shared social play, but that alternative still needs observed return sessions with real people.
- Names and likenesses remain the separate rights/endorsement question below. Studying a public voice is not permission to sell that person's avatar or have them deliver generated dialogue.

## One cohesive game direction

[Proposal] **“Come for the chaos. Stay for your people.”** Keep the Sims-style adult cast and warm city, then contrast ordinary social life with absurd market drama. The core experience is something to *do together* while talking, not a set of menus that competes with a conversation for attention.

### A five-minute visit

1. **Arrive where something is happening.** After wallet sign-in and onboarding, the lobby names the player's actual venue and available activities. Room counts and incidents come from the server. When nobody is present, show that honestly; authored NPCs still supply a solo experience and are visibly marked NPC.
2. **Join a small, recognisable situation.** A café table, a tiny stage and a street terminal sit within a readable camera composition. Each has a different silhouette, ambient sound and interaction cue. Do not require reading an entire phone screen to recognise the place.
3. **Make a short decision.** Work the existing skill mini-game, make a fictional-market decision, inspect a caller's record or react to an incident. The action earns its response through the confirmed server outcome.
4. **See a result together.** A liquidated player reacts; a siren, ticker and crowd gesture tell nearby players why. A restrained headline card identifies the incident. The result is legible without sound. Large failures are comic setbacks, not public harassment.
5. **Leave a trace.** A factual game receipt captures the outcome, cast and place. Sharing is optional and user-initiated. Later: invite a friend into the same activity, not an unspecified empty city.

[Proposal] Start implementation with a polished real-incident presentation because its contract already exists. Follow with in-world reaction choreography driven by those same messages. Local ambient NPC animation may decorate the setting; it must not fabricate transactions, players, room popularity or paid activity.

### A cast built around readable behaviour

[Proposal] A silhouette, repeated behaviour and reversal should define each character before extra dialogue is written.

| Role from the spec | Visible setup | Payoff proposal |
| --- | --- | --- |
| Town Crier | Folded newspaper, portable sign, short patrol | A new incident turns the sign, interrupts the stroll and draws nearby attention. |
| Reply Guy | Phone forward, always searching for a better angle | Follows an actual in-game leader; celebrates a win a beat late. |
| Perp Degen | Three tiny screens, impatient foot, overconfident posture | A confirmed liquidation folds the chair; the character recovers and walks to the clinic. |
| Builder | Toolkit, coffee, exhausted stance | A project milestone produces a small celebration while the terminal still displays one remaining task. |
| Sybil Farmer | Gardening tools and suspiciously identical pots | The existing later-phase clone/mod interaction becomes a readable visual gag. |
| Anon | Distinct hood, quiet gestures, expressive accessories | Can be socially expressive without revealing a real identity. |

[Proposal] Write jokes about incentives and behaviour: status anxiety, late entry, stubborn conviction, endless “one more fix,” performative alpha. Avoid a glossary of old catchphrases. Do not make every character a scammer, every line a shout, or every mechanic a price chart. Builders, artists, mods, hosts, collectors and ordinary friends need things to enjoy too.

[Proposal] Use original lines with 3–8-word visual punchlines and longer optional conversation. A line should reveal a character or respond to a real event. Do not present adapted real posts as original quotations by named people.

## Orientation and art decisions to test

**User decision: landscape gameplay.** [Proposal] Keep phone content in a comfortable panel within that landscape view. Use the wider composition for two-thumb controls, nearby people and clear destinations; avoid forcing a physical rotation every time a player opens their phone.

[Proposal] Validate the chosen landscape layout on Seeker: approach a person, turn the camera, trigger an interaction, open chat and return to walking. Record wrong taps, blocked sightlines, readability and frame time. The orientation decision is settled; movement and interaction quality still need native acceptance.

[Proposal] Use a nearer 3/4 camera, grounded feet, readable faces and a small number of carefully composed destinations. Show depth with facades, awnings, trees, foreground framing and restrained atmospheric colour. Reuse inspected licensed kits, then normalise scale, palette, roughness, texel density and LODs. Reference images, marketplace thumbnails and rendered lobby art do not count as playable asset acceptance.

## Voice: the reason to choose this over a Space

[Inference] Voice alone duplicates existing products. A better reason is shared presence: seeing a friend arrive, a host on a stage, a reaction across the table, and an activity that can be enjoyed while listening.

[Proposal] The default social posture is **listen first**. Make mic state, active speakers, audience and exit unmistakable. Keep mute/block/report within one visible action path. Do not record microphones for share clips by default. Use avatar animation, captions and game SFX for a first version of shareable moments. A future recording feature needs an explicit consent and retention design.

[Proposal] Proximity must control who receives a track, not merely reduce playback volume: muting far-away audio locally still leaves media delivered to that client. Review membership, subscription permissions, reconnection and blocking against the actual LiveKit/server implementation. Test headset/speaker echo, weak networks, background/resume and call interruption with multiple devices. The local dev server is not evidence of production media capacity.

## Real people, personal avatars and advertising

The current [Solana Mobile Publisher Policy](https://legal.solanamobile.com/en/publisher-policy), last updated 21 July 2026, prohibits intellectual-property infringement, false affiliation/endorsement and misleading deepfakes. It requires UGC reporting, moderation and block remedies, as well as transparent data handling and account deletion. This page was retrieved directly over HTTPS after the web extraction endpoint failed; its full text was inspected.

In *Keller v. Electronic Arts*, the Ninth Circuit rejected EA's asserted transformative-use defence for recreating the athlete in his familiar sporting setting. This US decision is not a worldwide ruling on CT Life or a declaration that all caricature is unlawful. It is concrete negative evidence against assuming “it is a game/parody” automatically clears a recognisable commercial likeness. [Court opinion, pp. 5–6](https://cdn.ca9.uscourts.gov/datastore/opinions/2013/07/31/10-15387.pdf).

[Proposal] Preserve the user's ambition for Mert, CZ, Toly and other recognisable guests as **opt-in licensed cameos** with approved name, likeness, dialogue, uses and commercial terms. Until then, author distinctive fictional roles. A “not affiliated” label is not evidence of likeness permission. Do not clone real voices or advertise a real person as endorsing the game without that agreement. Final jurisdiction-specific likeness and payments clearance needs qualified counsel; it is not resolved by this research.

[Proposal] Personal-avatar generation should be opt-in, previewable before purchase and separate from wallet authentication. Define ownership/consent for uploaded photos, provider retention/deletion, failure/refund handling and actual generation cost before fixing a price. A successful generator render is not proof of a usable low-poly rig, expressions, clothes or mobile performance.

[Proposal] Ads should appear at socially meaningful places—stage backdrop, café poster, event booth—with a persistent **Sponsored** label, reviewed destination and measured impressions. Sell observed traffic and a clearly described placement, not implied returns. Do not quote the old spec's prices or bookings as validated CT Life demand. The [FTC's influencer guidance](https://www.ftc.gov/business-guidance/resources/disclosures-101-social-media-influencers) requires material relationships to be disclosed; any paid creator launch arrangement needs its disclosure too.

## Distribution and an honest virality test

The [X developer policy](https://docs.x.com/developer-terms/policy) prohibits monetary or virtual compensation for X actions in its pay-to-engage section. It also restricts misleading affiliation and display of X content. Preserve the existing ban on rewards for posts, likes, follows and reposts. Do not scrape live posts into NPC speech.

[Proposal] The first distribution experiment is a **hosted Seeker social night**, recruited through opt-in community hosts, with direct venue/room joining, a fixed start time, two prepared activities and a useful recap. A host should be able to say exactly what friends will do on arrival. Pair this with dApp Store listing quality and actual gameplay clips. No invitations, messages, bookings or submissions were sent in this research.

[Proposal] Use 3 initial host groups with 8–12 invited participants each to inspect onboarding, arrival friction, humour and voice behaviour. These are research sample sizes, not forecasts. Then expand only when people independently choose to return and hosts ask to schedule another session. Keep all six phases in development; this experiment tests the proposition, not a scope cut.

[Proposal] Track wallet-start → successful sign-in → world arrival → first interaction → a second human interaction → next-day return. Separate invited event cohorts from ordinary store discovery. Track time spent with other humans, completed activities, voluntary shares, link-to-install conversion, D1/D7 return and host repeat sessions. Do not count NPCs, automation or fixture sessions as users. Predeclare success thresholds with the user once a baseline exists; no source opened here establishes a defensible universal Seeker target.

[Inference] A funny clip can produce reach without a durable game. A recurring room can retain a small group without enough advertiser demand. Both have to be measured separately. No amount of research can establish that this game will definitely go viral before players demonstrate the behaviour.

## Preserve the full six-phase game

| Phase | How the direction carries through |
| --- | --- |
| 1 · City and identity | Adult cast, movement, readable venues, real incident presentation, careers and meaningful arrival. |
| 2 · Money and markets | Fictional outcomes staged in the city; honest receipts; required ORE Refinery and SKR flows retain their existing integration gates. No financial writes in this work. |
| 3 · Voice and calls | Stage/table social spaces, clear speaking state, invitations, moderation and reliable reconnect. Paid calls follow the agreed backend, not new client economics. |
| 4 · City life | Homes and wardrobe make social identity tangible; jobs, visits, needs and daily clues add reasons to move through the world. |
| 5 · Projects and ads | Player teams host at HQs; reviewed sponsored placements fund relevant social places. |
| 6 · Competition | Tribes, leagues, Ratio Arena and rotating community occasions turn individual stories into recurring shared history. |

**What this report does not change:** imported contract IDs, backend ownership, the wallet-only normal entry route, six-phase scope, ORE/SKR requirements or publication authority. Source-level changes, native import, rendered quality, device performance, server integration and user approval remain separate acceptance states.
