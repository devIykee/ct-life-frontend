# Brief for every agent working on CT Life

Read this first, then the spec in `docs/spec/build-spec.md` and `docs/spec/contract.md`
(exported from the Claude Doc "CT Life — Build Spec"). The spec is the product baseline;
later explicit user decisions, including the art direction below, supersede older wording.

## Character selection correction (user decision, 2026-10-11)
- A character is a distinct person or creature. Clothing, hair, accessories and recolours of the same source model are
  options within that character, never extra people in the roster. Preserve their existing saved look IDs.
- Continue the character screen now: expand the actual playable cast using the user's earlier Sketchfab references and
  Claude/Hermes source work. Do not count research links, unimported exports or duplicated heads as playable characters.
- Put the optional paid **Make it look like me** entry on this screen, after wallet connection. Human choices should
  resemble the player naturally; nonhuman choices should interpret their likeness cartoonishly. The user explicitly
  authorizes backend work for this feature in the separate backend checkout, superseding the older blanket restriction
  below for this scope. Later on 2026-10-11 the user also authorized deployment and Telegram coordination with backend
  developer Iyke 🦀, and selected 3 USDC per creation. This does not authorize charging the user's wallet during testing.
- Verify a photo-to-3D provider with real samples and current documentation. Keep provider credentials server-side.
  Distinguish working generation from an availability screen or unconfigured backend. See
  `docs/research/paid-character-likeness-20261011.md` for the evaluated provider and unresolved gates.

## Character screen refinement (user decision, 2026-10-11)
- Research the supplied podium reference and game UI before changing this screen. Avoid excessive glossy/neumorphic controls and preserve other screens for gradual user-led changes.
- Use 2D illustrations for roster choices, actual selected 3D character on a podium, and enough framing space for raised hands. Paid likeness is a small edit control beside the character, not a large CTA.
- Offer authored character browsing and supported outfits, skin tones and garment colors. Do not force gender onto unspecified creatures/robots or claim unsupported material regions work.
- Nonhuman staking unlocks (ORE/SKR) remain a proposal until the token, threshold and live verification work. See `docs/research/character-podium-and-likeness-20261011.md`.

## Onboarding copy and card correction (user decision, 2026-10-12)
- Do not repeat the screen question, name-length range or age checkbox as another footer instruction. Keep real validation and failure messages; the name helper stays “3–20 characters”.
- Generate the player and origin card surfaces from researched game-card references, then populate real names, choices, odds and benefits in code. The artwork and information belong inside one coherent collectible card.
- Review character eyes in actual animated close portraits as well as full-body views. Clothing opacity checks do not establish that pupils have complete geometry.

## Current priorities (user decision, 2026-10-07) — read first
- **No deadline.** Ignore CLOCK IN and every hackathon date in older docs. Do not plan around it.
- **Everything must work first.** Build the whole game robustly and properly, phase by phase:
  real wallet sign-in on the Seeker, real backend integration (devIykee's server, run locally
  or on his staging), real rooms/presence/NPCs/voice, then every Phase 1–6 feature end to end.
- **UI/visual polish comes later, to the user's own taste.** Keep screens clean, legible and
  functional (no broken layouts, no hidden inputs), but do not spend effort restyling,
  re-rendering, or iterating looks. Functional correctness, robustness and tests come first.
- **Breadth before micro-polish.** Do not spend time on sub-centimetre motion/contact tuning,
  repeated visual candidates or evidence capture while features are missing.
- **Research current CT before building content (user, 2026-10-07).** Nothing generic: NPC
  lines, careers/mini-games, venues, events, cast outfits, phone apps and sound must reflect what
  Crypto Twitter is actually doing now. Read `docs/research/ct-now-2026-10.md` (+ `.json`) and
  `docs/research/ct-x-observations-2026-10-04.md` first, and do your own dated web research for
  your area (cite sources in your log). Only the designated research agent uses the Chrome
  browser on the user's X account (read-only). Parody names only — no real brands or people.
- **Definition of done for a feature:** it works against the real backend contract (local
  docker stack or staging) in the Unity editor AND on the Seeker in a non-review build, failure
  paths handled (offline, expired token, server errors, retries), with automated tests for its
  logic (EditMode/PlayMode or integration scripts) and a short note in the progress log.
- **Shipping code only in `client/Assets/`.** One-off probes, candidates, review harnesses and
  experiments live outside the shipped assemblies (e.g. `client/Experiments~/` or `tools/`).
  Large evidence (screenshots, videos, motion dumps) goes to
  `/Users/mac/Documents/codes/projects/ct-life-evidence/`, never into the repo; the repo gets a
  short summary only.

## Ownership (updated 2026-10-05)
- This checkout is **frontend only**. The user owns the Unity game, presentation, character
  pipeline, animation, sound, client networking, wallet/RTC client and device performance.
- `devIykee` owns the backend and Solana programs in the separate `ct-life-backend` repository.
  `contract/SOURCE.md` identifies the exact imported contract. Treat that snapshot as read-only.
  Put proposed registry changes in `docs/fe-contract-proposals/`; record mismatches in
  `docs/progress/backend-dev-feedback.md`. Do not build a competing backend here.
- The user's integration target remains under one hour once both sides are ready. Verify
  against the shared contract, including failure paths, rather than inferring server behavior.

