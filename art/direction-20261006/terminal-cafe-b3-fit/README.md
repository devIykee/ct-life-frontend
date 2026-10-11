# Café B3 social joint-fit target

**Coordinator staged natively; isolated social contact capture completed. Not baked, registered, or accepted as a complete venue.** This is a joint furniture/posture target using native corrected social geometry. It preserves Café B2's visual family and all 17 immutable anchors. Workstation body/desk fit remains pending motion's typing solve; this candidate is not ready for registry promotion.

## Concrete shared chair

- Existing licensed `home_chair_1`, native imported yaw 180°.
- Center relative to each social anchor `(0, 0, -0.30)`; B2 was `(0, 0, -0.10)`.
- X/Z scale remains 1.08. Y scale is `1.08 × (.450 / .428800762)`, keeping chair feet at floor zero and placing actual upper support at 0.450 m.
- Native support polygon bounds relative to anchor: X ±0.209414 m; Z −0.491898 to −0.073070 m. The posterior witness region is X ±0.145 m and Z −0.42 to −0.15 m.
- Proposed underside contact target is 0.452 m, a 2 mm diagnostic separation. Motion must verify actual resulting surfaces, not treat pelvis shifts as linear exact displacements. The actor root/contract anchor remain fixed and corrected feet stay planted.

The native corrected social clips retain the old hips but change leg geometry. At original chair height, a −0.15 m rearward delta still crosses 2–4 Snow chair triangles at the eight exported tier-0 phases; −0.18 m and farther had zero tested triangle intersections for both looks. We selected −0.20 m for the shared target. A raised support plus corrected pelvis still requires a new native joint check; this is not a final clearance claim.

Actual imported upper support is 0.428800762 m before height adjustment. Against that support, corrected posterior underside minimum gaps across eight phases × three LODs were Snow 26.18–29.70 mm and Rain 57.34–61.08 mm. There was no sampled contact within 5 mm. At the proposed shared 0.450 m support, a first motion solve toward underside 0.452 m suggests roughly −3 to −7 mm Snow pelvis change and −34 to −38 mm Rain change. These are starting constraints, not accepted clip corrections. No look-dependent furniture is proposed.

## Candidate builder / collision / approach

`TerminalCafeCandidateB3Social.BuildCandidate` produces only `Assets/Art/Candidates/TerminalCafe20261006B3Social/TerminalCafeB3Social.prefab`; `BakeCandidate` bakes only that output. Source retains B2's narrow save/protection handling and original textures; no catalog/registry promotion.

All twelve claimed chairs now have actual-source XZ renderer-bounds navigation exclusions. Four social approaches are at anchor + `(.95,0,-.30)`; eight work approaches are at anchor + `(1.35,0,-.10)`. They are frontend-only `SpotAnchor.approach` transforms; authoritative roots/yaws/IDs remain unchanged. Native navigation checks target those approaches with a 0.35 m sampling radius and require complete paths. Nonclaimable anchors retain original targets. The candidate requires the separately reviewed runtime seat-approach patch so a confirmed actor can occupy an off-NavMesh seat and later restore its valid approach.

Work furniture is intentionally still the existing B2 geometry pending a coordinated hand/foot solve. It is **not** contact-accepted. The next hand target under discussion is desk front +0.20 m / top 0.74 m, but it is not implemented in this social-only helper.

## Evidence and limitations

- `social-support-v1.json`: actual native imported seat-triangle/skin vertical witnesses, eight body phases × three LODs × two looks, 8 mm ray spacing. `analyze_social_support.py` reproduces it. The selected posterior region is geometric, not a verified pressure/skin-weight map.
- `corrected-social-intersections.json`: tier-0 native body/chair triangle intersection counts at eight phases per look, six translations. `sweep_corrected_social.py` reproduces it. Zero surface intersections is not proof of continuous clearance or full-volume nonoverlap.
- `shared-social-target.json`: exact frozen physical/contact target sent to motion agent.
- `B2-to-B3Social.patch`: bounded builder differences. Isolated source compile against candidate World assembly passed; only the known Newtonsoft CS1701 warning. Native stage, 17 complete approach paths and isolated social contact capture have since completed; bake, connected seating and full-venue acceptance remain pending.

After motion exports the shared-seat pelvis candidate, use its real effective clips to render Snow and Rain front/side in the actual B3 chair and compare all body LOD surfaces across the loop. Check support overlap/contact, backside/backrest separation, thigh and calf front-edge clearance, planted shoes, and unchanged actor roots. Then validate actual runtime approach → authoritative seated mode → local/server stand using the new controller. Neither a NavMesh pass nor a foot-only pass accepts the chair pose.

## Joint body-fit evidence and native preview handoff

Motion's native `SocialBodyFitV1` receipt now passes both looks with protected drift empty. Snow's authored body correction is −2.307 mm and Rain's is −38.179 mm; the actor roots remain exact and corrected shoes remain planted. This is motion evidence, not furniture acceptance.

`review_joint_social.py` checks those actual exported meshes against `shared-social-chair-target.obj.gz`, the frozen transformation of the native B2 chair. Its 48 cases cover eight phases × three body LODs × two looks. The posterior surface-ray minimum gap spans Snow 3.20–6.44 mm and Rain 2.65–7.10 mm. No ray penetrates the seat beyond 2 mm. Some sampled poses have no ray within 5 mm, so apparent support still needs visual review. Full numerical results and input hashes are in `joint-social-body-fit-v1-surfaces.json`; its explicit limitations include sampled time, surface-only tests, and the separate native B3 placement gate.

