# NPC babble: how to play a line

NPC voices are babble, not text-to-speech (spec, "NPCs and AI"). Every archetype has a bank of 26
syllables, one per letter: `processed/babble/babble_<archetype>_<a..z>.wav` (48 kHz, 16-bit mono,
all syllables at the same RMS, true peak <= -1.5 dBTP). Per-bank numbers are in
`processed/babble/babble-banks.json` and in `sfx-map.json` under `babble.<archetype>` (`files` ordered a..z, plus a `letters` map).

Shipping code: `client/Assets/Scripts/Audio/Logic/BabbleScheduler.cs` (these rules as a pure, unit-tested function)
and `client/Assets/Scripts/Audio/BabblePlayer.cs` (playback). `unity/BabblePlayer.cs` is the original reference.
Python twin for auditioning: `tools/babble_preview.py <archetype> "<line>" [npcId]`.

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
8. **Fallbacks:** unknown archetype → the `default` bank (files `babble_generic_*`). Missing clip → skip that syllable, never crash.
9. **Rate limits** (`BabbleBudget`): at most 3 NPCs babbling near the listener (nearest win; a nearer new line cuts the
   farthest talker), one line per NPC per 1.5 s, silent beyond 15 m (linear roll-off from 2 m). The speech bubble and
   the returned line length are unaffected; only the sound is skipped.
10. **Endings with the cap:** the `?`/`!` contour applies to the last syllables actually played (after the 3 s cap).

## Banks

All 28 archetype IDs in `contract/ids/archetypes.json` have a bank (`babble.<id>` in `sfx-map.json`), plus
`default` (files `babble_generic_*`) and two not yet in the registry: `laser_eyes_maxi` and `chain_tribalist`
(spec NPC table; use two npcIds for the pair so they differ in pitch).

Voices: `town_crier` megaphone; `reply_guy` nasal; `perp_degen_npc` fast, driven; `rekt_guy` slow, shaky,
falling; `doomposter` low, dark; `spaces_host` and `vc` radio voice; `ore_miner` gravel; `cabal_insider` whisper;
`sniper_bot` beeps, not speech; `auditor` monotone; `farmer` twang; `sybil_farmer` tiny chipmunk (give clones random
npcIds); `ai_agent` ring-mod robot; `drainer` breathy; `community_mod` headset band-pass; `normie` rising, unsure.
