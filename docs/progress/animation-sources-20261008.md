# Animation sources catalogue (2026-10-08)

Scope: source clips for sit / stand / stumble / fall / ragdoll get-up / landing / idle / turn / mic gestures. Nothing here was imported into Unity. Licences were read from live pages or the shipped licence files on 2026-10-08.

## Already in the project (do not refetch)

Full Quaternius Universal Animation Library 1 and 2 (Standard) are in `art/raw/quaternius/ual/` and `ual2/` (43 clips each, GLB and FBX, `_RM` = root motion baked, plain = in place). Licence row in `art/LICENSES.md` (characters block). Licence file text: "CC0 1.0 Universal ... Public Domain Dedication". 96 retargeted clips already exist in `art/processed/animations/clips.json` (incl. `act_sit_down`, `act_sit`, `act_sit_talk`, `act_stand_up`, `act_get_up`, `react_stagger`, `jump_*`, `act_phone_call`, `idle_fidget_*`, hand-keyed emotes, typing, phone scroll, host mic). Quaternius has no newer animation pack (checked quaternius.com packs list and itch profile today; UAL3 URL is 404).

## New downloads

| Folder | Source | Licence (quoted) | Size |
| --- | --- | --- | --- |
| `art/raw/animations/kaykit_character_animations/` | https://kaylousberg.itch.io/kaykit-character-animations (v1.1 Free) | License.txt: "License: (Creative Commons Zero, CC0) ... This content is free to use in personal, educational and commercial projects." Credit optional. | 56 MB (zip + extracted) |
| `art/raw/animations/cmu_mocap/` | https://mocap.cs.cmu.edu (ASF skeleton + AMC per clip) | FAQ "How can I use this data?": "The motion capture data may be copied, modified, or redistributed without permission." (saved in `LICENSE_CMU_faq_2026-10-08.txt`). Site asks for acknowledgement: "The database was created with funding from NSF EIA-0196217." | 27 MB |

Rejected: Bandai Namco Research Motiondataset (GitHub page today: "CC BY-NC 4.0", non-commercial, skipped). Mixamo, ActorCore, Synty, Asset Store banned. Kenney has no animation pack (kenney.nl/assets/animation-pack 404). OpenGameArt: could not surface a usable CC0 humanoid pack; not pursued.

## Needed action -> clip map

Rig key: UAL = Quaternius 65-bone rig (fingers, neck; already retargeted to CTL); KK = KayKit Rig_Medium, 22 bones (no fingers, no neck), FBX/GLB, Humanoid-mappable [Inference: not opened in Unity]; CMU = ASF/AMC, 120 fps, 31 bones, needs conversion (see notes). Lengths for UAL/KK are from GLB animation channels today; CMU frames are AMC frames at 120 fps.

