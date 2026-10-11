# Studio F: one shared fitted chair

Staged only. No Unity launch, live source import, profile attachment or registration by this agent.

The populated E2 capture showed both bodies crossing the lounge backs. The native triangle audit also rejected putting the accepted Café social pose into those unchanged lounge chairs. Root chose one shared physical chair assembly for all 30 seats. F clones the exact accepted Café `table_1/Direction_home_chair_1` mesh, transform and truthful collider at each unchanged Studio anchor. It changes neither avatar offsets nor synced positions. Audience seats receive deep teal upper surfaces; speaker seats receive warm neutral upper surfaces; source wood remains on the legs. The original weave and chair are already locally licensed assets; no new package or outside source is introduced.

The source chair's anchor-local support plane is 0.450 m. Its position is local Z −0.30 m, yaw 180°, source scale 1.08 with the previously fitted vertical ratio. The nine native support probes are X −0.12/0/+0.12 and Z −0.36/−0.30/−0.24 m. A CPU read of the frozen target confirms all nine hit Y 0.450 m; native stage independently measures the copied collider. The builder also checks every source/copy rendered vertex and oriented triangle index set, retaining the original collider mesh. This does not substitute for the forthcoming occupied camera review.

Only the 30 chair render/collision assemblies and their corresponding 30 navigation exclusions change. All non-chair triangles/colliders, 34 contract anchors, 31 approach transforms, room identity and camera parameters remain exact. The other 17 saved exclusions remain. Candidate navigation uses the actual 0.30 m agent radius and requires all 34 destinations to remain complete. Stage writes both baseline and candidate paths, representative exact collider OBJ files, per-chair support witnesses and the source byte hashes. E2 and its baked evidence remain intact.

## Import and native procedure

Use `native-import-manifest.json` to verify the two new source hashes, destination absence and source asset hashes. Import both into the listed new Editor paths; do not replace earlier classes. Root owns the native slot.

1. Idle batch stage: `-executeMethod CTLife.EditorTools.SpacesStudioSharedChairCandidate.BuildCandidate -batchmode -quit` with the normal project path and a fresh log.
2. If stage exits 0, idle batch bake: `-executeMethod CTLife.EditorTools.SpacesStudioSharedChairCandidate.BakeCandidate -batchmode -quit` with a fresh log. The bake changes only the new F folder and checks protected source bytes before returning.
3. Fresh graphics Editor launch uses **only** `-openfile /Users/mac/Documents/codes/projects/ct-life/client/Assets/Scenes/Bootstrap.unity` for project entry, followed by `-executeMethod CTLife.EditorTools.SpacesStudioSharedChairRoomReview.Launch -ctlife-studioOutput /tmp/ct-studio-shared-chair-room-native-20261007 -ctlife-studioExit 1`. Do not also pass `-projectPath`, `-batchmode`, `-nographics` or `-quit` for this asynchronous graphics fixture. Preserve any unsaved-scene refusal.
4. After each Editor exits, independently rehash the physical files listed in its protection transaction. Never recreate virtual package aliases. Root can then inspect the actual baked arrival, audience, speaker and host views.

The room fixture checks 31 actual source-furniture rays with WorldInput, then five representative actual tap/walk arrivals (`seat_18`, `seat_1`, `stage_1`, `stage_10`, `stage_host`) and return: 11 walking legs, five exact null-channel seat intents. It records native bound lightmaps when baked. It uses the saved candidate NavMesh and actual LocalPlayerController. The player remains standing: these are selection/navigation checks, not confirmed seating or authority checks. Success also requires cleanup-result exit 0/errors empty and protected-drift empty.

## Next acceptance boundary

After native stage/bake/room checks, render the same explicitly synthetic cast in the copied chairs with the exact accepted social motion as a preview-only assignment. Inspect both looks in front/side and actual room cameras; compare the exported chair geometry to the existing accepted Café target. Canonical Studio `act_sit` has not become fitted merely because the chair changed. No Studio motion binding, seated exit, host/speaker authority, RTC, device budget or registry promotion is part of this builder. The existing feedback item 13 remains the one backend request for the wrong stage action; no duplicate request or backend edit is made.

The Studio stage floor includes its existing thin decorative top. This builder holds it unchanged. Native occupied views must report any visible foot/floor issue rather than hide it with a per-seat actor offset. Instant authoritative seat entry is the current chosen interaction; continuous entry choreography is not an additional blocker.

## Occupied preview helper

`SpacesStudioSharedChairPopulationReview.cs` is a separate new class, frozen in `population-import-manifest.json`. It compiles against actual Editor references. After F's bake and scoped ray/walk review, run `CTLife.EditorTools.SpacesStudioSharedChairPopulationReview.Launch` with the same `-openfile` entry shape and:

- `-ctlife-studioPrefabHash` followed by the exact current passing F baked prefab SHA.
- `-ctlife-studioBakeHash` followed by the exact current F BakeReceipt.json SHA.
- `-ctlife-studioPopulationOutput /tmp/ct-studio-shared-chair-population-native-20261007` and `-ctlife-studioPopulationExit 1`.

It repeats the same 12 explicitly synthetic cast positions and four observer walks as the E2 diagnostic, which makes the material/chair difference directly inspectable. Eleven seat previews directly evaluate the accepted saved Café social state; host remains canonical standing, with one separately labeled existing microphone-gesture audition. The helper checks both saved controllers and social clips against fixed accepted hashes and checks each fitted actor's exact look/own Avatar. This bypass is preview-only: it does not attach a Studio profile or claim canonical Studio act_sit changed.

Actual Animator time must advance on real Play frames. Each frame uses CPU BakeMesh snapshots with source materials/property blocks and verifies the rendered vertices match the sampled pose. Four close-up side views also export actual LOD0 body surfaces. Baked map and renderer counts come from the actual F receipt rather than E2's former counts. Inspect audience/stage front and side contacts for both looks, feet against the preserved floor, clothing/back/calf crossings, clear headline and the shared material family. This remains a bounded synthetic visual/contact review: no exits, voice, server claim, frame-rate benchmark or venue registration. Preserve the E2 rejected canonical-pose images for comparison.
