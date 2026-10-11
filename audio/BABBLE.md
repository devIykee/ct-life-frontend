# NPC babble: how to play a line

NPC voices are babble, not text-to-speech (spec, "NPCs and AI"). Every archetype has a bank of 26
syllables, one per letter: `processed/babble/babble_<archetype>_<a..z>.wav` (48 kHz, 16-bit mono,
all syllables at the same RMS, true peak <= -1.5 dBTP). Per-bank numbers are in
`processed/babble/babble-banks.json` and in `sfx-map.json` under `babble_<archetype>`.

Reference code: `unity/BabblePlayer.cs` (compiles against Unity 6000.3.25f1's engine assemblies; not yet
run inside the game). Python twin for auditioning: `tools/babble_preview.py <archetype> "<line>" [npcId]`.

## Rules

1. **Walk the line one character at a time.** Step = `1 / chars_per_second` (11-22 cps per archetype).
2. **Letters a-z** play their own syllable. Case-insensitive.
   - Digits map to letters: 0→o 1→w 2→t 3→r 4→f 5→f 6→s 7→s 8→e 9→n.
   - Symbols: `$`→s `@`→a `#`→h `%`→p `&`→n `+`→p.
3. **Everything else is a pause** of `step × k`: space 1, `,;:` 2, `.!?` and newline 3, `-` and `/` 1, any other
   character 0.5.
4. **Pitch** (in semitones, applied as `AudioSource.pitch = 2^(semitones/12)`, which also changes speed; that is
   the intended Animal-Crossing feel):
   - NPC offset: `lerp(npc_pitch_range[0], npc_pitch_range[1], (crc32(npcId) % 1000) / 999)`. Default range is
     ±2 st, so two NPCs of the same archetype sound different but stable across sessions and clients.
   - Plus a random jitter in `±pitch_jitter_semitones` per syllable (seeded by `crc32(npcId + line)`).
   - UPPERCASE letter: +1.5 st and ×1.12 gain (the Town Crier's "JUST IN" shouts).
   - Line ends with `?`: the last 4 syllables rise +4, +3, +2, +1 st.
   - Line ends with `!`: the last 3 syllables +1 st and ×1.12 gain.
5. **Cap** a line at 3 s (the TALK state's length) or end of text, whichever comes first.
6. **Voices:** 4 round-robin 3D `AudioSource`s on the NPC, started with `PlayScheduled` about 0.1 s ahead on the
   DSP clock. Syllables may overlap slightly; do not cut the previous one (cutting clicks).
7. **Volume:** `volume_db` from the bank (−4 to −12 dB). Babble goes on the SFX bus, not the music bus.
8. **Fallbacks:** unknown archetype → `babble_generic`. Missing clip → skip that syllable, never crash.

## Banks

Phase 1 archetypes (IDs from `contract/ids/archetypes.json` as listed in contract.md): `town_crier`
(megaphone), `giveaway_guy`, `reply_guy` (nasal), `perp_degen_npc` (fast, driven), `rekt_guy` (slow, shaky,
falling), `wallet_tracker`, `doomposter` (low, dark), `spaces_host` (radio voice), `ore_miner` (gravel).

Later phases, **proposed IDs** (BE owns archetype IDs; rename the files if BE picks other names):
`alpha_caller`, `cabal_insider` (whisper), `rug_dev`, `sniper_bot` (beeps, not speech), `laser_eyes_maxi`,
`chain_tribalist` (use two NPC ids so the pair differs in pitch), `sleuth`, `auditor` (monotone), `vc`,
`farmer` (twang), `sybil_farmer` (tiny, chipmunk; clones get random npcIds), `fake_rich_larp`, `dubai_relocator`,
`ai_agent` (ring-mod robot), `drainer` (breathy), `community_mod` (headset band-pass), `thread_boy`,
`engagement_farmer`, `normie`, `doxxed_founder`, `prediction_shark_npc`. Plus `generic`.
