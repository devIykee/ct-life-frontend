#!/bin/bash
# Rebuild after LOD-stage fixes (hair hull, legs trim, hip push-out, tone tweak). One Blender job at a time.
cd "$(dirname "$0")"
L=/Users/mac/Documents/codes/projects/ct-life-evidence/cast/logs
EV=/Users/mac/Documents/codes/projects/ct-life-evidence/cast/families
for f in m f; do
  ./run.sh cast_lods.py $f > $L/lods_$f.log; echo "lods_$f: $(tail -1 $L/lods_$f.log)"
  rm -f $EV/$f/manifest_$f.json $EV/$f/looks.blend
done
rm -rf ../export/looks ../export/parts
for f in m f; do ./run.sh cast_looks.py $f > $L/looks_$f.log; echo "looks_$f: $(tail -1 $L/looks_$f.log) $(grep -c Traceback $L/looks_$f.log) tracebacks"; done
for f in m f; do ./run.sh cast_parts_export.py $f > $L/parts_$f.log; echo "parts_$f: $(tail -1 $L/parts_$f.log)"; done
./run.sh cast_validate.py > $L/validate.log; echo "validate: $(tail -1 $L/validate.log | cut -c1-400)"
python3 cast_manifest.py > $L/manifest.log 2>&1; echo "manifest: $(cat $L/manifest.log | cut -c1-400)"
./run.sh cast_render.py family m > $L/render_m.log; echo "render_m: $(tail -1 $L/render_m.log)"
./run.sh cast_render.py family f > $L/render_f.log; echo "render_f: $(tail -1 $L/render_f.log)"
./run.sh cast_render.py tiers m:snow_everyday f:rain_breeze > $L/render_tiers.log; echo "render_tiers: $(tail -1 $L/render_tiers.log)"
echo FINISH_DONE
