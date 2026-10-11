# Duolingo design teardown and CT Life onboarding spec

Date: 2026-10-08. Purpose: learn Duolingo's structure and clarity for CT Life's onboarding (landscape 20:9, Android/Seeker). Keep our identity. Do not copy their owl, green or font.

## 0. Source status and labels

Read this first. It tells you how much to trust each number.

| Label | Meaning |
|---|---|
| **[Published]** | Stated in Duolingo's own brand guidelines. |
| **[Measured-CSS]** | Read from Duolingo's live production stylesheet (duolingo.com, fetched 2026-10-08). Real values, but not "official guidance". |
| **[Teardown]** | Stated by a third-party teardown or a Duolingo blog post. |
| **[Unverified]** | Recalled or inferred. Not confirmed by a fetched source. Treat as a starting guess. |
| **[Spec]** | Our own decision. Not from Duolingo. |

What I could and could not get:

- **design.duolingo.com now 301-redirects to blog.duolingo.com/hub/design/** (checked with curl on 6 sub-paths). The live brand site is gone. I read the last Wayback capture (26 Dec 2024) by pulling its JavaScript bundle, which holds the full guideline text and the colour table. Source: `web.archive.org/web/20241226190258/https://design.duolingo.com/` (bundle `main.97ab633ac458b0ea94d6.js`).
- **The guidelines have no motion section and no sound section.** They cover logo, colour, typography, illustration, characters, writing and voice. All motion, sound and haptic claims below are [Teardown] or [Unverified].
- **Button lip, radii, progress-bar and card specs** come from Duolingo's live web CSS (`d35aaqx5ub95lt.cloudfront.net/css/app-14909179.css`). That is the web app, not the native mobile app. Mobile is likely similar [Unverified].
- **Search was unavailable** (web-search budget exhausted), so I fetched known URLs directly. Several teardown URLs I guessed returned 404. I found only thin teardowns, listed in section 1.
- I did not find a teardown with measured pixel specs of the mobile onboarding. Mobile-specific numbers are therefore not measured from screenshots. Where I give them, they are **[Spec]**.

Sources used:
1. Duolingo brand guidelines, archived: https://web.archive.org/web/20241226190258/https://design.duolingo.com/
2. Duolingo web CSS: https://d35aaqx5ub95lt.cloudfront.net/css/app-14909179.css and https://d35aaqx5ub95lt.cloudfront.net/css/4779-c36ad395.css
3. Duolingo blog, core tabs redesign: https://blog.duolingo.com/core-tabs-redesign/
4. Duolingo blog, widget: https://blog.duolingo.com/widget-feature/
5. Duolingo blog, streak: https://blog.duolingo.com/how-duolingo-streak-builds-habit/
6. Duolingo blog, character lip-sync with Rive: https://blog.duolingo.com/world-character-visemes/
7. Duolingo blog, design culture: https://blog.duolingo.com/duologues-design-conversations/
8. Page Flows, Duolingo iOS onboarding screen list: https://pageflows.com/post/ios/onboarding/duolingo/
9. UserGuiding, Duolingo onboarding walkthrough: https://www.userguiding.com/blog/duolingo-onboarding-ux/
10. UserOnboard, Duolingo teardown (78 slides, dated 01/2015, images only, so no usable text): https://www.useronboard.com/how-duolingo-onboards-new-users/
11. Growth.design, Duolingo retention case study (not onboarding): https://growth.design/case-studies/duolingo-user-retention
12. Android touch target guidance: https://support.google.com/accessibility/android/answer/7101858
13. Android haptic constants: https://developer.android.com/reference/android/view/HapticFeedbackConstants (the fetch summary admitted it could not see the full table; the constants are [Unverified] until checked in the Unity/Android build).

---

## A. Principles, each as a rule

### A1. One decision per screen
**Rule: each screen asks one question and has one primary action.**
- The documented iOS flow is a chain of single-question screens: select language, how did you hear, experience level, sign-up reason, goal, then a quiz. Each is one screen. [Teardown: Page Flows, source 8]. UserGuiding lists the same single-question steps (language, proficiency, goal, daily goal, account) [Teardown, source 9].
- Their redesign write-up states the same instinct: "Good design means knowing what to remove and what to keep" [Published, source 3].
- Note: the Page Flows order is as captured at some unknown date. Duolingo changes this flow often. [Unverified] that it is current in Oct 2026.

### A2. Sticky bottom CTA, disabled until a choice is made
**Rule: the primary button is always in the same place and is visibly inactive until the screen is answerable.**
- Duolingo's disabled button is a flat grey (Swan `#E5E5E5` fill, Hare `#AFAFAF` text) with no lip: the code translates it down by the lip width and drops the shadow, so it looks pressed in [Measured-CSS, source 2].
- "Sticky at the bottom, disabled until a choice" is how the flow behaves on mobile [Unverified; no teardown states it in words]. I am treating it as the pattern, because the disabled-state CSS exists.

### A3. Top progress bar and back
**Rule: show where the player is with a thin bar at the top and a back arrow at the left. Fill animates, it does not jump.**
- Progress bar CSS: track Swan `#E5E5E5`, fill Feather Green `#58CC02`, fully rounded ends (radius = height / 2), fill animates over **0.4 s**, a small white "shine" strip on the fill at 20% opacity [Measured-CSS, source 2].
- Teardown: "Simple looking UI design and a progress bar can go a long way in keeping users interested" [Teardown, source 9].
- Bar height is a CSS variable with no default I could read. Use 16 dp [Spec].

### A4. Reactive mascot with a speech bubble
**Rule: a character reacts to the player's choice in a short line. It never blocks the action.**
- Duo "never actually talks — he communicates via text" in the app, push and email. Speech bubbles are the in-app form [Published, source 1].
- Duo "is social, a great communicator, and rather emotional" and is "their #1 fan and biggest cheerleader" [Published].
- Characters should not be "static, expressionless" [Published].
- The widget swaps Duo's mood by state: relaxed after a lesson, "increasingly panicked" as the streak is at risk [Teardown, source 4].
- Duolingo uses a state machine (Rive) so reactions blend instead of snapping [Teardown, source 6].
- **For us:** the reactor is not an owl. Use our own cast (see C0).

### A5. Big tappable choice cards with icons
**Rule: choices are large cards, one per option, with an icon and a short label. Whole card is the tap target.**
- UserGuiding: the "what brought you here" step uses icons for each option [Teardown, source 9].
- Card CSS: radius **12 px**, border **2 px** (Swan) on three sides plus **4 px** on the bottom (`border-width: 2px 2px 4px`), white fill. On press it moves down **2 px** [Measured-CSS, source 2].
- Selected state colours: not in the CSS I fetched. [Unverified]. Common pattern is a blue-tinted fill with a blue border. We define our own in C.

### A6. The button: chunky rounded rectangle, darker lip, press-down
**Rule: every primary button has a solid darker "lip" under it. On press it travels down by the lip's depth and the lip disappears.**
- Default lip **4 px**; small buttons **2 px** [Measured-CSS: `--__internal__lip-width: var(--web-ui_button-lip-width, 4px)` and `.\_17LYs{--__internal__lip-width:2px}`].
- Press: `transform: translateY(<lip width>)`, lip shadow set to `none` [Measured-CSS]. So travel = lip depth exactly.
- Radius **12 px** default, **16 px** on small and pill variants, **8 px** on compact secondary [Measured-CSS].
- Heights: **50 px** default primary, **48 px** secondary and square icon buttons, **32 px** compact, **26 px** small [Measured-CSS].
- Padding `0 16px` primary [Measured-CSS].
- Hover (pointer devices only): `filter: brightness(1.1)` over **0.2 s** [Measured-CSS]. No hover on touch. Irrelevant for us.
- Lip transition duration is a CSS variable I could not resolve. [Unverified]. I will specify ~60 ms in C.
- Button text: ALL CAPS, no punctuation [Published, source 1: "Put button copy in all caps", "Never use punctuation in a button"]. Letter-spacing **0.04 em** on secondary buttons [Measured-CSS].
- Macaw blue button: fill `#1CB0F6`, lip colour Whale `#1899D6` [Measured-CSS]. Primary green button uses Owl `#58CC02` with a darker green lip [Unverified colour of lip].

### A7. Typography: heavy rounded display face, a short scale
**Rule: one heavy rounded face for headlines, a lighter rounded face for body, few sizes.**
- Headlines: Feather Bold (custom). Body: DIN Next Rounded. Fallback: Nunito [Published, source 1].
- Feather Bold: lowercase headlines, left-aligned, leading **100-110%**, tracking -20, never below 30 px on screen, avoid sentences over 10 words [Published].
- DIN Next Rounded: leading **140%**, tracking 0, never below **14 px** [Published].
- Feather should be about **150% larger** than DIN when paired [Published].
- Live app type tokens (web): page title 40/44 and 32/36, heading 28/32, 24/32, 20/24, label 24/24 and 16/16, all weight 700 [Measured-CSS, source 2]. So the scale is about 6 sizes: 14, 16, 20, 24, 28, 32, 40.
- **For us:** Lilita One is already our heavy rounded face. Do not add a second display face. Pair with a rounded body face (Nunito is free, OFL) [Spec]. Check what the current build uses for body before choosing; the screenshots show a geometric sans (looks like Manrope) [Inference from screenshots].

### A8. Limited palette per screen, generous whitespace
**Rule: one accent colour per screen. Use space instead of boxes.**
- Core hierarchy: Feather Green `#58CC02` is "the core color"; Mask Green `#89E219`; Eel `#4B4B4B` for type; Snow `#FFFFFF` as background [Published/Measured, sources 1 and 2].
- Neutrals: Polar `#F7F7F7`, Swan `#E5E5E5`, Hare `#AFAFAF`, Wolf `#777777`, Eel `#4B4B4B`.
- Secondaries: Macaw `#1CB0F6`, Whale `#1899D6`, Humpback `#2B70C9`, Cardinal `#FF4B4B`, Bee `#FFC800`, Fox `#FF9600`, Beetle `#CE82FF`.
- "Never use gray" in illustration: "it appears lifeless and cold" [Published]. (UI neutrals are grey; illustration is not.)
- Illustration: "Keep your illustration to a few colors. Too many colors can hurt legibility when an object scales to a small size" [Published].
- Whitespace: "cleaned up our visual spacing by purposefully using the whitespace around the components" and removed borders and card segmentation [Teardown/Published, source 3].
- Colour in headers: "using artwork in every tab header is consistent, but it can also feel forced" [source 3]. Lesson: use art where it carries meaning (our character, our reveal), not as decoration on every screen.

### A9. Shape language: rounded everything
**Rule: no sharp corners, ever. Shapes are rounded rectangles, circles, rounded triangles.**
- "All of our illustrations are made from three basic shapes: the rounded rectangle, the circle, and the rounded triangle... Pointy shapes are off-brand" [Published].
- Shadows are a **pill shape, never an oval** (ovals imply perspective) and always darker than the ground [Published].
- Eyes geometric, "No ovals!" [Published]. Skip for us; our 3D cast has its own look.

### A10. Celebration moments
**Rule: finishing a step earns a short, loud, rewarding beat. Make milestones bigger than steps.**
- Streak animation: Duo spins and bursts into flame, bigger on milestone days. Result: "+1.7%" chance a new learner is still active 7 days later [Teardown, source 5, Duolingo's own number].
- "Early-stage learners see proportionally larger milestone jumps (2 to 3 days = 50% growth), making daily animations feel more rewarding" [source 5].
- In-lesson: "celebratory remarks and achievement milestones... when users answer correctly in a row" [Teardown, source 9].
- Voice: "When learners succeed, we cheer them. Bust out the exclamation points!" and when they stumble: friendly, helpful, "don't overdo it" [Published].
- Confetti, sound and haptic details are not in any source I fetched. [Unverified]. I specify ours in C.

### A11. Copy voice: short, playful, second person
**Rule: short sentences, plain words, contractions, one idea per line. Cheer on success, support on failure.**
- "Our writing is short and clear. We use words that everyone understands" [Published].
- Voice has four qualities; two are "expressive" and "playful" ("recess for the brain"); another is built on "micro-successes... celebrate learners when they succeed, and encourage them when they stumble. We never alienate our learners" [Published].
- Contractions are "encouraged" [Published]. Avoid slang or references "not everyone knows" [Published]. **We deliberately break that last one**: our audience is CT. But every slang term must be clear from context, and "live" terms only (C0).
- Headlines in sentence case, button copy ALL CAPS, no punctuation in buttons, exclamation points allowed in headlines [Published]. Emoji sparingly, about half the time [Published, for push].
- "Tone down the exuberance" for serious moments [Published]. Applies to our money/debt moment (C1).

### A12. Sound and haptics
**Rule: every tap, correct answer and finish has a small sound and a matching tactile tick, and both can be turned off.**
- Duolingo's design team lists "micro-interactions, copy, and sound" as craft that is hard to replicate [Published, source 7].
- Specific sound files, timings or haptic patterns: not found. [Unverified].
- Android has built-in haptic constants (`CONFIRM`, `REJECT`, `CLOCK_TICK`, `KEYBOARD_TAP` and others) [Unverified until checked against the Android reference; my fetch could not read the table, source 13]. In Unity, confirm what the Seeker supports before relying on them.

### A13. Accessibility
**Rule: touch targets at least 48 dp with 8 dp between them; never rely on colour alone; every sound and haptic is optional.**
- Android: "at least 48dp" and "separated by 8dp of space or more" [source 12].
- Duolingo's minimum type is 14 px body and 30 px headline [Published].
- Colour alone: Duolingo pairs colour with an icon and message in feedback [Unverified].
- Contrast: Duolingo's white text on `#58CC02` is a known low-contrast pair (about 2.2:1 by my arithmetic, [Inference]). Do not copy that. Our plum outline on cream text already gives high contrast. Check every new pair at 4.5:1 for body and 3:1 for large text (WCAG 2.x thresholds [Unverified here; widely published]).
- Reduced motion: provide a setting that cuts confetti, shake and spin to fades [Spec].

### A14. Motion style
**Rule: bouncy but quick. Things slide, pop and settle. Nothing floats slowly.**
- Not in the guidelines. What I have from the CSS: the modal slide-in keyframes move **20 px to 150 px** vertically with fade [Measured-CSS]. Some transitions use `cubic-bezier(.22,1,.36,1)` over **0.4 s**, which is an ease-out-quint [Measured-CSS]. Others use `.5 s` and `.7 s ease-in-out`. Colour/filter fades use **0.2 s** and **0.3 s**. Progress fill **0.4 s**.
- Character feet "flutter below his body as he jumps" [Published]. Characters should have overshoot and squash [Unverified].
- Our timings in section B are [Spec], informed by these.

---

## B. Specs we can copy without copying the brand

All Duolingo figures are CSS pixels on a web app at 1x. For Unity, convert by units, not pixels. Our target is 2670 x 1200. [Unverified] I do not know the Seeker's exact density. **Use dp = pixels / (Screen.dpi / 160) and measure on device.** Below, "dp" values are [Spec] unless marked.

### B1. Button
| Property | Duolingo | CT Life [Spec] |
|---|---|---|
| Height, primary | 50 px [Measured-CSS] | 56 dp (landscape thumbs reach farther; taller is safer) |
| Height, secondary | 48 px | 48 dp (the Android minimum) |
| Radius | 12 px (16 px small) | 14-16 dp; keep our painted squircle look |
| Lip (bottom depth) | 4 px (2 px small) | 5 dp primary, 3 dp small. Colour: 25-30% darker than the face, plum-tinted |
| Press travel | = lip depth | = lip depth. Face moves down, lip collapses to 0 |
| Press-down duration | var unknown [Unverified] | 60 ms ease-out |
| Release | n/a | 140 ms, small overshoot (ease-out-back) |
| Disabled | Swan fill, Hare text, pressed-in | Desaturated cream fill, 55% plum text, no lip, no press reaction |
| Outline | none | Keep our plum outline, 3 dp. The lip sits inside it |
| Label | ALL CAPS, no punctuation | Lilita One, cream text, plum outline. Caps for the primary CTA only; Lilita in caps is wide, check length |
| Min width | n/a | 200 dp |

### B2. Choice card
| Property | Duolingo | CT Life [Spec] |
|---|---|---|
| Radius | 12 px | 14 dp |
| Border | 2 px sides, 4 px bottom (Swan) [Measured-CSS] | 3 dp plum outline sides, 5 dp bottom lip |
| Press | translateY 2 px [Measured-CSS] | 3 dp |
| Layout | icon + label | Icon left 48 dp, title Lilita, one-line perk below in body face |
| Selected | tinted fill + coloured border [Unverified] | Cream fill goes to our accent (green for pick, purple for secondary), lip flips to accent-dark, 2 dp check badge top-right, card does a 1.04 pop |
| Gap between cards | n/a | 12 dp (above the 8 dp minimum) |
| Min height | n/a | 72 dp |

### B3. Progress bar
Height 16 dp, radius = height/2, track plum at 15% on cream, fill our green, 0.4 s fill [Measured-CSS] with ease-out-quint, shine strip at 20% white. Segment count equals step count (we have 7 steps), no numbers.

### B4. Type scale [Spec; Lilita One display, rounded sans body]
| Role | Size | Notes |
|---|---|---|
| Screen title | 40 dp | Lilita, plum outline 3 dp, cream fill, line 110% |
| Card title | 24 dp | Lilita |
| Body | 18 dp | Rounded sans, line 140% |
| Perk / caption | 16 dp | Floor. Never below 14 dp [Published] |
| Button | 24 dp | Lilita, tracking +0.04 em |
| Reveal number | 72 dp | Lilita |
Six sizes max. No size outside this list.

### B5. Spacing scale
4 / 8 / 12 / 16 / 24 / 32 / 48 dp [Spec]. Screen safe margin: 32 dp, plus the notch/cutout inset on the left and right (we are landscape, so cutouts sit on the sides).

### B6. Radius scale
8 (chips) / 14 (buttons, cards) / 24 (panels, speech bubbles) / full (pills, progress bar). [Spec]

### B7. Animation durations and easings
| Event | Duration | Easing |
|---|---|---|
| Button press | 60 ms | ease-out |
| Button release | 140 ms | ease-out-back |
| Card select pop | 180 ms | ease-out-back, scale 1.04 |
| Screen change (content) | 260 ms exit, 320 ms enter | ease-in then ease-out-quint (cubic-bezier .22,1,.36,1 [Measured-CSS]) |
| Slide distance | 48 dp horizontal | |
| Progress fill | 400 ms [Measured-CSS] | ease-out-quint |
| Speech bubble in | 220 ms, scale 0.8 to 1 + fade | ease-out-back |
| Mascot reaction | 300-500 ms | squash/overshoot |
| Error shake | 280 ms, 3 swings, 8 dp | linear decay |
| Reveal sequence | see C1 | |
| Reduced motion | all above become 120 ms fades | |
Rule: nothing in onboarding animates longer than 600 ms except the lottery reveal and the final celebration.

### B8. Landscape layout grid (20:9, 2670 x 1200)
Duolingo is portrait: stack, centre, CTA at bottom. For landscape:
- **Two zones.** Left zone about 38% width: character/visual and speech bubble. Right zone about 62%: the question title and choices. Visual left, choices right keeps the right thumb on the choices and the CTA.
- **CTA** bottom-right of the right zone, 56 dp tall, right-aligned to the safe margin. Back is a round icon button at top-left, next to the progress bar.
- **Progress bar** across the top, between the back button and a close/menu slot. Height 16 dp at 24 dp from the top.
- Single-choice cards in a **2-column grid** (3 columns for 7+ items) so no screen scrolls. If the content cannot fit without scrolling, split the screen.
- Vertical budget at 1200 px: top bar 80, title 100, content about 780, CTA zone 160, margins. Check on device.
[Spec for all]

---

## C. CT Life onboarding spec

### C0. Identity rules for everything below
- Keep: hand-painted sunny CT town, painted squircle buttons (green and purple, plum outline), Lilita One with thick plum outlines, cream text (title screen, screenshot `01-title-signin.png`).
- Do not use: owl, Duolingo green as the brand colour, Feather/DIN, 4 px flat-vector look. Our world is painted and 3D, so our lip and outlines are painted, not flat.
- **Current state (from screenshots and code):** the origin and creator screens use a flat light-blue background, white rounded cards with thin blue borders, a yellow button with a dark lip, and a tab strip "1 Look / 2 Personality / 3 Identity". The title screen is painted and warm; the next screens drop that world. The gap is the main thing to close. Files: `client/Assets/Scripts/UI/Screens/LotteryScreen.cs` (152 lines), `CharacterCreatorScreen.cs` (398 lines). Evidence: `/Users/mac/Documents/codes/projects/ct-life-evidence/first-time-flow/` (screens 01-16).
- **What exists today that we keep:** server-side roll (`POST /v1/lottery/roll`, client only animates), sound cues `sfx_ui_toggle_on`, `sfx_moon_horn`, `sfx_rug`, drag-to-turn 3D preview with Full body / Face focus, tribe perk text, trait cap of two, validation messages, alias min 3 chars, 18+ toggle, resume after relaunch.
- **A third lottery outcome exists in code:** "Seeker Baby" for Seeker Genesis Token holders. Not in the brief, so I spec it as an optional third card in C1.
- **Mascot/reactor.** Do not make a new mascot (scope). Use a character already in the title art: the round, laughing friends on the fountain, or the shiba, the purple ghost, the penguin. Recommendation: **the shiba** ("Doge-adjacent", instantly CT, wordless, easy to read at 96 dp) as the guide who reacts with a speech bubble. [Spec] **Parody-safe check:** the shiba is already in our own title art. Do not use a real project's exact mascot or logo. Confirm with the art lead that it is our own character and not a trademarked one.
- **Copy tone.** Short, playful, second person, CT-native, but readable by a newcomer from context. Sources: `docs/research/ct-slang-110.md`, `ct-now-2026-10.md`. **Use only terms marked Live in ct-now:** trenches, sending, cooked, skill issue, bundled, cabal, KOL, "we're so back / it's so over", slow rug, points/XP, gm. **Avoid as the main voice (Fading, ironic only):** ser, wagmi, HFSP. Never put real brands, handles, or token names in onboarding copy.
- **Money sensitivity.** All rewards are game Cash and not withdrawable; the title screen already says so. Keep a one-line reminder on the reveal. "$720 to pay back" is a debt: tone down the exuberance for that outcome [Published tone rule], but keep it funny.
- **Disclosure of odds.** The lottery is server-rolled. Show the odds before the roll [Spec, and it is the honest design]. I have not read the server odds; fill them in from the server config. **Do not invent them.**

### C1. Origin story lottery, as a reveal

**Goal:** turn a form with two cards into the first emotional beat. One decision: "open your origin". The player does not choose; they reveal. So the one action is a single button, and the mascot sets it up.

**Layout (landscape):** painted town background (reuse the title art, blurred and darkened 25%) behind everything. Left zone: shiba guide with a speech bubble. Right zone: two (or three) face-down cards side by side. CTA bottom-right.

**Screen 1: Setup**
- Progress bar: step 1 of 7 (the lottery is step 1).
- Eyebrow (small caps): `EVERY STORY STARTS SOMEWHERE` (keep, it works).
- Title (40 dp): `Draw your origin`
- Shiba bubble: `Where you start is luck. Where you go is yours.` (keep the existing line)
- Cards: two face-down cards with a plum back pattern and a "?" in Lilita. Small label under each showing odds from the server config: `Insider Baby: X%`, `Exit Liquidity Baby: Y%`. **[odds from server; do not hardcode]**
- Sub-line: `All rewards are game Cash.` (keep)
- CTA: `DRAW` (primary, green). Not "Discover my beginning": that is long and the button text should be a verb of 1-2 words.
- Sound: soft deck shuffle loop at 30%. Haptic: none.
- States: default; pressed (lip collapse); loading after press (button shows 3-dot pulse, disabled); error (bubble turns plum, text `Network's cooked. Try again.`, CTA re-enables); already-rolled (server returns 409 or reconciles): skip straight to the existing result screen, no card flip.

**Screen 2: The draw (about 2.4 s, cannot be skipped)**
- Cards shuffle: they fan out, swap places twice (260 ms each, ease-out-back), then hover.
- A highlight "spotlight" ring hops card to card, accelerating then slowing, exactly like the existing spin (0.07 s to 0.35 s steps). Keep that curve; it is good. Keep the tick sound per hop (`sfx_ui_toggle_on`, pitch rising with each hop: +1 semitone, cap at +6). Haptic: `CLOCK_TICK`-type tick on each hop, light.
- Shiba bubble swaps to `Hold on... *sweating*`. Shiba does the nervous shake.
- Minimum 2.2 s as now. If the server answer is late, the ring keeps slowing on one card but never lands until the result arrives (the existing code already waits, with an 8 s cap). At 8 s show `Still rolling. Hang on.` and a retry.
- CTA is hidden during the draw (not disabled), to keep the screen quiet.

**Screen 3a: Reveal, Insider Baby**
- The winning card flips (Y-axis, 420 ms, ease-out-quint), lands with a 1.06 pop and a 120 ms hold. The loser slides to the side and fades to 35%.
- Gold coin burst: about 24 coins from the card, 700 ms, gravity, then a short confetti shower (60 pieces, 900 ms, plum/cream/gold/green/purple only).
- Sound: `sfx_moon_horn` on the flip. Haptic: `CONFIRM`-type strong tap on the flip, then a light double tick on the confetti.
- Card content (big): `Insider Baby` (Lilita 40), `$3,000 Cash`, `A studio of your own`. Numbers 72 dp, count up over 600 ms.
- Shiba bubble: `Born on the right side of the group chat.` (flavour; fits CT's "insider/cabal" language, parody-safe)
- Footer: `Game Cash. Not withdrawable.`
- CTA: `NEXT` (green). Slides in 400 ms after the flip (not before: the reveal must not compete).

**Screen 3b: Reveal, Exit Liquidity Baby**
- Same flip, but the card lands with a squash (0.94 then back) and a "bonk" instead of a pop. No coin burst. A small cloud puff and 12 plum-grey confetti pieces falling slowly.
- Sound: `sfx_rug`, but mixed at -6 dB and cut at 800 ms so it reads as comic, not punishing. Haptic: `REJECT`-type single soft thud. (Then a light confirm tick when the CTA arrives, to say "this is fine".)
- Card content: `Exit Liquidity Baby`, `$300 Cash`, `$720 to pay back`. The debt line is smaller and in a warm orange tag, with an info icon that opens a bubble: `Pay it back and you're square.` [Spec; verify the debt mechanic with the economy design before shipping this line]
- Shiba bubble: `Cooked on day one. Skill issue? No. Just luck.` Tone down: no exclamation marks. Then a second bubble 1.2 s later: `Everyone's got a comeback arc. Yours starts now.`
- Reason for the second bubble: the player must not feel punished. Duolingo's rule: when learners stumble, be friendly and don't overdo it [Published].
- CTA: `NEXT`.

**Screen 3c (optional): Seeker Baby**
- Only if the account is a Seeker Genesis Token holder (existing code path). Third card, phone icon, Mask-style gold glow. Copy: from the existing `lottery.seeker` strings. Same flow as 3a. [Spec]

**Components:** `OriginCard` (face-down, face-up, win, lose states), `GuideBubble`, `PrimaryButton`, `ProgressBar`, `CoinBurst`, `ConfettiLayer`.
**Accessibility:** card state is also shown by text and a badge, not only colour. Reduced motion: no shuffle, no confetti; a 200 ms crossfade to the result. Sound on/off respected. Cards at least 160 dp wide.

### C2. Character creator: one decision per step

The existing creator has 3 tabs where two (Personality, Identity) hold multiple decisions in one scroll. Split into 5 decision screens, plus confirm and celebration. The tab strip goes away; the top progress bar replaces it. Back always works. Selections persist when going back.

**Step list and progress bar (7 steps in total with the lottery):**
1. Origin draw (C1)
2. Look
3. Tribe
4. Traits (2)
5. Dream
6. Name
7. Confirm, then celebration and enter the city

Shared frame on every creator screen:
- Top: back button (round, 48 dp), progress bar, optional menu slot.
- Left zone (38%): the player's character in 3D, centre stage on the Look step, and smaller (about 60% height) on later steps, wearing the chosen look, reacting to each pick (see per-step reactions). **The character is the mascot here.** The shiba bubble is not used in the creator, to avoid two voices. Instead the player's own character "speaks" in the bubble, in first person. [Spec]
- Right zone (62%): one title, one subtitle, the choices, in that order.
- Bottom-right: CTA. Bottom-left: a small hint line (e.g. `Pick one`).
- Disabled until the step is answerable. Short reason in the hint line when disabled, as the code already does (`creator.needTribe` etc.).

**Step 2: Look**
- Layout: character fills the left 60% of the screen on this step only (centre stage). The right 40% has the look selector: left and right arrows with the look name and `1 of 24`, plus `Full body / Face` toggles. This is what exists today, but it is wrapped in a flat grey box. Replace the grey box with the painted town background and a soft spotlight and a pill shadow under the feet (pill, not oval [Published]). Idle breathing animation. Slow auto-rotate of 8 degrees until first touch.
- Title: `Pick your look`. Hint: `Drag to turn`. (There is already `Drag to turn`; keep it, add a one-time finger swipe hint that fades after the first drag.)
- Swiping arrow: 180 ms slide, character cross-fades.
- CTA: `NEXT`. Enabled as soon as a look is shown (it always is, default look). So the CTA is enabled on arrival; that is fine because a default exists. [Spec]
- Reaction: character does a small wave on arrival and a little jump when the look changes. Bubble: `Fresh fit.` (2 words, no punctuation noise) [Spec].
- Edge cases from the code: look preview-only vs server-confirmed (`creator.lookPreviewOnly`). Show a small `Preview` tag only on dev builds; do not show an end user an error about server confirmation unless submit fails.
- Sound: soft swish on arrow. Haptic: light tick per look change.

**Step 3: Tribe (7)**
- Title: `Find your people`. Subtitle: `One tribe. One perk.`
- Grid: 7 cards. In landscape, 4 + 3 in a 4-column grid with the last row centred, each about 150 dp wide by 120 dp tall. Each card: icon (48 dp, our own painted icons), tribe name (Lilita 22), perk line (16 dp, 2 lines max).
- **Perks, from the existing registry (screenshot 09):** Trencher: sees new launches 3 seconds earlier. Perp Degen: leverage cap +5x. Prediction Shark: prediction fee -50%. Airdrop Farmer: farm yield +15%. Caller: calls reach 2x NPC followers. Builder: project launch cost -30%. Anon: hidden from rich list, immune to Doxx events.
- Tribe flavour lines (for the character's bubble on select), live terms only:
  - Trencher: `Living in the trenches.`
  - Perp Degen: `Max long, no fear.`
  - Prediction Shark: `I already know the outcome.`
  - Airdrop Farmer: `Points are my love language.`
  - Caller: `Drop the CA. I'll handle the vibes.`
  - Builder: `Shipping while you scroll.`
  - Anon: `You never saw me.`
  [Spec; check these against current copy owner before shipping]
- States: default, pressed, selected (accent fill, lip flips, check badge, 1.04 pop), disabled N/A. Single-select; tapping another card moves the selection. Tapping the selected card again does nothing (no deselect: it would disable the CTA and confuse).
- CTA: `NEXT`, disabled until a tribe is selected. Hint while disabled: `Pick a tribe`.
- Feedback: card tap = tick sound + light haptic. Character swaps a small prop or pose per tribe (hard hat for Builder, hoodie for Anon) if art exists; otherwise a tribe-coloured glow behind the character. [Spec; props need art]
- Perk shown in full in the selected state, so the player sees what they get.

**Step 4: Traits (pick 2)**
- Title: `Pick two traits`. Subtitle: `Your habits.` Six cards, 3 x 2 grid.
- Traits: Fast Talker, Deep Pockets, Iron Will, Early Bird, Sleuth, Rug Veteran. Each card needs a perk or effect line. I do not have the trait effects in my notes; read them from the registry (`_reg.Traits`) and write one line each. **Do not invent effects.**
- Counter `0 / 2` as two empty dots above the grid, filling as selected (keep the existing counter idea, make it visual).
- Behaviour: today picking a third silently removes the oldest. Keep that, but show it: the oldest card un-pops with a 120 ms shake so the player understands. [Spec]
- CTA `NEXT`: disabled until exactly 2. Hint: `Pick 2 (1 left)`.
- Reaction bubble after the second pick: the character shows a two-trait combo line if one exists, otherwise `That tracks.` Keep it short.
- Sound/haptic: tick on pick, confirm-type tap when the second pick lands.

**Step 5: Dream**
- Title: `What's the dream?` Subtitle: `Where you're headed.`
- Choices come from the registry (`_reg.Dreams`). Single-select cards, 2-column grid, each with an icon and a one-line description. **Dream names and count: read from the registry. Not in my notes.**
- CTA `NEXT`, disabled until picked.
- Reaction: character looks up and right (toward the horizon) for 600 ms.

**Step 6: Name**
- Title: `What do they call you?` Subtitle: `3-20 characters.`
- Layout: the character left, a large text field right, keyboard at the bottom. Landscape keyboards cover half the screen. The existing code sets `hideMobileInput = false` to keep the field above the keyboard (noted from device QA). Keep that. Place the field at the top of the right zone, CTA above the keyboard line or in the keyboard's accessory row [Unverified; test on device].
- Field: inline validation as you type: green tick when valid (3+ chars), grey rule line under the field. Error states: too short, taken (server), not allowed. Error text in plum on cream with an icon, not only red.
- Age: the 18+ confirmation is a requirement already in the title screen and in the code. Make it a **separate sub-step on the same screen** only if it fits; otherwise move to its own screen before Confirm. A checkbox is OK here: `I'm 18 or older.` (This is a legal gate; keep it plain and not cute.)
- Bubble: `Pick something CT will remember.` Shows after 2 s idle.
- CTA `NEXT`, disabled until valid name and age confirmed.
- Name suggestions: a `Roll a name` dice chip (3 random CT-style names, no real handles). [Spec; needs a clean word list]

**Step 7: Confirm**
- Title: `Meet [Name]`. Left: character, full body, turning slowly. Right: a summary card: `[Look] · [Tribe] · [Trait] · [Trait] · [Dream]` as five chips with icons, each tappable to jump back to that step (stays editable; today's `creator.editHint`).
- Origin line at the top of the card: `Insider Baby · $3,000 Cash` (or the Exit Liquidity version with the debt).
- CTA: `ENTER CT` (green, bigger: 64 dp). Pressing it submits (`POST /v1/characters`). While saving: spinner on the button, back disabled (existing `SetBusy`). On error: show the message in the bubble, button re-enabled.
- Disclaimer line: `Game Cash only. 18+.`

**Celebration then enter the city**
- On success: the progress bar fills to 100% with a flash. Screen dims to a painted-town vignette. Character jumps (squash then stretch, 500 ms). Confetti (full: 120 pieces, 1.2 s), coin burst if Insider, a banner `Welcome to CT, [Name]!` in Lilita with plum outline.
- Sound: a short fanfare (about 1.5 s) then the city ambience fades in under it. Haptic: `CONFIRM` strong, then a 3-tick roll.
- Shiba pops in at the corner with one last line: `gm. Go touch some grass. Or the trenches.` [Spec]
- After 1.8 s: auto-advance, or tap to skip. Camera pushes from the character into the town scene (fade through painted background, 600 ms), then gameplay starts.
- Reduced motion: no confetti, 300 ms crossfade, haptic only.
- Optional: a `Daily goal`-like first quest card on arrival, so the first minute has a goal. Not in scope here.

### C3. Component inventory (new or changed)
- `PrimaryButton` (green and purple variants, lip, press travel, disabled state)
- `SecondaryButton` / `BackButton` (round)
- `ProgressBar` (7 segments, 0.4 s fill, shine)
- `ChoiceCard` (default, pressed, selected, disabled), with `ChoiceGrid` (2/3/4 columns)
- `GuideBubble` (shiba) and `CharacterBubble` (player)
- `OriginCard` (face-down, up, win, lose)
- `CoinBurst`, `Confetti`, `Shake`
- `StepFrame` (back, bar, left zone, right zone, CTA slot, hint line)
- `SfxMap` and `HapticMap` (central; every cue named, so QA can see them in one place)
Implementation note: put all lip/press and card states in USS tokens, so each screen only picks a variant.

### C4. Event, sound and haptic table
| Event | Sound | Haptic |
|---|---|---|
| Any button press | soft tap | light tick |
| Card select | `sfx_ui_toggle_on` | light tick |
| Card deselect/replace | low tick | none |
| Draw hop | tick, +1 semitone per hop | light tick |
| Reveal good | `sfx_moon_horn` | strong confirm |
| Reveal debt | `sfx_rug` at -6 dB, cut 800 ms | soft thud |
| Step complete | rising 2-note chime | light confirm |
| Error | short low blip | reject |
| Final celebration | fanfare 1.5 s | strong confirm + 3 ticks |
Haptic API on Android: `HapticFeedbackConstants` (CONFIRM, REJECT, CLOCK_TICK, etc.) [Unverified]. Settings: sound and haptics toggles on the first screen's menu and in Settings.

### C5. Metrics to add [Spec]
Step-to-step drop-off, time per step, number of back presses, reroll of names, debt-outcome drop-off (does the Exit Liquidity result cause quits?), time on lottery, error rate on name. These tell us if one decision per screen helped.

---

## D. Do / don't checklist for UI agents

**Do**
1. Ask one question per screen, with one primary action.
2. Keep the CTA in the same spot (bottom-right), disabled until answerable, with a hint saying why.
3. Show a top progress bar and a back button on every creator step.
4. Make cards big (min 72 dp high, 48 dp min target, 8-12 dp apart) with icon + title + one perk line.
5. Give every button and card a lip and a press-down equal to the lip depth.
6. Use Lilita One with a thick plum outline for titles and buttons; at most six sizes; nothing below 14 dp.
7. Use one accent colour per screen; leave space instead of adding borders.
8. React to every choice with a short character line, a tick and a haptic.
9. Make milestones bigger than steps: the draw reveal and the final celebration get the big effects.
10. Write short, second-person copy; buttons in caps with no punctuation; live CT terms only, and clear from context.
11. Pair colour with an icon or text for every state.
12. Respect sound, haptics and reduced-motion settings.
13. Keep the painted town visible behind the UI.

**Don't**
1. Don't put two decisions on one screen or use a scrolling form (today's Personality page does this).
2. Don't use flat light-blue backgrounds, thin blue borders or a yellow button. They are off-identity.
3. Don't copy Duolingo's owl, green `#58CC02`, Feather or DIN fonts, or their sound files.
4. Don't use sharp corners or oval shadows (pill shadows only).
5. Don't use real brand names, token names, handles or exact third-party mascots.
6. Don't use fading slang (ser, wagmi, HFSP) as the main voice.
7. Don't hard-code odds, trait effects or dream lists; read them from the server/registry.
8. Don't make the debt outcome feel like a punishment. Soften with a second bubble.
9. Don't animate anything over 600 ms outside the draw reveal and the final celebration.
10. Don't rely on colour alone, and don't use white text on light green without checking contrast.
11. Don't let the keyboard cover the name field (landscape).
12. Don't skip the 18+ gate or the "game Cash, not withdrawable" line.

---

## E. Unverified items and open questions
- Duolingo's mobile (native) button, card and progress specs. Only the web CSS was read. [Unverified for mobile]
- The lip transition duration (CSS variable not resolved).
- Whether Duolingo's onboarding in Oct 2026 still follows the Page Flows order. The capture date of that page is unknown.
- Duolingo's sound design and haptic patterns. No source found.
- Duolingo's selected-card colours.
- Android `HapticFeedbackConstants` details and Unity access to them on the Seeker.
- The Seeker's exact dp density. All dp values here need a device check.
- Lottery odds, trait effects, dream list and debt mechanics (from the server and registry, not from me).
- Whether the shiba in our title art is cleared as our own character.
- UserOnboard's 78-slide teardown is images only and dated 2015, so I could not read it.
- No `[Measured from screenshots]` values exist in this doc: none of the teardowns I could reach contained screenshots I could measure. Everything labelled Measured-CSS comes from the live web stylesheet.
