# Solana Duo — iOS redesign spec (2026-10-16)

**Approved visual target:** `ct-life-evidence/duo-redesign-20261016/duo-redesign-board-v1.png`
(source `index.html` beside it, 450×562 CSS px per screen = the Duo glass at ½ device scale).
**User references:** `ct-life-evidence/duo-redesign-20261016/codex-context/` (toast/tag/menu/metric/form/task/composer/toolbar
references from the user's last Codex brief, the iPhone fold-leaf home reference, the game-lobby references).

## What the user asked (verbatim intent, 2026-10-15/16)

- The Duo home layout is liked, but it does not *feel* like an iPhone: icons, text, materials, "that iPhone squircleness".
- App interiors are bland: "all containers", "box box box". The layout is not smart. Study each reference, ask how *they*
  solved it, and use that idea where it belongs. Not copies.
- Text is too small. Default slightly bigger, and the player controls it in Settings.
- It is a phone: players receive message notifications and chat with other players, like a real phone.
- Wallet: do it smartly.

## Principles (apply to every app)

1. **Content sits on the canvas, not in boxes.** Lists are full-bleed rows with *inset hairline separators* that start at
   the text column (iOS). A white card is allowed only when it is a real object (a wallet card, a featured item, a chat
   bubble, a settings group on grey). Never a card that only holds a heading and text.
2. **Hierarchy comes from type, not borders.** One large title per screen (bold, tight tracking), then section headers,
   then rows. Numbers that matter are heroes: big, bold, no container (Bank balance, price, net worth).
3. **One primary action per screen, placed where the thumb is.** Floating glass bottom bar (Long/Short, Compose,
   Send). Row actions are App-Store "GET"-style capsule pills on the right of the row (Work, Notify, Join, Buy).
4. **Glass is for navigation and controls only** (dock, search, nav buttons, bottom bars, composer, banners). Content
   stays opaque.
5. **Colour is semantic and sparing.** iOS system colours: blue #0A7CFF actions, green #30C75E gains/positive,
   red #FF3B30 losses/destructive, orange #FF9500 warnings. Each app gets an identity colour only in its icon and its
   hero element.
6. **Details open in sheets**, not by growing the page with inline forms. Confirmation and forms are bottom sheets with
   a grabber, one primary button and a quiet secondary.
7. **Real state only.** Never invent numbers, charts or statuses that the backend does not provide. Empty states are a
   friendly line plus one action, centred, no box.

## Type scale (Duo panel units; Inter as the licensed SF stand-in)

Default size = **Large** (≈ iOS proportions on the Duo glass; ~15–20% bigger than the 2026-10-15 build).
Implement as USS custom properties on `.ct-phone`, switched by a root class from the Settings slider.

| Token | Large (default) | Weight | Use |
|---|---|---|---|
| `--duo-large-title` | 66 | Bold, tracking −1.5 | One per screen |
| `--duo-title2` | 44 | Bold | Hero numbers (price/balance use 76–84 Bold) |
| `--duo-title3` | 38 | SemiBold | Section headers ("Latest transactions") |
| `--duo-headline` | 33 | SemiBold | Row titles, names |
| `--duo-body` | 33 | Regular | Messages, post text |
| `--duo-subhead` | 29 | Regular | Row subtitles, previews (secondary colour) |
| `--duo-footnote` | 26 | Regular/Medium | Timestamps, meta |
| `--duo-caption` | 23 | Medium | Icon labels, badges |

Sizes: XS 0.82 · S 0.9 · M 0.95 · **L 1.0** · XL 1.12 · XXL 1.25. Persist in PlayerPrefs (per device). Icon grid labels
stay fixed (iOS does not scale home labels beyond XL).

## Components (one Duo kit, used by every app)

- **DuoNav**: circular glass back/action buttons (72 units), large title below that collapses to an inline centred title
  on scroll.
- **DuoRow**: leading (icon tile 60–100 with 22% corner radius / avatar circle / coin), title + subtitle (1–2 lines),
  trailing (value, value + caption, chevron, pill, toggle). Separator inset to the text column. Pressed state = system
  grey fill.
- **DuoSection header**: title3 + optional blue "See all".
- **DuoGrouped**: iOS inset grouped list on `#F2F2F7` for Settings-like screens only.
- **DuoSegmented**: capsule track with white sliding thumb.
- **DuoPill**: GET-style capsule (grey fill + blue text; filled blue for the primary; lock + level for gated).
- **DuoHero**: label, huge number, delta line (green/red with arrow) + "Today".
- **DuoBottomBar**: floating glass capsule with 1–2 large buttons.
- **DuoSheet**: bottom sheet, grabber, title, content, primary + secondary.
- **DuoSearchField**, **DuoToggle**, **DuoSlider** (text size).
- **Chat kit**: bubbles (received #E9E9EB, sent blue gradient), grouped by sender with a tail only on the last bubble,
  centred timestamps when the gap is > 15 min, inline special bubbles (cash sent, coin card), composer = glass "+" circle
  plus glass pill field with the send arrow appearing inside it when text is present.

## Notifications (new behaviour)

- **Banner**: iOS-style glass banner, top-centre of the *game* screen (≈ 420 css / 900 units wide), app icon, sender or
  app name, "now", up to 2 lines. Shows over the HUD whether or not the Duo is open (when the Duo is open and the
  matching app is in front, no banner, just the content updates). Tap → opens the Duo straight into that thread/screen.
  Swipe up or 4 s → dismiss. Multiple → stacked (newest on top, one peeking card behind).
- **Sources** (real events only): incoming DM (`DmMessage` realtime), mentions/replies, job shift ready, rent/loan due,
  call incoming (uses the existing call flow), Space started by someone you follow. Use what the backend sends; do not
  fabricate.
- **Badges**: red count badges on app icons (Messages unread, Timeline mentions, Jobs ready) and on the HUD Phone
  button. Cleared when read.
- **Settings → Notifications**: banners on/off per app, Do Not Disturb toggle (silences banners, keeps badges).

## Home screen

Keep the approved layout (two widgets, 4-column grid, vertical glass dock + round search on the right, Solana mark +
"Duo" in the status bar). Change the feel:
- **Icons**: a new consistent set, rendered from one source. Squircle 22.4% corner radius, vertical gradient body,
  top sheen, inner hairline, soft drop shadow, a single white filled glyph (Phosphor *fill*, MIT) with a slight shadow.
  Render via `art/source/duo-icons/` (HTML/SVG → PNG at 256 px with headless Chrome), replacing the 2026-10-12 icons.
- **Widgets**: Calendar (weekday, date, next real events: rent due, scheduled Spaces) and Net worth (value, today's
  change, real sparkline from server history if available, else no chart).
- **Labels**: Inter Medium, caption size, white with a soft shadow.
- **Wallpaper**: abstract blue → violet → peach gradient (original), with a Settings choice later.

## App blueprints

| App | Layout idea (reference it borrows from) |
|---|---|
| Messages | iMessage: large title, search, conversation rows (unread dot, avatar 100, name, 2-line preview, time + chevron), glass filter bar + compose button. Thread per board #4. |
| Wallet / Bank | Apple Wallet: stacked cards (CT Cash with balance on the strip, Loan with due date, connected Solana wallet with real SOL/SKR balances, front card expanded). Below: Latest transactions rows. "+" = Top up sheet. Debt repay from the Loan card's sheet. |
| Perp Pit | Robinhood/Coinbase: asset line, hero price, delta, edge-to-edge line chart (no box, faint dashed gridlines), timeframe text pills, "Your position" key-value grid, glass Long/Short bar. Order entry in a sheet. |
| Jobs / Careers | App Store: segmented Careers/Side gigs, a featured hero card for the best available job, rows with icon tile + pay/time + Work/Notify/Lv pill. Job details in a sheet. |
| Timeline | X: Following/For you tabs with blue underline, posts as rows with hairline dividers, inline coin chips, action row, floating compose. |
| Calls | iOS Phone: Favourites avatars row, Recents list (missed in red), call screen with big avatar and glass control row (reference 8 toolbars). |
| Spaces | X Spaces: stage avatars grid with speaking rings, listeners count, glass control bar (mic/hand/leave), live badge. |
| Settings | iOS Settings exactly: profile header row, Display & Text Size slider, grouped rows with coloured icon squares (Notifications, Sounds & Haptics, Do Not Disturb, Wallet, Privacy, Credits & Licences). |
| Rich List / League | Podium top 3 (avatars, ranks) then rank rows with net worth trailing; your own row pinned at the bottom in glass. |
| Home (needs) | Health app: ring/metric summary at the top, needs as rows with small progress bars and an action pill. |
| Predict | Polymarket-like: question rows with Yes/No percentage pills and volume; bet in a sheet. |
| Shop / Business / Builder / Ads / ORE / Pulse / Week / Profile / Top up | Same kit: hero where a number matters, rows with pills, sheets for actions. Status tags (reference 2) only where a real state exists. |

## Toasts

The 2026-10-15 white toasts stay (reference 1, right column). Use the same banner material so toasts and notifications
read as one system.

## Verification

Every wave: EditMode tests for new logic (notification queue, badge counts, text-size persistence), native captures of
each changed screen at Large and XXL, and a Seeker pass (open each app, send/receive a DM between two accounts to see
the banner, tap through). Compare each capture against the board side by side before calling it done.
