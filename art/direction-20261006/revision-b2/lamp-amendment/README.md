# B2 lamp amendment

Source-only amendment; native validation pending. Captured B2 and its builder/receipts remain intact.

`CityDirectionCandidateB2Lamp.cs` is B2 plus two lamp moves and measured-footprint navigation exclusions. It uses a separate class/output so staging cannot overwrite the captured B2 prefab:

- `CTLife.EditorTools.CityDirectionCandidateB2Lamp.BuildCandidate`
- `CTLife.EditorTools.CityDirectionCandidateB2Lamp.BakeCandidate`
- Output: `Assets/Art/Candidates/CityDirection20261006B2Lamp/TimelineStreetB2Lamp.prefab`

West lamp: (−3.7, −11.5) → (−12.2, −10.4). East lamp: (3.7, −11.5) → (9.8, −8.5). They mark the outer terrace edges, leaving the shop identity and central route clear in the arrival frame. All source scale/rotation remains 1/0. No camera, building, furniture, anchor, material or backend changes were made.

CPU-only Blender import measured the actual lamp at 0.531493 × 0.531493 × 4.400000 m; `source-bounds.json` records its extrema. `projection-audit.json` compares full lamp bounds with complete fascia frame masks, using the camera logged during the actual B2 native capture. The original two lamps intersect the Coffee/Terminal masks respectively, matching the screenshot. The amendment intersects none of the three shop frames. West/east lamps are at least 7.80/5.05 m from any fixed spot. Added 0.532 m square nav exclusions round up the real footprint; native path checks remain necessary.

Standalone source compilation exited 0, with the established CS1701 Newtonsoft/netstandard harness warning in `/tmp/ct-city-b2-lamp-isolated/compile.log`. Source SHA256: `8d6b5c88b9acf998c9c3cbf0265eccbc8f14b492af9d1e60e70b62b58694c118`.

Use the B2 protected stage/bake procedure, including external bytes before Editor startup and after exit. Native review must cover the same arrival camera, bench approaches, the kiosk/ATM and movement past the outer tables. The full bounds intentionally extend partly outside the arrival frame; their purpose is street lighting at the terrace edge, not a foreground frame across the lettering.
