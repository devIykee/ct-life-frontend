#!/bin/bash
# Full cast rebuild, one Blender job at a time. Logs in ct-life-evidence/cast/logs.
cd "$(dirname "$0")"
LOG=/Users/mac/Documents/codes/projects/ct-life-evidence/cast/logs
EV=/Users/mac/Documents/codes/projects/ct-life-evidence/cast/families
set -e
for f in ${FAMS:-m f}; do
  if [ -z "$SKIP_EXTRACT" ]; then ./run.sh cast_extract.py $f > $LOG/extract_$f.log; fi
  if [ -z "$SKIP_WARDROBE" ]; then rm -f $EV/$f/wardrobe.blend $EV/$f/wardrobe.json; ./run.sh cast_wardrobe.py $f > $LOG/wardrobe_$f.log; fi
  ./run.sh cast_lods.py $f > $LOG/lods_$f.log
  rm -f $EV/$f/manifest_$f.json $EV/$f/looks.blend
  ./run.sh cast_looks.py $f > $LOG/looks_$f.log
  tail -1 $LOG/looks_$f.log
done
