# Studio E — a readable broadcast room

This is a staged visual revision of the **actually baked** D room. D's arrival view has a large uninterrupted wood field; its host and right-stage views put a suspended beam directly across the room identity. The copied D evidence remains in `docs/progress/screens/studio-d-baked-room-native-20261006/`. E is not yet native-rendered or accepted.

E removes only the non-colliding 21 m suspended beam (12 trim triangles). It changes the existing floor tiles in X −8..8 / Z −10..0 to a muted mineral-green woven material, with no added rug thickness or floor position change. The same restrained original weave goes on the current chair upholstery; feet stay walnut. This is a material improvement to the existing 128-triangle chairs, not a claim that their blocky silhouette or seated contact has been solved.

Three existing licensed Kenney buildings establish a city beyond the room: `building_low_a` at (−21.3, 6.8), `building_low_d` at (−21.3, −5.2), both yaw90, and `building_low_e` at (4,18), yaw180. Native bounds must remain either west of X−13 or north of Z12.5; they have no colliders or interactions. Sources are already recorded in the environment manifest and project license inventory. No new download or dependency is added. The small deterministic weave is original project artwork generated into this candidate only.

All 34 exact anchors, 31 approaches, existing physical furniture/collision witnesses and camera settings are compared against D. E copies the exact D NavMesh asset bytes and requires all 34 sampled paths to equal D. It retains D's seven authored lights for a controlled comparison; a new bake is necessary because visible materials and exterior occluders changed. This does not assert a path proves body clearance.

Import the two unique E classes in `native-import-manifest.json`; C/D classes and failed C evidence remain intact. Both compile against the actual Editor reference set with exit0 (the existing standalone CS1701 Newtonsoft/netstandard warning remains). No live Assets/native work has been performed by this agent.

1. Root-owned idle batch: `CTLife.EditorTools.SpacesStudioCandidateE.BuildCandidate`.
2. If stage passes, bake: `CTLife.EditorTools.SpacesStudioCandidateE.BakeCandidate`.
3. Graphics review: `-openfile /Users/mac/Documents/codes/projects/ct-life/client/Assets/Scenes/Bootstrap.unity -executeMethod CTLife.EditorTools.SpacesStudioRoomReviewE.Launch -ctlife-studioOutput /tmp/ct-studio-e-baked-room-native-20261007 -ctlife-studioExit 1`. Do not combine `-projectPath` with `-openfile`, and do not use `-quit` for the asynchronous graphics fixture.

Review the same arrival, audience, left/right speaker, host and return cameras. The headline must stay unobstructed; fabric should distinguish the audience area without high-frequency shimmer; exterior models must frame the windows without dominating the foreground or darkening the room excessively. Compare actual bound lightmaps and the unchanged paths/rays. Device/crowd cost is still unmeasured for E, and no source count is reported as a frame-time result.

No registry promotion. Backend item13's host/speaker action mismatch, adult seat/stand contact, connected Space roles/voice, crowd/device performance and broader six-phase scope remain open. Phone Spaces UI evidence does not approve this 3D room.
