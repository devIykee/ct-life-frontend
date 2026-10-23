# CT Life v3 backlog (coordinator's working list)

Read this on every wake-up. Pick the top unblocked item, run it, then update this file. Branch: `feat/v3-studio-world` in
`/Users/mac/Documents/codes/projects/ct-life-repair`. Started 2026-10-21. The user wants the full game built to their
taste and landed as PRs, with work continuing without prompts.

## How we work (user rules)
- **Look:** clean semi-real, studio quality. Use real ready-made models (Sketchfab, Poly Haven, ambientCG); never generated
  stand-ins or synthesized audio. Use fictional, unbranded vehicles. Vet provenance (no AI meshes, rips or NC licences).
- **One Unity agent at a time**, cost-conscious; do small jobs as coordinator. Verify on the Seeker
  (`SM02G40619141343`): screenshots for looks, screen recordings for motion (never judge motion from stills). Don't
  over-test.
- **Push every frontend change to both repos:**
  - `origin` (`Immadominion/ct-life`): PR #1 `feat/v3-studio-world` → `main`.
  - `iyke-fe` (`devIykee/ct-life-frontend`): a sync commit on top of `sync/v3-studio-world` → PR #3 into `dev`. Use
    `git commit-tree HEAD^{tree} -p <remote sync sha>` and push sha:refs (zsh: `"${C}:refs/..."`).
- **Backend fixes** go as PRs to `devIykee/ct-life-backend` from `projects/ct-life-backend-pr` (its `.env` points at
  the throwaway DB `ctlife_pr_test`). Never edit `projects/ct-life-backend`.
- **Sketchfab:** 8 download calls per window, resetting within ~7 h. Use the logged-in Chrome, `fetch('/i/models/<uid>/download')`
  plus the anchor-download JSON trick, then curl into `ct-life/art/raw/sketchfab/<uid>/<uid>.glb`. The last window
  opened 2026-10-23 06:20 UTC; the files were LOST (see below). Next window ≈13:20 UTC.
  Curl recipe: quote `--proto '=https'` (zsh expands bare `=https`). Only delete `~/Downloads/sfdl_*.json` after the .glb
  has size > 0. Chrome saves at most 7 anchor downloads per page, so do 7 per page or reload the page between.
- **Money rules:** only scarce items become Metaplex Core NFTs (hovercar pass, drops, art, later deeds); Cash never
  buys anything tradable; homes are game-only for now.

## Done (v3)
District fix-ups (2026-10-23): mountain terrain without stripes (triplanar cliffs, scree, alpine, snow), closed boulders, an intact islet with a path, real venue architecture for CHECKPOINT 26, Perp Pit, Basecamp Lodge and the beach hut, the Trenches walled, 54 power poles with wires.
District fix-ups (2026-10-23): mountain on scanned tri-planar rock + scree with slope-aware weights (the sine strata stripes removed), boulders by cliff bands / ridges / cliff feet, islet shards fixed (island-tree wood) + a footpath, real architecture for CHECKPOINT 26 / Perp Pit / Basecamp Lodge / the beach hut (world_venue_shells.py), Trenches lot halls, power poles with wires, last toy road lamps. Evidence `ct-life-evidence/world-fixups-20261023/`.
Forests + districts 2 (2026-10-23): scanned Poly Haven forest trees island-wide, Builder Hills/Airdrop Farm/Boardwalk/Breakpoint/Basecamp/islet dressed, toy rocks/crops/palms/poles removed, sofa sit fixed.
World polish (2026-10-23): seated three-quarter camera, semi-real terrain and ground cover, Trenches yards and compounds.
Vehicle fleet with 4 LODs; hovercar gating; downtown, Trenches, Residences and Marina on the v3 kit; mocap locomotion;
GTA-style HUD; collisions; the shadow fix; the perf pass (30 fps cap holds); the walk-in apartment. Backend PR #6
(vehicle kinds, entitlements).

## Queue (top = next)
1. **[running]** District fix-ups 2 (left from the pass): the mountain cone is still mostly bare rock from afar (the
   heightfield is steep; the cliff/stone Sketchfab packs could dress the faces); more venue shells on world_venue_shells.py
   (ct_jail, launchpad_tower, rekt_clinic, terminal_cafe, sybil_barn, ore_refinery, ...); the device mtn_view/power_poles
   route shots look at a grass bank (use the editor captures fx_mtn_air / fx_poles).
2. **Sketchfab batch** (next window). Still to download (the 06:20 UTC batch was lost to a curl quoting bug): bedroom 4f3db3cb, bathroom 9ba7e0a0,
   cafe a945f202, pub a6534141, office 5540183d, container 772f4be3, crane 49851dc7, lilac bushes 10312697. Next: dining df3f3c9f, Contemporary Apts 0d928b4f,
   American House 798ffaea, cliff 75fb6b34, stone pack f3e0a67b, pigeon 797d27b6, seagull 73aed843, snake 83c4290c,
   dog 9abfce88, cat 0b86bde7, bus station 8c984e73, dumpster 6caa33dd, fence f2ed9f55, palm d05c8ffb.
3. **Interiors:** the ~8 reusable types (apartment done; bedroom, bathroom, cafe, shop, office, bar/club, gym) behind
   real doors; every venue gets a real interior. The m99_storefront shop interiors need rescaling.
4. **Draw calls:** ~740 at eye level vs ≤350. Traffic cars drawn per part (merge static parts), avatars, venue
   dioramas.
5. **Remaining districts on the v3 kit:** Builder Hills, Airdrop Farm, Boardwalk/beach, Breakpoint Park (convention
   centre), MtnDAO mountain/lodge, the islet. Timeline Square plaza and the venue landmarks are still toy-shaped.
6. **Home and rent flow (FE + backend PR):** a lease maps to a flat/door; the owner's door unlocks; visits stream the
   room; tiers (street/shelter, basement, flat, penthouse). Design: `ct-life-evidence/systems-research-20261022/ownership-housing-shops.md`.
7. **Duo Shop + scheduled doorstep delivery (FE + backend PR):** furniture catalogue, delivery windows, a courier NPC/van, a
   parcel at the door, furniture placement in the home.
8. **NFT art frames:** show your wallet's NFTs (DAS) framed at home, with report/block buttons.
9. **Hovercar sale:** the Core collection, the Duo Shop page, the `HOVERCAR_COLLECTION` env.
10. **Police "Sleuth heat" + carjacking:** a heat system, police AI using the police cruisers, the Steal prompt (the HUD is
    already built), an original parody sleuth character (no ZachXBT likeness).
11. **Life:** pedestrians density and variety, animals (pigeons, gulls, dog, cat, snakes), ambient audio pass (real
    recordings).
12. **Small gaps:** remote horn, haptics helper, hovercar glass and visible driver, washed-out sun-side sky, 2 pre-existing
    EditMode test failures.

## Waiting on others
- Iyke: merge and deploy backend PR #6 (migration 081); then flip `WireV3Kinds` on in the client.
- User: keep the Seeker connected with USB debugging authorized ("Always allow").
