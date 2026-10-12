# Onboarding refinement — 11 October 2026

The review asks for automatic framing, consistent compact cards, bright white surfaces, real human wardrobe controls, and an animated `Customize` arrow on Make it yours. Preserve the existing cast, illustrated roster, painted town and podium on the character selection step.

## Reference inspected before implementation

[Nintendo Switch Sports — official Nintendo page](https://www.nintendo.com/en-gb/Games/Nintendo-Switch-games/Nintendo-Switch-Sports-2168292.html), including the actual Kit Yourself screenshots `01_05` and `02_05`. Local reference captures are outside the repository in `ct-life-evidence/onboarding-bright-20261011/`.

The useful decisions are a large unobstructed character, compact category navigation, a small palette separated from style thumbnails, white surfaces, and a single clear selection accent. These inform hierarchy; Nintendo artwork is not used in the game.

## Implementation decisions

- Pick your character and Make it yours use full body and podium. Find your people and subsequent questions use a large, noninteractive portrait at the lower left. No view-switch controls.
- The render texture remains fully opaque at the UI level. Transparency in its background must not fade the character itself.
- Make it yours has a horizontal category strip and compact style/palette choices. Only actual fitted assets and verified material regions are selectable.
- Onboarding choice cards share flat white surfaces and restrained borders. Next and Back use flat surfaces rather than glossy button art and extrusion.
- The human likeness entry is a `Customize` arrow tag on Make it yours. The isolated photo flow owns consent, price, upload, progress, generated preview and acceptance.

## Release constraints found

[Tripo developer terms §3.2](https://developers.tripo3d.ai/en/terms) require prior written permission to make the service available to third parties, including end users. Operator testing can establish technical feasibility; public availability needs the applicable agreement. The authenticated Railway account does not currently expose the existing CT Life API/realtime projects. Both questions have been raised with the owner while implementation and local testing continue.