The frozen native helper is `CafeSocialFitReview.cs`, SHA256 `4aed60d88f040910287823942e5525a15c94f94f85738b696f520cc7ce0c6323`. Root/coordinator procedure:

1. Stage the frozen builder and run `CTLife.EditorTools.TerminalCafeCandidateB3Social.BuildCandidate` in idle native Edit mode. Do not register this candidate. No bake is needed for the isolated pose preview.
2. Stage the review helper, then run `CTLife.EditorTools.CafeSocialFitReview.Capture` with graphics. `CaptureGeometry` explicitly skips images if desired; it is not a visual pass.
3. The helper uses real current Snow/Rain world prefabs, immutable `table_1` root, actual B3 chair/table and proven SocialBodyFitV1 clips. It warms two loops, asserts native time advancement across 121 samples, exports all body tiers at eight phases and renders front/side at three phases per look. It protects its asset dependencies and writes only outside Assets under `native-social-fit-review/`.
4. Inspect the 12 PNGs and actual geometry for seat, back, thigh, calf and shoes. The receipt deliberately says `captured_not_seating_accepted`.

The review controller is explicitly transient with **Foot IK enabled** because that is part of the proven candidate. Production `act_sit` currently has Foot IK off. No production controller is changed by the helper; actual transition integration, Timeline benches, Spaces lounge seats, connected multiplayer and device seating remain separate gates. Both new helper and builder compile in the isolated exact-reference harness; only the known CS1701 reference warning is present. Root owns all native execution.

## Provisional typing chair constraint

The proposed shared office-chair profile is the existing licensed mesh at yaw 180°, center relative to anchor `(0,0,-.31)`, scale `(1,1,.8)`, original height. It shortens the source depth without any look-dependent furniture. The broader sloped upper-surface region spans about 0.477 m in Z; the stricter near-horizontal bearing selection spans 0.404 m. The seat is curved, not a single 0.451 m plane.

`shared-office-target-v1-provisional.json` compares that exact target mesh with the older typing V1 exports, whose foot tolerance failed. Posterior ray minima are Snow −0.60..+3.40 mm and Rain +33.01..+37.19 mm. Early Snow surface crossings occur at the upper support; the tested back/calf triangles clear. A first native body-fit constraint is roughly +2.6 mm Snow and −31 mm Rain toward 2 mm minimum separation, followed by actual geometry validation. This is not a prescribed linear exact correction and not an accepted pose. Final typing loop/feet/body/hand geometry must be re-exported together.

The real centered keyboard target remains frozen: `keyboard/shared-typing-keyboard-target.obj.gz`, SHA256 `9b7c02535527e6e7e5e1e327715fea950d383c9954ba27797002f81857920eaf`. Neither this keyboard nor the proposed office-chair profile is implemented in the social-only builder. The original B2 work furniture remains explicitly unaccepted.

## Completed native B3 social placement review

The coordinator's stage exited 0 with 17 immutable anchors, 17 complete paths and 12 authored approaches. Its protected transaction covered 918 paths; an independent post-exit hash check found no drift, including package virtual paths resolved through the installed PackageCache. This is not a lighting bake or runtime phone-performance pass.

`native-social-fit-review/` captured both looks over 121 native samples each, with 12 images and zero protected drift. The helper intentionally reports `captured_not_seating_accepted`. Its actual native body/chair/table exports were audited in `native-social-furniture-surfaces.json` using `review_native_social.py`: 48 meshes (eight phases, three LODs, two looks), zero tested chair or current-table triangle crossings. These are discrete surface checks; enclosed volume, coplanar overlap, continuous-time clearance and production transitions are not established.

Front/side native images show plausible seat support and planted shoes. The table appears too far forward: conservative full-body front-to-table Z separation is 0.266–0.274 m for Snow and 0.281–0.292 m for Rain. A table-only rearward translation of 0.20 m, including its decorative tabletop objects, leaves 0.066–0.074 m / 0.081–0.092 m conservative Z separation and zero tested table crossings at these samples. This is a bounded composition recommendation, not an implemented change or a hand-reaching claim; social hands remain in the lap. It must be visually reviewed in the final candidate.

The source-derived collider issue is handled separately by `../terminal-cafe-b3-hits/`: tall bake obstacles are separated from physical furniture and real touched furniture resolves its existing SpotAnchor. Neither the original B3 stage nor its direct pose capture proves actual world-tap selection.

## Final venue promotion gates

1. Complete shared workstation contact: accepted persistent typing body/feet/hands replay against the frozen curved chair, real desk and keyboard, all body LODs and front/side native renders. The social-only builder still contains unaccepted B2 work furniture.
2. Stage the final combined furniture once; retain all 17 exact wire anchors and 12 navigable approaches. Validate the real gameplay-camera tap on chairs/tables, actual walk/arrival, authoritative seated pose, and local/server stand. The separate null-transport controller fixture passed 31 checks but cannot establish connected occupancy or backend animation mapping.
3. Integrate accepted clips through the actual controller/AvatarView transitions, with the required Foot IK configuration. Current social preview uses a transient state; production states are not silently changed. Timeline benches and Spaces lounge chairs require their own support checks before a global act_sit replacement.
4. Bake that final isolated prefab and verify lightmaps, all navigation approaches, room arrival/movement/return, HUD/phone and native error logs. Measure device startup/frame cost with actual static hit meshes and crowd; source triangle estimates are not performance acceptance.
5. Only the coordinator promotes the tested registry entry, preserving prior artifacts. Connected claims/rejections, remote observer animation and the terminal act_type server mapping remain explicit integration gates. No offline fixture resolves them.
