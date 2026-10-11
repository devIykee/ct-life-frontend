# Combined typing furniture review

The saved FK typing clips passed independent fresh-process native replay. Native furniture capture v2 passed both 241-sample cases and exact source-surface checks with zero protected drift. Its PNGs repeated across some requested phases, so they establish static views only. The frozen v3 source now adds original keyboard appearance and verified current-pose CPU snapshots; v3 native graphics remains pending.

## Fixed shared workstation

- Chair: actual licensed `office_chair`, yaw 180°, anchor-relative center `(0,0,-.31)`, scale `(1,1,.8)`. Its curved support mesh remains frozen; no single plane is substituted.
- Desk: actual native-exported `home_desk`, center Z `.5487464`, Y scale `.74/.76`. Source mesh front is Z `.20`, top `.74`; full target X is about ±.750651, back Z `.897493`. Existing native source triangles are transformed; actor/anchor roots remain exact and both looks share the same desk.
- Keyboard: frozen licensed Kenney keyboard mesh, width `.46`, base Y `.74`, front/back Z `.205/.355`. The main typing field remains X `-.19889..+.09103`; the right region is the numpad. Its hash and geometry are unchanged from motion's accepted hand target.

`shared-typing-desk-target.json` records exact source/export hash, transform, bounds and target SHA256. All target meshes are frozen for this comparison.

## Completed CPU audit

`review_combined_typing.py` checks the actual calibrated transient hand/body V2 exports against all three physical targets. `combined-typing-hand-v2-surfaces.json` holds 48 cases (eight phases, three LODs, two looks) with input hashes. Chair and desk tested triangle crossings are zero; posterior seat-ray penetration beyond 2 mm is zero. Snow intersects 0–2 keyboard triangles at sampled phases, consistent with the prior native hand minimum gap of −0.111 mm. Rain has no tested keyboard crossing. Shoes remain approximately +0.50 mm above the floor. This is discrete geometric evidence, not continuous-time collision proof or final visual/transition acceptance.

## Coordinator-only native procedure

Import `CafeTypingFitReview.cs`, `CafeFurnitureGeometry.cs` and `../terminal-cafe-final/CafeKeyboardAppearance.cs` into `client/Assets/Scripts/Editor/` only when the coordinator releases imports. The original B3Social prefab must exist; it is read and cloned in an isolated preview scene, never saved or modified. Run `CTLife.EditorTools.CafeTypingFitReview.Capture` with graphics; `CaptureGeometry` explicitly omits images and is not a visual pass.

The helper refuses to start unless `motion-review-20261006/native-typing-persistent-replay-v1/receipt.json` reports `review_revision=fresh-process-persistent-replay-v3`, global and per-look `all_pass=true`, per-look `persisted_replay_verified=true` and `replay_source=saved-asset-loaded-in-distinct-fresh-process`, empty protected drift, `playback_foot_ik=false`, `playback_hand_ik_pass=false`, zero playback IK callbacks and an exact current clip hash. It takes `candidate_asset` from that receipt. A successful transient OnAnimatorIK comparison cannot satisfy this gate.

The preview uses current real Snow/Rain world prefabs at immutable local `terminal_1`, with a temporary single act_type state at 1×, Foot IK and layer IK pass both OFF, no hand driver, and no actor-root translation. The actual sourced desk/chair are cloned and transformed to the shared profile, then every oriented LOD0 triangle surface is matched bijectively to the frozen target (20 µm maximum corner-distance tolerance; UV2 vertex splits and index order may differ). The keyboard now restores both original material groups, source normals and source UVs (retained in UV3). Original restrained key legends cover the existing two plate surfaces; there are no raised keys. The geometry still matches the fixed contact target with original triangle multiplicity. The preview and final builder share the same appearance source; native final-material inspection remains necessary.

After two actual warm loops, 241 native phase samples must advance correctly. The helper records planted shoe minima and weighted posterior ray-to-curved-seat gaps, exports all body LODs plus chair/desk/keyboard at eight phases, and renders front, side, keyboard-close and isolated landscape-distance views at three phases per look (24 PNGs). Output: `native-typing-fit-review-v3/`. Each image uses a CPU BakeMesh snapshot verified within10µm of the same measured native pose, with original skin materials/property blocks and the full source transform chain; original GPU skins are hidden only during capture. Per-phase native time, vertex hash, image hashes and pose error are retained. When native visible geometry moves at least5mm, at least one contact image must change. The landscape view uses actual CameraRig look-ahead with final Café values (10.8m,30° pitch,−12° yaw,45° FOV,feetY.30,focus height1), but shows isolated furniture and does not establish full-room composition. Receipt status remains `captured_not_combined_furniture_accepted` until independent image/full-triangle inspection. Protected dependency, receipt and target hashes must remain unchanged.

The final room builder, actual controller transition, world-tap approach/claim/stand, monitor/laptop composition, lightmaps and device/connected acceptance remain separate. The current B3Social/B3Hits work furniture is still B2 layout and must not be registered as a completed workstation.

The revised gate also verifies a recorded positive process ID and start time after the exact frozen author receipt timestamp/hash, and pins the two already saved clip hashes. Failed authoring evidence is preserved; it is not relabelled as a passed replay.

## Preserved topology failure and revised exact surface gate

The first native furniture capture refused the office chair before posing because its unbaked source vertex/index arrays differ from the baked B2 export. Original failed receipt remains in `native-typing-fit-review/`. The source bake pipeline creates isolated UV2 meshes; frozen office target has 3797 vertices/1096 unique positions/2116 triangles, desk3783/1354/2640. These counts alone do not establish identical geometry.

The shared `CafeFurnitureGeometry` verifier now records actual/target meshes, source asset, raw and unique vertex counts, index equality, transforms, bounds and unmatched triangle indices, then requires a one-to-one match of **every oriented triangle and every corner** at the original20µm tolerance. Only cyclic corner permutations are allowed; reversed winding is rejected. Equal triangle multiplicity is mandatory. This is not a bounds-only or nearest-vertex replacement. New output is separate (`native-typing-fit-review-v2`), and final builder uses this same verifier. Native evidence must establish whether UV seams/index order are indeed the only difference.

## Persisted clip surface result and bounded appearance correction

`review_native_typing.py` and `native-persisted-typing-furniture-surfaces.json` audit the actual saved-clip native v2 combined OBJ exports: two looks × eight phases × three LODs. Tested chair/desk triangle crossings and posterior penetration beyond2mm are zero; Snow has0–2 keyboard triangle crossings, Rain0. All sampled sole minima stay about+.50mm. These are discrete edge/triangle and support-ray tests, not continuous-time or coplanar full-volume proof. No further pose tuning follows from this evidence.

The original neutral keyboard preview carried into the draft final builder and rendered as a black slab. This was an appearance defect, not the appearance of the full licensed source. The source has two materials and flat keybed plates, without individual raised keys or an image texture. Authorized correction restores those materials/normals and adds original QWERTY/numpad artwork using the existing Manrope font. No contact shape or hand target changes. `../terminal-cafe-final/keyboard-detail/source-surface-check.json` confirms all64 original oriented triangles match within0.028µm in CPU source conversion; the native surface verifier remains mandatory.

`typing-fit-review-source.json` and `../terminal-cafe-final/keyboard-beauty-freeze.json` freeze the v3 source/import set. The final builder requires the completed v3 graphical receipt, same texture hash,12 images per look, verified snapshots and exact current PNG hashes. Manual art inspection, actual full-room gameplay images and fitted seat-exit acceptance are distinct pending gates. Original failure and v2 output are preserved.
