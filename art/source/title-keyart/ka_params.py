"""Art-direction numbers for key art B ("Close duo"). Positions are metres; angles degrees.

Character-frame vectors (poses): rig at the origin, facing -Y, character's left = +X, Z up.
World placements are given in Unity coordinates (the plate's) and converted with u2b().
"""

# ---------------------------------------------------------------- camera (Unity coords, matches the clean plate)
CAMERA = dict(pos=(0, 0.55, -11.4), target=(0, 1.05, -6.2), mm=35)
PLATE = 'bg-dof.png'          # Unity clean backdrop rendered by client/Tools~/keyart/title-keyart-blender-bg.json

# ---------------------------------------------------------------- placement (Unity coords x, z; yaw about up, + turns toward frame right)
PLACE_F = dict(pos=(-1.30, 0.0, -6.80), yaw=24)
PLACE_M = dict(pos=(1.42, 0.0, -6.35), yaw=-18)

# ---------------------------------------------------------------- left: head_player_f_kol (skin_tan), phone hold
F = dict(
    hip_shift=(0.055, 0.0, -0.02), hip_tilt=-9, hip_turn=-6, spine_tilt=10, chest_tilt=3, spine_turn=4, chest_fwd=3,
    ankle_L=(0.10, 0.0, 0.085), knee_L=(0.1, -1, 0), foot_dir_L=(0.25, -1, 0),
    ankle_R=(-0.13, -0.06, 0.149), knee_R=(0.3, -1, 0.1), foot_dir_R=None, toe_R=6,
    clav_L=0, clav_R=0,
    hand_R=(-0.09, -0.36, 1.23), elbow_R=(-0.4, 0.3, -1),
    handR_y=(0.95, -0.2, 0.1), handR_z=(-0.22, -0.56, -0.80),
    hand_L=(0.20, 0.02, 1.00), elbow_L=(1, 0.6, 0), handL_y=(-0.25, -0.6, -0.75), handL_z=(1, 0, 0.2),
    neck_x=8, head_x=12, head_y=-10, head_z=0,
    grip_R={'Index': (8, 80, 45), 'Middle': (10, 85, 45), 'Ring': (14, 85, 45), 'Pinky': (20, 80, 40)},
    grip_L={'Index': (15, 25, 15), 'Middle': (18, 28, 15), 'Ring': (20, 30, 15), 'Pinky': (24, 32, 18)},
    thumb_R=(15, 15, 20), thumb_L=(5, 10, 10),
)
PHONE = dict(along=0.058, below=0.020, scale=1.2)
# smirk: (character-frame x, z of the mouth corner, lift metres, back metres, radius)
SMILE_F = [(0.023, 1.488, 0.0045, 0.0015, 0.016), (-0.023, 1.488, 0.0015, 0.0005, 0.012)]

# ---------------------------------------------------------------- right: head_player_m_kol (skin_dark), arms-up celebrate
M = dict(
    hip_shift=(0.03, 0, -0.055), hip_turn=4, hip_tilt=4, hip_pitch=0,
    ankle_L=(0.24, 0.07, 0.092), knee_L=(0.25, -1, 0), foot_dir_L=(0.3, -1, 0),
    ankle_R=(-0.22, -0.13, 0.092), knee_R=(-0.25, -1, 0), foot_dir_R=(-0.3, -1, 0),
    spine_x=-3, chest_x=-5, spine_z=-4, chest_y=3,
    clav_L=-15, clav_R=15,
    hand_L=(0.40, -0.06, 1.90), elbow_L=(1, 0.4, -0.3), handL_y=(0.4, -0.1, 0.9), handL_z=(0.3, 0.9, -0.1),
    hand_R=(-0.45, -0.09, 1.83), elbow_R=(-1, 0.3, -0.6), handR_y=(-0.4, -0.1, 0.9), handR_z=(-0.3, 0.9, -0.1),
    thumb_L=(45, 35, 35), thumb_R=(45, 35, 35),
    neck_x=2, head_x=-2, head_z=6,
)
SMILE_M = [(0.026, 1.602, 0.0090, 0.0030, 0.022), (-0.026, 1.602, 0.0090, 0.0030, 0.022),
           (0.044, 1.628, 0.0030, 0.0, 0.020), (-0.044, 1.628, 0.0030, 0.0, 0.020)]

# ---------------------------------------------------------------- light (Blender units; tuned in AgX)
SUN = dict(unity_euler=(30, 40, 0), color=(1.0, 0.72, 0.46), strength=3.7, angle=3.0)
WORLD_STRENGTH = 0.40
SKY = (0.70, 0.70, 0.82)
BOOT_FILL = dict(z_top=0.30, color=(0.018, 0.018, 0.022))
# rim lights (light-linked to the characters only): name, who, offset from feet, aim height, colour, watts, size
RIMS = [
    ('rim_f_inner', 'F', (1.0, 2.1, 1.9), 1.25, (0.55, 0.72, 1.0), 2600, 0.7),
    ('rim_f_outer', 'F', (-1.2, 1.8, 1.7), 1.10, (0.60, 0.78, 1.0), 1800, 0.6),
    ('rim_m_inner', 'M', (-1.1, 2.1, 2.1), 1.40, (0.55, 0.72, 1.0), 2600, 0.7),
    ('rim_m_outer', 'M', (1.3, 1.9, 1.9), 1.40, (0.60, 0.78, 1.0), 1800, 0.6),
]
FILL = dict(loc=(2.8, -9.8, 1.7), target=(0, -6.4, 1.1), color=(1.0, 0.88, 0.78), power=160, size=3.0)
PHONE_GLOW = 6.0
# soft beauty key from camera-left-high (characters only): faces read warm under the sun's hard key
BEAUTY = dict(loc=(-3.2, -10.2, 3.2), target=(0, -6.6, 1.45), color=(1.0, 0.84, 0.68), power=320, size=2.6)
CONTACT = dict(height=2.0, size=1.6, power=45)
