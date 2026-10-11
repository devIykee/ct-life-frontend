# CT Life presentation correction — 5 October 2026

Design recommendation, not user approval. The user rejected the entire prior UI, including typography, icons, character presentation and audio. Quality is part of the build, not a later polish phase.

## References actually inspected
- Pinterest search: life simulation game ui design.
- https://www.pinterest.com/pin/127508233194989371/ — inZOI creator: full-height character, neutral studio, peripheral controls, visual thumbnails. Use the hierarchy, not desktop sizing or borrowed artwork.
- https://www.pinterest.com/pin/1548181186834509/ — Betterfly by Further (linked from pin to https://further.group/work/betterfly): narrow currency row, small consistent outline navigation, world/content occupies most of phone. This is a layout reference, not an actual CT game or evidence that its mechanic fits.
- https://www.pinterest.com/pin/958492733183582655/ — Sims 4 environment reference previously open in the user's research: readable adult people within a coherent, bright inhabited world.

## Critique of installed screenshots
- The game cash card is louder than the character. The creator reads as a long account form with a tiny faceless mannequin.
- The city avatar is about 50 px high in a 1200 px-wide capture. Camera distance 52 hides the actual character work.
- Repeated oversized bold text, all-caps eyebrows and large rounded cards make every item equally important.
- Hand-drawn icon approximations are inconsistent and do not establish a polished icon family.
- Choosing named hair/clothing that does not change the visible asset breaks trust.
- Files existing in Audio/ does not establish that interactions sound intentional or that music/voice are mixed well.

## Direction to implement and inspect on the Seeker
- Modern adult life-sim with a character studio and restrained game HUD.
- Warm mineral/offwhite surfaces; charcoal/aubergine ink; one bright lime accent for selected/primary actions. Financial positive/negative colours remain semantic, not decorative. Palette remains a recommendation until viewed.
- A real bundled typeface (Manrope), static regular/semibold weights. Readable body text, limited headline size, no wall of bold headings.
- Coherent sourced Phosphor family; regular for utility controls, optional filled active states. All navigation has labels or tooltips; touch targets stay at least 44 logical dp.
- Creator: large live character/face, thumb-reachable controls, concise copy, exact working looks only. No fictitious modular choices. The full customizable system is still required; one look is an integration checkpoint.
- HUD: compact balance/location treatment, small needs summary, lightweight corner controls. Zoom in so the character is recognizable; keep touch navigation and pinch zoom.
- Phone: same typography/icon family and spacing, hierarchy of app choices without loud competing badges or arbitrary colours.
- Motion: short subtle transitions, stable layouts; no gratuitous wobble.
- Sound: quiet tactile selection, distinct open/back/confirm cues, no doubled click events. Ambient sound and music must not mask speech. Retain separate volumes. Source/credit every external asset.

## Acceptance evidence
- Actual Seeker creator full-body + face images, with the downloaded model visible.
- Actual city movement with a visible authored character and normal animation.
- Sign-in, phone home, map and representative detail screen use the same type/icon/spacing system.
- Touch/drag/face-toggle/back behaviour checked; read-only fixtures cannot submit to backend.
- Audio trigger/missing-file/level checks, with listening not claimed unless actually performed.
- Performance measured with the authored Humanoid model, not inherited placeholder metrics.