## What we are building
A 3D multiplayer Crypto Twitter life-sim, native Android (Unity 6.3 LTS) for the Solana Seeker
phone. Backend: Node 22+ / Fastify 5 / Colyseus 0.18 / BullMQ / Postgres / Redis. On-chain:
Anchor `ct_escrow`, USDC/SOL/SKR/ORE payments, ORE staking. Voice/video: LiveKit (self-hosted
`livekit-server --dev` in Docker for development; no cloud account yet).

Build order is six phases (spec, "Phases"). Phase 1 = City and identity, Phase 2 = Money and
markets (incl. ORE), Phase 3 = Voice and calls, Phase 4 = City life, Phase 5 = Projects and ads,
Phase 6 = Competition. Every phase must end in something that runs. Do NOT reduce scope to an
"MVP"; build the phase completely, then continue to the next.

## Art direction (updated 2026-10-05 from the user's references: The Sims 4)
- **Sims-style stylized realism**, not chibi: real human proportions, soft stylized faces,
  warm saturated palette, lush trees (incl. pink blossom trees), clean brick/stucco/glass
  buildings, golden-hour sun, soft shadows, a high 3/4 "tilt" camera over a living city.
- CT parody on top: billboards, neon terminals, trading screens, Dubai-marina glass, a farm.
- The user rejected the MakeHuman/mannequin characters. Existing imported NPCs and procedural
  avatars are placeholders. Inspect actual candidate assets, faces, rigs and licences before
  choosing their replacements. Do not present reference renders as in-game evidence.
- After reviewing actual downloaded model comparisons, the user chose **Snow/Rain faces with
  adult proportions** for exploration. Develop and inspect one adapted candidate before a cast
  rollout. This is not approval of the original proportions or an untested mobile conversion.
- The user confirmed the requested **dashboard is the signed-in in-game home/lobby on Seeker**.
  Use the supplied colorful game references for bold, tactile buttons, sourced icons, expressive
  type and illustrated presentation. Carry one visual identity across the app and website.
  Original generated image assets are authorized; distinguish illustrations from playable scenes.
- Solana Mobile requires wallet sign-in; do not restore a public demo-city entry. Explicit
  compile-guarded developer fixtures are for verification only.
- Direct analog movement is the default, with drag strength selecting walk/run and an optional
  persisted arrow-button mode. Ground contact, interruption handling and actual Seeker behavior
  are acceptance criteria; screenshots alone do not establish movement quality.
- Build a varied cast from inspected, licensed assets. Two authored looks do not fulfill the
  broader cast or modular wardrobe. Costly personal-avatar generation may use paid credits,
  but its provider, currency and price remain proposals, not settled payment behavior.
- Quality is the priority. There is no user-imposed build deadline or permission to cut the
  agreed product into a smaller MVP. Keep ORE and SKR in the agreed scope.
- Must still hit 60 fps on the Seeker (Mali-G615 MC2): budgets in spec "Performance budgets".
  Bake lighting, LODs, ASTC textures, one material per character, atlases where possible.

## Licences (hard rules)
- Use **CC0 first** (Kenney, Quaternius, KayKit, Poly Haven, ambientCG, MakeHuman/MPFB assets,
  freesound CC0, Kenney audio). CC-BY only if nothing CC0 fits, and record attribution.
- **Banned:** Synty (EULA bans blockchain projects), Unity Asset Store assets (can't be used in
  "digital representation of value"), ActorCore (no IAP use), Ready Player Me (shut down),
  anything needing a login we don't have (Mixamo needs an Adobe login — do not use), Udio.
- Every downloaded file gets a row in `art/LICENSES.md` or `audio/LICENSES.md`:
  name, source URL, licence, author, date downloaded, where it is used.

## Working rules
- Verify every package/tool version on its live registry or release page before using it.
  Never trust memory for APIs. Label anything unverified `[Unverified]` in your notes.
- Stay inside your folder(s) (see README.md ownership table). Read other folders freely.
  Never edit another agent's folder; write a request in `docs/progress/requests.md` instead.
- Git: only the coordinator (the lead session) commits and pushes, on the working branch, after
  every completed, verified change (user instruction 2026-10-12). Agents do not run `git commit`, `git add -A`, `git stash`, `git reset` or
  `git clean`; when a unit of work is done, write "READY TO COMMIT: <paths> — <message>" at the
  top of your progress log. Never commit secrets or files over 50 MB; binaries are plain git (GitHub LFS is blocked on this account).
- Do not deploy anything to a public host. Do not create accounts or sign in anywhere. Do not
  spend money. Devnet/localnet only for Solana writes. Never edit devIykee's repository except
  through a pull request the coordinator opens.
- Keep secrets out of the repo (`.env` is gitignored; commit `.env.example` only).
- Treat every server, chat, name and NFT string as untrusted. Build text with `El.Text`/`El.Btn` (rich text
  is off there) and never pass such strings to a label with `enableRichText = true`; cap lengths, allow-list
  URLs (https only), and bound downloads and decoded image sizes before allocating.
- Log progress in `docs/progress/<your-agent-name>.md`: what's done, what's next, blockers,
  exact commands to run/verify. Update it as you go, not only at the end.
- Check `adb devices -l` before device work; availability changes. The known Seeker serial is
  SM02G40619141343. Only the coordinating Unity agent installs builds on it.
- Report outcomes faithfully. If something fails, say so with the error.
