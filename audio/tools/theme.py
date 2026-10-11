"""CT Life main theme: the shared musical identity (own work, CC0).

Key G major, 112 BPM, light swing. The 2-bar HOOK is the leitmotif: the title loop states it, and the origin-reveal
stingers and the creator celebration jingle quote it, so every onboarding sound reads as one piece.
Notes are (beat, midi, length in beats); beat 0 = bar start.
"""
from __future__ import annotations

BPM = 112.0
SWING = 0.55
KEY_ROOT = 55  # G3

# MIDI helpers (G major)
D4, E4, Fs4, G4, A4, B4 = 62, 64, 66, 67, 69, 71
C5, D5, Ds5, E5, Fs5, G5, A5, B5 = 72, 74, 75, 76, 78, 79, 81, 83
C6, Cs6, D6, Ds6, E6, G6, B6 = 84, 85, 86, 87, 88, 91, 95

# The hook: "D-G-B, A-G-A, B..G-E-D" — a bright rising call that answers itself.
HOOK = [(0, D5, 0.5), (0.5, G5, 0.5), (1, B5, 1), (2, A5, 0.5), (2.5, G5, 0.5), (3, A5, 1),
        (4, B5, 1.5), (5.5, G5, 0.5), (6, E5, 1), (7, D5, 1)]

# 8-bar A melody (hook, then its answer), chords underneath.
A_CHORDS = [(55, "maj"), (52, "m7"), (48, "maj7"), (50, "7"), (55, "maj"), (59, "7"), (52, "m7", 57, "7"), (50, "7")]
A_MELODY = HOOK + [
    (8, E5, 0.5), (8.5, G5, 0.5), (9, B5, 1), (10, C6, 0.5), (10.5, B5, 0.5), (11, G5, 1),
    (12, A5, 1.5), (13.5, Fs5, 0.5), (14, D5, 0.5), (14.5, E5, 0.5), (15, Fs5, 1),
    (16, G5, 0.5), (16.5, B5, 0.5), (17, D6, 1), (18, B5, 0.5), (18.5, D6, 0.5), (19, E6, 1),
    (20, Ds6, 1), (21, B5, 0.5), (21.5, A5, 0.5), (22, Fs5, 1), (23, Ds5, 1),
    (24, E5, 0.5), (24.5, G5, 0.5), (25, B5, 1), (26, Cs6, 0.5), (26.5, A5, 0.5), (27, G5, 1),
    (28, Fs5, 1.5), (29.5, E5, 0.5), (30, D5, 2),
]

# 8-bar B section (lyrical bridge).
B_CHORDS = [(48, "maj7"), (50, "maj"), (59, "m7"), (52, "m7"), (57, "m7"), (50, "7"), (55, "maj7"), (50, "7sus", 50, "7")]
B_MELODY = [
    (0, E5, 1.5), (1.5, G5, 0.5), (2, B5, 2),
    (4, A5, 1.5), (5.5, Fs5, 0.5), (6, D5, 2),
    (8, Fs5, 1), (9, A5, 1), (10, D6, 1.5), (11.5, B5, 0.5),
    (12, G5, 3),
    (16, C6, 1), (17, B5, 0.5), (17.5, A5, 0.5), (18, G5, 1), (19, E5, 1),
    (20, Fs5, 1.5), (21.5, A5, 0.5), (22, C6, 2),
    (24, B5, 1), (25, A5, 0.5), (25.5, G5, 0.5), (26, Fs5, 1), (27, D5, 1),
    (28, G5, 1.5), (29.5, Fs5, 0.5), (30, E5, 1), (31, D5, 1),
]

INTRO_CHORDS = [(55, "maj7"), (48, "maj7"), (55, "maj7"), (50, "7")]
OUTRO_CHORDS = [(48, "maj7"), (50, "7"), (55, "maj"), (50, "7")]