| Need | Clip(s) | Source | Skeleton | Length | Loop? | Status |
| --- | --- | --- | --- | --- | --- | --- |
| Sit down (chair/bench/sofa) | Sitting_Enter (already `act_sit_down`) ; KK `Sit_Chair_Down` ; KK `Sit_Floor_Down` | UAL1 CC0 ; KK CC0 | UAL ; KK | 1.30 s ; 0.80 s ; 1.00 s | one-shot | covered |
| Seated idle variants | Sitting_Idle_Loop (`act_sit`) ; KK `Sit_Chair_Idle` ; KK `Sit_Floor_Idle` | UAL1 ; KK | UAL ; KK | 1.67 s ; 3.60 s ; 4.00 s | loop | covered |
| Stand up from seat | Sitting_Exit (`act_stand_up`) ; KK `Sit_Chair_StandUp`, `Sit_Floor_StandUp` | UAL1 ; KK | | 1.03 s ; 0.80 s ; 1.13 s | one-shot | covered |
| Seated talking | Sitting_Talking_Loop (`act_sit_talk`) | UAL1 | UAL | 2.93 s | loop | covered |
| Seated typing / phone | `act_type`, `act_frantic_type`, `act_sit_head_in_hands` (hand-keyed, in clips.json) | ours | CTL | see clips.json | loop | covered |
| Stumble / trip | CMU 104_13 "StumbleWalk" (550 f, 4.6 s) ; CMU 91_59 "360 smallStumble" (312 f, 2.6 s) ; UAL2 Hit_Knockback (`react_stagger`, 0.83 s) | CMU ; UAL2 | CMU | | one-shot | partial: no true trip. CMU clips are real stumbles but are within a longer walk; trim in Blender |
| Fall forward | CMU 90_16 "fall on face" (724 f, 6.0 s) ; KK `Death_B` (2.63 s) ; KK `Death_A` (0.80 s) | CMU ; KK | | | one-shot | covered (CMU = realistic; KK = stylized). For ragdoll, only first 0.3-0.5 s is needed as lead-in |
| Fall backward | CMU 90_18 "RugPullFall" (394 f, 3.3 s, feet slip from under: backward/up-ended fall) ; UAL1 Death01 (`react_liquidated`, 2.4 s) | CMU ; UAL1 | | | one-shot | covered [Unverified: direction of RugPullFall not viewed; the CMU .avi preview at mocap.cs.cmu.edu/subjects/90/90_18.avi can confirm] |
| Knocked-down lying idle | KK `Lie_Idle` (2.67 s) ; KK `Death_A_Pose`, `Death_B_Pose` (static poses) | KK | KK | | loop / pose | covered (face-up) ; no face-down lying idle: use last frame of CMU 140_01 start or ragdoll rest pose |
| Get up from face-down | CMU 140_01, 140_02 "Get Up Face Down" (830 f 6.9 s ; 937 f 7.8 s) | CMU | CMU | | one-shot | covered (long; speed up 1.5-2x) |
| Get up from face-up | CMU 140_08 "Get Up From Ground Laying on Back" (898 f, 7.5 s) ; 139_18 / 77_18 (854 f, same capture content) ; KK `Lie_StandUp` (2.33 s) ; UAL2 LayToIdle (`act_get_up`, 1.53 s) | CMU ; KK ; UAL2 | | | one-shot | covered |
| Get up from side | CMU 140_03 "Get Up Laying on Side" (1023 f, 8.5 s) | CMU | | | one-shot | covered |
| Jump / fall loop / landing | UAL1 Jump_Start/Loop/Land (already `jump_*`) ; UAL2 NinjaJump_* ; KK `Jump_Start` 0.60 s, `Jump_Idle` 1.07 s, `Jump_Land` 0.67 s, `Jump_Full_Short` 1.17 s | UAL ; KK | | | start/land one-shot, idle loop | covered (soft). Hard landing: only UAL2 `NinjaJump_Land` (1.27 s) and `Slide_*`; true hard/roll landing = UAL1 Roll (`react_roll`) |
| Idle: look around | CMU 139_01 "Looking Around" (1090 f, 9.1 s) ; ours `idle_fidget_lookaround` | CMU | CMU | | trim | covered |
| Idle: check phone / phone scroll | ours `act_phone_hold`, `act_phone_scroll` ; UAL2 Idle_TalkingPhone_Loop | ours ; UAL2 | | | loop | covered |
| Idle: shift weight | CMU 139_02 "Shifting Weight" (937 f, 7.8 s) | CMU | CMU | | trim to loop | covered |
| Idle: arms crossed | UAL2 Idle_FoldArms_Loop (`idle_fidget_foldarms`) | UAL2 | | 2.5 s | loop | covered |
| Talking gestures standing | UAL1 Idle_Talking_Loop (`idle_fidget_talk`) ; ours `talk_gesture_1/2` ; CMU 13_26/13_27 "direct traffic, wave, point" | | | | loop | covered |
| Cheering | KK `Cheering` (1.67 s) ; ours `emote_wagmi_fist_pump` | KK | KK | | one-shot/loop | covered |
| Facepalm / rekt reaction | ours `emote_facepalm`, `react_kneel_despair`, UAL2 Zombie_Idle (`idle_rekt`) | | | | | covered by existing hand-keyed; no mocap source found |
| Turn in place 90 / 180 | CMU 91_56 "90" (500 f, 4.2 s) ; CMU 91_54 "180" (678 f, 5.7 s) ; 91_58 "270" (493 f) | CMU | CMU | | one-shot | partial: these are walk-turns with a step, not pure idle pivots. No left/right pair labelled; mirror one for the other side |
| Walk start / stop | CMU 104_14 "HappyStartWalk" (709 f, 5.9 s) ; 104_06 "StartJog" (337 f) ; 104_09 "JogStop" (349 f) ; 104_17/18 "SpasticStop" not downloaded | CMU | CMU | | one-shot | partial: jog start/stop yes, a plain walk-stop is missing |
| Mic host: mic to mouth, point at audience | ours `act_host_mic`, `talk_host_with_mic`, `emote_point` ; CMU 13_27 (point) | ours | CTL | | loop | covered by hand-keyed only |
| Sit / stand reference (mocap) | CMU 143_18 "Sit Down And Get Up" (752 f), 143_19 "Sit On Stool And Get Up" (839 f), 13_01, 13_05 stool sit/stand | CMU | CMU | | one-shot | backup only; UAL/KK are the stylized match |

