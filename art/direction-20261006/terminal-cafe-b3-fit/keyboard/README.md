# Sourced typing contact target

Existing Kenney Furniture Kit 2.0 `computerKeyboard` from the already downloaded CC0 pack. Original OBJ, MTL, License.txt and hashes are included. Source: https://kenney.nl/assets/furniture-kit . No new dependency or canonical art change.

`keyboard-geometry.json` normalizes the source's 18 vertices / 64 triangles to adult width 0.46 m × depth 0.15 m × height 0.023 m, retaining topology/material groups. Coordinates are explicit Unity local coordinates (OBJ X reflection plus reversed winding); `placement_relative_anchor_m` is `(0,.74,.205)`. Candidate native facing/normals must still be inspected. The OBJ gzip is an analysis target in the same root-local frame as motion exports; it is not a claim of a native imported/baked game object.

Actual keybed surfaces run from Y .748857779 / Z .226028073 at the near row edge to Y .760091138 / Z .307233759 at the far edge. `keybed-triangles.json` contains the actual triangles. The source has broad main/numpad plates, not individually modeled keycap rows. Motion receives these surfaces to compare actual palms/fingertips; no empty-desktop or fake-keyboard acceptance.

A future coordinated typing builder can create a candidate-only Mesh from this geometry with its two original material groups, or use a verified native model import. Keep this centered surface distinct from B2's offset laptop. The work desk's proposed front +.20 m / top .74 m and hand wrist targets remain provisional until the seated body/foot baseline and actual hand reach are jointly accepted. Café B3Social intentionally does not alter workstation furniture yet.
