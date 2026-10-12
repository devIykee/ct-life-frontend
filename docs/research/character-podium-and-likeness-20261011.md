# Character selection — reviewed direction, 2026-10-11

This continues the existing Claude/Hermes cast and onboarding. It does not replace the surrounding app design.

## References inspected before implementation

- [Łukasz Zagdan — Super Brawl Game, original project](https://www.artstation.com/artwork/nY1PX9), reached from the user's [Pinterest reference](https://www.pinterest.com/pin/256916353738321353/). Inspected the actual images, including the central character on a gold/purple circular plinth. Adopt the large unobstructed character, distinct roster and small adjacent controls; omit unrelated rank/stat panels.
- [Unity 6.3 Button](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-uxml-element-Button.html) and [supported USS properties](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-USS-SupportedProperties.html) checked before UI changes.
- [Tripo Studio](https://studio.tripo3d.ai/) inspected in the logged-in browser. The displayed $7/month Pro promotion was billed annually at $84, not a standalone $7 monthly purchase. The displayed standard monthly price was $20. Studio's 200 credits were separate from API balance. No subscription was purchased by an agent.

## Current implementation decisions

- Ten distinct identities remain ten identities. Snow/Rain clothing variants stay inside their respective wardrobe. Character cards use new source-referenced 2D illustrations; the large selected character remains the actual playable 3D model.
- Browse All / Men / Women / Others. This is authored roster metadata, not gender inference from user photos. Unspecified masked characters, robot and creatures remain under All/Others. Browsing never changes the selected identity automatically.
- Selected character stands on real preview plinth geometry. The square render surface has room for measured reactions and extends behind the surrounding layout. Home preview keeps its previous default behavior.
- Paid likeness entry becomes a small pencil alongside the name, with a small Paid marker. No price is invented. Full-body and face framing remain flat adjacent icon controls.
- Existing reviewed Dex/Nova masks support ten skin tones, twelve hair colors and sixteen clothing colors independently for top, bottom and shoes. Original materials remain untouched. Snow/Rain currently support authored outfits only; their skins and garments do not have reviewed region masks.
- Palette variations preserve the seven-field Look contract. Both runtime art resolution and fresh server allowlists must accept a choice before submission. A cache or an imported local material does not grant server capability.

## Paid likeness trial

The user authorized API testing with a maximum of 200 credits. One operator owned spending. Two completed operator trials (human Snow and Froggy, using the same fictional adult face) each consumed 75 credits: 10 conditioning, 40 mesh, 0 pre-rig check, 25 rig. Actual total is 150, with 350 available and 0 frozen after the user's 500-credit funding. The remaining 50 credits of this task's authorization were not spent. No user's personal photo was used. Exact task IDs, outputs and reconciled accounting live in external evidence and the backend progress report.

Recommendation: sell personal face identity, and keep ordinary wardrobe/skin/color editing separate. This is a product recommendation, not a verified Tripo capability. Initial reference conditioning visibly changed the stock character's torso/proportions and style. Tripo must not be advertised as preserving our exact body, topology, wardrobe or Humanoid rig until those are actually demonstrated. A successful provider job also does not establish that its downloaded model is safe and performant in the game.

The human result has coarse facial detail and changed proportions. The frog preserved recognizable species features and interpreted hair/beard cartoonishly, but invented accessory detail and rebuilt the full body too. Both are rigged replacement GLBs, not approved face-only edits; neither was imported/equipped in the game. Public likeness remains disabled.

## Nonhuman unlocks / ORE

The user is considering staking-based unlocks, with ORE or SKR not finalized. No lock threshold, currency or entitlement is invented here. The supplied prize image requires a live user-facing ORE integration, primary promotion where competing products exist, continued support and milestone reporting. Eligibility has not been verified.

Read-only backend audit found `/ore/me` returns zero balances/stake and that the stake builder differs from the official [ore-stake program](https://github.com/regolith-labs/ore-stake). Those are blockers to an honest stake-based gate. No chain transaction is attempted and no client-only lock is presented as staking integration.

## Evidence / limits

See `docs/progress/character-podium-20261011.md`, character illustration provenance, backend likeness report and coordinator verification report. Do not call the phone/photo/payment/equip flow complete based on an operator-generated mesh.