## Gaps (nothing CC0 found / not verified)

- True trip (toe catch) one-shot; walk-stop; pure in-place 90/180 pivots (L and R); face-down lying idle; seated laptop typing with real mocap; a hard-landing crouch absorb. Fillable by trimming CMU clips or hand-keying in `art/pipeline/anim_emotes.py`.
- I did not view any clip. CMU clip content is by index title only; the `.avi` previews on mocap.cs.cmu.edu show each one.

## Notes for the character-feel agent (retargeting to Unity Humanoid)

- **UAL1/UAL2**: 65 bones (hips -> spine_01/02/03 -> neck_01 -> Head, fingers, `*_leaf` end bones, IK/ctrl bones on the Unity export). Use `Unity/UAL*_Standard.fbx` (in place) for state-machine clips, `_RM` only if you want baked root motion. Existing pipeline already retargets these to the 55-bone CTL skeleton (`art/pipeline/anim_retarget.py`); keep using it.
- **KayKit**: Rig_Medium has 22 bones: `root, hips, spine, chest, head, upperarm/lowerarm/wrist/hand/handslot (.l/.r), upperleg/lowerleg/foot/toes (.l/.r)`. Dot-suffixed side naming; no neck, no fingers. [Inference] Unity Humanoid auto-map should accept it (neck and fingers are optional) but I did not open Unity. Animation files are split by category (General, Simulation, MovementBasic...), each FBX/GLB holds many clips on the one rig, and each contains a `T-Pose` clip. Easiest: import Rig_Medium FBX as Humanoid, then retarget through Blender with the existing `anim_retarget.py` flow. Root: a `root` bone exists above `hips`; clips appear in place, but the `Jump_Full_*` and `Dodge_*` clips may translate [Unverified]; check before use. FPS not inspected.
- **CMU**: ASF/AMC is not importable by Unity. Convert AMC -> BVH (e.g. Blender add-on or a script) -> Blender -> retarget to CTL, then export FBX. Units: ASF scale 0.45, inches (x 2.54/100 for metres, per CMU FAQ), so scale on import. 120 fps; resample to 30. Z-up / Y-up conventions differ per tool, check facing. CMU data has root translation in the clip (it is real displacement); strip to in-place for fall/get-up if the ragdoll handler places the body, and keep the hips-height curve (important for get-up). Get-ups run 7-8 s: speed 1.5-2x or cut the dead tail. Mocap proportions are a real adult; the Snow/Rain characters are stylized, so expect foot sliding and arm-to-body penetration on sit/lie clips; fix with IK or retarget scale.
- For ragdoll hand-off: use the first 0.3-0.5 s of a fall clip as the animated lead-in, switch to physics, then on rest detect face-up/face-down by hips/chest up-vector and play the matching get-up (face-down = CMU 140_01/02, face-up = KK Lie_StandUp or UAL2 LayToIdle or CMU 140_08), aligning the clip's start pose to the ragdoll's rest pose by root yaw and position.
- Attribution: all sources are CC0 or the CMU free-use statement; no attribution is required, but add the CMU funding acknowledgement and credit "Kay Lousberg" if you ever ship credits (optional).
