"""CT Island v2: the authored plan (single source of truth for terrain, roads, districts, venues and landmarks).

Coordinates: island metres, +x east, +z north, +y up. Origin = Timeline Square plaza centre, at y = 0.
Sea level is y = SEA (-6 m), so the plaza stands 6 m above the sea; the flight ceiling is 220 m above sea level.

The island faces south. One connected mountain range runs along the north coast, crowned by the MtnDAO summit, with
foothills and the rolling Builder Hills below it. Downtown (an irregular grid turned off the coast, cut by one diagonal
boulevard) sits at the head of a crescent bay with the beach boardwalk along it. A
river comes from a foothill spring, past Breakpoint Park and down the west side of downtown into the Trenches
harbour. The Dubai Marina basin is cut into the south-east coast behind Marina Point; Airdrop Farm rolls over the
east; the Residences sit on the western plateau above the cliffs and the fjord inlet. Everything here is placed by
hand; generator code only smooths, samples and carves.
"""
import math

SEA = -6.0
FLIGHT_CEILING = SEA + 220.0
DOMAIN = 1024.0            # heightfield covers [-DOMAIN, DOMAIN] on x and z
CELL = 2.0                 # heightfield spacing (m)
LANE = 3.3                 # lane width (contract)

# ------------------------------------------------------------------ coastline
# Counter-clockwise outline (east along the south coast first). Each point opens a stretch of coast of its type:
# beach (gentle sand), rock (rocky shore), cliff (steep drop; h = cliff top above the sea), quay (vertical wall).
COAST = [
    (-720, -300, 'cliff', 16), (-668, -372, 'cliff', 12), (-600, -414, 'rock', 0), (-566, -430, 'rock', 0),
    (-452, -414, 'quay', 0), (-392, -378, 'quay', 0), (-346, -342, 'rock', 0),                      # Trenches, Rug Point
    (-298, -330, 'beach', 0), (-236, -298, 'beach', 0), (-160, -266, 'beach', 0), (-80, -246, 'beach', 0),
    (0, -240, 'beach', 0), (84, -246, 'beach', 0), (164, -268, 'beach', 0), (232, -302, 'beach', 0),   # crescent bay
    (276, -342, 'rock', 0), (298, -392, 'rock', 0), (322, -444, 'cliff', 8), (372, -470, 'rock', 0),   # Marina Point
    (404, -452, 'quay', 0), (414, -396, 'quay', 0), (430, -352, 'quay', 0), (480, -334, 'quay', 0),    # marina basin
    (548, -330, 'quay', 0), (600, -346, 'quay', 0), (614, -392, 'quay', 0), (608, -440, 'rock', 0),
    (628, -488, 'rock', 0), (676, -540, 'cliff', 10), (738, -534, 'rock', 0), (782, -488, 'beach', 0),  # SE peninsula
    (806, -402, 'beach', 0), (822, -300, 'beach', 0), (834, -180, 'beach', 0), (828, -60, 'beach', 0),  # east beach
    (806, 30, 'rock', 0), (762, 74, 'beach', 0), (770, 118, 'beach', 0), (806, 150, 'rock', 0),       # east cove
    (826, 224, 'cliff', 18), (862, 300, 'cliff', 26), (884, 382, 'cliff', 32), (856, 456, 'cliff', 28),  # Observatory
    (790, 480, 'cliff', 22), (742, 530, 'cliff', 22), (652, 584, 'cliff', 22), (548, 612, 'cliff', 22),
    (452, 604, 'rock', 0), (402, 624, 'beach', 0), (350, 618, 'rock', 0),                            # Gm Cove
    (246, 652, 'cliff', 24), (120, 674, 'cliff', 26), (0, 670, 'cliff', 26), (-120, 682, 'cliff', 28),
    (-240, 678, 'cliff', 26), (-360, 664, 'cliff', 24), (-462, 642, 'cliff', 22), (-562, 612, 'cliff', 22),
    (-642, 562, 'cliff', 24), (-722, 492, 'cliff', 28), (-782, 402, 'cliff', 30), (-812, 302, 'cliff', 30),
    (-816, 214, 'cliff', 28), (-764, 194, 'cliff', 20), (-716, 170, 'beach', 0), (-752, 144, 'cliff', 20),  # fjord inlet
    (-818, 122, 'cliff', 26), (-834, 20, 'cliff', 26), (-818, -82, 'cliff', 24), (-792, -180, 'cliff', 22),
    (-756, -252, 'cliff', 18),
]

# IslandDAO islet off the east beach, reached by a causeway (a low land strip carrying a service road).
ISLET = dict(center=(800, -646), radius=46, top=SEA + 7.5)
CAUSEWAY = dict(path=[(800, -470), (802, -532), (802, -598)], half_width=7.0, top=SEA + 1.8)

# ------------------------------------------------------------------ relief
# Mountain massifs (x, z, summit y, half-width x, south reach, north reach). Their soft union makes the range:
# saddles fall where massifs overlap; angular noise grows spurs and gullies; rock and snow come from slope/height.
MASSIFS = [
    (-660, 470, 58, 140, 120, 90),
    (-520, 522, 96, 120, 110, 85),
    (-410, 482, 112, 110, 95, 90),      # west peak
    (-300, 532, 88, 120, 100, 90),
    (-180, 495, 176, 175, 165, 105),    # the MtnDAO summit
    (-90, 537, 118, 110, 95, 80),
    (40, 502, 104, 100, 95, 90),
    (150, 542, 138, 140, 120, 95),      # east peak
    (270, 517, 92, 110, 95, 85),
    (380, 550, 104, 110, 95, 75),
    (500, 517, 70, 120, 90, 80),
    (610, 482, 44, 110, 80, 70),
]
SPURS = [((120, 500, 120), (240, 432, 52), 70)]   # east peak spur into the top of Builder Hills
# The range (v2, 2026-10-17 art direction): ONE connected crest line, west to east (x, z, crest y). Saddles fall
# between the high points; relief.mountain_range adds spurs, the summit pyramid and the lodge bench, then erosion.
RANGE = [
    (-770, 352, 22), (-700, 414, 46), (-612, 452, 70), (-520, 478, 86), (-432, 496, 102), (-350, 508, 78),
    (-268, 516, 104), (-180, 515, 140), (-96, 530, 108), (-18, 538, 80), (64, 538, 96), (150, 532, 116),
    (238, 516, 88), (326, 502, 74), (414, 490, 66), (500, 480, 52), (580, 466, 36), (650, 440, 22),
]
SUMMIT = (-180.0, 515.0, 176.0, 235.0)          # MtnDAO summit (x, z, y, pyramid radius)
SHOULDER = (-254.0, 404.0, 70.0, 34.0, 26.0, 0.0)   # lodge bench at the summit road's end (x, z, y fallback, rx, rz, yaw)
# Rounded hills (x, z, height y, radius x, radius z); never perfect circles (angular wobble in terrain.py).
HILLS = [
    (842, 380, 70, 80, 110),      # Observatory peninsula (the coast highway tunnels through its west flank)
    (360, 330, 38, 210, 150),     # Builder Hills (rolling; lots get their own pads)
    (620, 160, 14, 170, 120), (690, -80, 10, 140, 120), (560, 240, 20, 120, 90),   # farm hills
    (-600, 60, 18, 240, 280),     # west plateau under the Residences
    (-60, 372, 32, 170, 75),      # foothills under the summit switchbacks
    (-430, 330, 26, 200, 90),
    (330, -160, 14, 110, 80),     # Hodl Heights between downtown and the marina
]
TERRACES = None   # v2 art direction: no stepped shelves; building lots get their own pads

# Flat districts: (name, polygon, target, edge blend m). target: number (y), 'downtown', 'farm' (smoothed), 'soft'.
DOWNTOWN_POLY = [(-232, -250), (-160, -232), (-80, -214), (0, -208), (84, -214), (164, -232), (262, -262),
                 (236, 198), (-250, 148)]
FLATS = [
    ('downtown', DOWNTOWN_POLY, 'downtown', 24),
    ('trenches', [(-440, -414), (-300, -330), (-262, -230), (-300, -176), (-430, -230)], SEA + 1.9, 18),
    ('marina', [(380, -470), (420, -470), (630, -440), (680, -300), (640, -250), (360, -270)], SEA + 2.3, 20),
    ('boardwalk', [(-300, -332), (-160, -268), (0, -242), (164, -270), (240, -305), (250, -255), (0, -205), (-300, -280)], SEA + 2.6, 10),
    ('breakpoint', [(-420, 205), (-150, 205), (-130, 330), (-400, 350)], 'soft', 40),
    ('farmyard', [(470, 20), (740, 40), (745, 190), (480, 170)], 'farm', 40),
]


def downtown_height(x, z):
    """Downtown leans gently towards the bay: about -1.4 m on Ocean Drive, +1.1 m on Summit Street."""
    return 0.0065 * z


# ------------------------------------------------------------------ river
# (x, z, water level y, half-width, depth): from a foothill spring to the Trenches harbour mouth.
RIVER = [
    (96, 392, 33.0, 5, 1.2), (78, 352, 26.0, 6, 1.4), (46, 318, 18.0, 7, 1.6), (0, 296, 11.0, 8, 1.7),
    (-60, 276, 6.0, 9, 1.8), (-112, 256, 3.4, 10, 2.0), (-162, 234, 1.4, 11, 2.1), (-208, 206, -0.4, 11, 2.2),
    (-246, 160, -2.2, 12, 2.4), (-270, 92, -3.2, 13, 2.6), (-284, 22, -4.0, 14, 2.8), (-300, -60, -4.6, 15, 3.0),
    (-345, -140, -5.2, 17, 3.4), (-410, -205, -5.7, 22, 4.5), (-460, -270, -6.0, 32, 6.5), (-490, -345, -6.0, 40, 7.5),
    (-505, -425, -6.0, 50, 8.0), (-515, -520, -6.0, 60, 9.0),
]
LAKES = [dict(center=(100, 402), radius=22, level=33.2, depth=2.5), dict(center=(-300, 238), radius=18, level=4.6, depth=1.5)]

# ------------------------------------------------------------------ districts (labels, masks, building rules)
DISTRICTS = [
    dict(id='timeline', name='Timeline Downtown', label=(-20, 140), poly=DOWNTOWN_POLY),
    dict(id='trenches', name='The Trenches', label=(-330, -390), poly=[(-450, -420), (-300, -334), (-250, -226), (-300, -170), (-440, -226)]),
    dict(id='dubai_marina', name='Dubai Marina', label=(520, -400), poly=[(370, -476), (640, -440), (690, -300), (640, -240), (350, -262)]),
    dict(id='boardwalk', name='Beachfront Boardwalk', label=(-20, -300), poly=[(-300, -334), (0, -246), (244, -308), (250, -250), (0, -200), (-300, -280)]),
    dict(id='airdrop_farm', name='Airdrop Farm', label=(640, 30), poly=[(450, -200), (800, -200), (800, 250), (460, 270)]),
    dict(id='builder_hills', name='Builder Hills', label=(420, 214), poly=[(190, 210), (540, 200), (560, 420), (220, 430)]),
    dict(id='breakpoint', name='Breakpoint Park', label=(-290, 330), poly=[(-430, 200), (-150, 196), (-130, 340), (-420, 360)]),
    dict(id='residences', name='Residences', label=(-560, 100), poly=[(-790, -240), (-330, -190), (-320, 200), (-780, 300)]),
    dict(id='mountain', name='MtnDAO Summit', label=(-180, 560), poly=[(-720, 380), (640, 380), (640, 620), (-720, 620)]),
    dict(id='islet', name='Palm Islet', label=(760, -700), poly=[(750, -700), (850, -700), (850, -600), (750, -600)]),
]

# ------------------------------------------------------------------ roads
# Node ids are shared between routes; a route is split into segments at every node it passes. Route ends that are
# plain points become dead-end nodes.
NODES = {
    # coast highway
    'HM1': (352, -268), 'HM2': (604, -270), 'HE1': (752, -352), 'HE3': (740, 126), 'TUNS': (782, 262),
    'TUNN': (778, 470), 'HN1': (600, 582), 'HN4': (-470, 620), 'BRF1': (-752, 204), 'BRF2': (-742, 132),
    'HW2': (-748, 44), 'BRHW': (-566, -404), 'BRHE': (-452, -396), 'T3': (-404, -350), 'T4': (-350, -318),
    # Genesis Avenue west + river bridge + Residences
    'BR1A': (-252, 38), 'BR1B': (-306, 35), 'R1': (-392, 40), 'R2': (-520, 52), 'R3': (-640, 48), 'R0': (-420, 128), 'RS1': (-560, 162), 'RC1': (-462, -40),
    # Farm Road
    'F1': (500, 72), 'F2': (652, 112),
    # Hills Drive
    'B1': (182, 238), 'B2': (420, 252), 'B3': (272, 306), 'B4': (452, 366), 'B5': (560, 424),
    # Marina
    'MQ': (404, -318),
    # Harbour Road / Trenches
    'T1': (-330, -234), 'T2': (-402, -276),
    # Breakpoint Way + Foothill Road
    'BR3A': (-160, 206), 'BR3B': (-194, 238), 'P1': (-232, 268), 'P2': (-336, 302),
    'FH1': (-120, 318), 'FH2': (-20, 340), 'MR0': (-112, 388), 'BR4A': (30, 332), 'BR4B': (66, 322), 'FH3': (96, 306),
}

# Downtown (v2 art direction): an irregular grid turned DT_ROT degrees off the coast, ONE diagonal boulevard
# (Consensus Boulevard) whose axis runs through the Timeline Square clock tower (the plaza block interrupts it), roundabouts
# where it crosses grid nodes, a central park north of the plaza, and Ocean Drive with its promenade along the bay.
# Local grid coordinates (u east, v north) turn by DT_ROT counter-clockwise about the plaza (0, 0) into island metres.
DT_ROT = 6.0
DT_U = [-206.0, -137.0, -54.0, 54.0, 126.0, 204.0]           # column lines (block widths 69, 83, 108, 72, 78)
DT_V = [None, -148.0, -98.0, -38.0, 40.0, 92.0, 148.0]       # row lines (row 0 = Ocean Drive along the bay)
DT_OCEAN_PATH = [(-262, -262), (-219, -238), (-141, -216), (-58, -202), (35, -198), (122, -206), (207, -224), (262, -252)]
DT_REMOVED = {('h', 2, 5), ('h', 4, 1), ('v', 1, 5)}         # ('h', col, row) / ('v', col, row) street pieces left out
DT_AVENUE_ROW, DT_AVENUE_COL = 4, 3                          # Genesis Avenue and Timeline Avenue
DT_ONE_WAY = {('h', 2): +1, ('h', 5): -1}                    # one-way rows (+1 eastbound, -1 westbound)
DT_ROW_NAMES = [None, 'Wen Street', 'Liquidity Street', 'Gm Street', 'Genesis Avenue', 'Hodl Street', 'Summit Street']
DT_COL_NAMES = ['River Road', 'Mint Street', 'Ledger Street', 'Timeline Avenue', 'Validator Street', 'Epoch Street']
DT_DIAGONAL = [('consensus_sw', ['D01', 'D12', 'D23']), ('consensus_ne', ['D34', 'D45', 'D56'])]
DT_PARK = ('genesis_park', [(2, 4), (3, 6)])                 # columns 2..3, rows 4..6: the central park block
DT_PLAZA = ('timeline_plaza', [(2, 3), (3, 4)])              # the plaza block round the Timeline Square venue


def dt_node(c, r):
    return f'D{c}{r}'


def dt_local_to_world(u, v):
    a = math.radians(DT_ROT)
    return (u * math.cos(a) - v * math.sin(a), u * math.sin(a) + v * math.cos(a))


def dt_world_to_local(x, z):
    a = math.radians(DT_ROT)
    return (x * math.cos(a) + z * math.sin(a), -x * math.sin(a) + z * math.cos(a))


def dt_pos(c, r):
    """Grid node position (island metres). Row 0 nodes sit where each column meets Ocean Drive's bay curve."""
    if r == 0:
        u = DT_U[c]
        best = None
        for (x0, z0), (x1, z1) in zip(DT_OCEAN_PATH[:-1], DT_OCEAN_PATH[1:]):
            # column line: points (u, v) for all v; in local coords the bay segment is a line too
            a0, b0 = dt_world_to_local(x0, z0)
            a1, b1 = dt_world_to_local(x1, z1)
            if (a0 - u) * (a1 - u) <= 0 and a1 != a0:
                t = (u - a0) / (a1 - a0)
                best = (x0 + (x1 - x0) * t, z0 + (z1 - z0) * t)
        return (round(best[0], 2), round(best[1], 2))
    x, z = dt_local_to_world(DT_U[c], DT_V[r])
    return (round(x, 2), round(z, 2))


def dt_block_poly(c0r0, c1r1):
    """Island-metre corners of the local block between columns c0..c1 and rows r0..r1."""
    (c0, r0), (c1, r1) = c0r0, c1r1
    return [dt_pos(c0, r0), dt_pos(c1, r0), dt_pos(c1, r1), dt_pos(c0, r1)]


# Cross-sections (metres). surface: asphalt | dirt.  markings: centre line (double_yellow | dashed | none).
PROFILES = {
    'highway': dict(median=1.2, sidewalk=0.0, parking=0.0, shoulder=1.8, kerb=False, markings='double_yellow', surface='asphalt', speed=25.0),
    'avenue': dict(median=0.6, sidewalk=4.0, parking=0.0, shoulder=0.0, kerb=True, markings='double_yellow', surface='asphalt', speed=16.7),
    'street': dict(median=0.0, sidewalk=3.0, parking=2.4, shoulder=0.0, kerb=True, markings='dashed', surface='asphalt', speed=11.1),
    'suburban': dict(median=0.0, sidewalk=2.2, parking=0.0, shoulder=0.6, kerb=True, markings='dashed', surface='asphalt', speed=11.1),
    'mountain': dict(median=0.0, sidewalk=0.0, parking=0.0, shoulder=1.0, kerb=False, markings='double_yellow', surface='asphalt', speed=12.5),
    'service': dict(median=0.0, sidewalk=0.0, parking=0.0, shoulder=0.8, kerb=False, markings='none', surface='asphalt', speed=8.3),
    'dirt': dict(median=0.0, sidewalk=0.0, parking=0.0, shoulder=0.6, kerb=False, markings='none', surface='dirt', speed=8.3),
}
MAX_GRADE = {'highway': 0.07, 'avenue': 0.08, 'street': 0.09, 'suburban': 0.11, 'mountain': 0.115, 'service': 0.12, 'dirt': 0.13}
SMOOTH = {'highway': 26.0, 'avenue': 16.0, 'street': 10.0, 'suburban': 10.0, 'mountain': 12.0, 'service': 8.0, 'dirt': 8.0}

ROUTES = [
    # Coast Highway: 2+2 lanes from downtown's east end round the island to its west end (Ocean Drive closes the loop).
    dict(id='coast', name='Coast Highway', kind='highway', lanes=(2, 2),
         pts=['D50', (236, -244), (270, -262), (310, -270), 'HM1', (450, -262), (530, -260), 'HM2', (662, -296), (712, -326),
              'HE1', (782, -250), (792, -120), (782, -10), (748, 64), 'HE3', (762, 196), 'TUNS', (792, 366), 'TUNN',
              (706, 540), 'HN1', (500, 596), (452, 592), (360, 608), (246, 636), (120, 652), (0, 650), (-120, 660),
              (-240, 656), (-360, 644), 'HN4', (-560, 596), (-640, 548), (-710, 484), (-760, 402), (-782, 300),
              'BRF1', 'BRF2', (-758, 88), 'HW2', (-762, -40), (-748, -140), (-716, -232), (-660, -312), (-612, -372),
              'BRHW', 'BRHE', (-428, -372), 'T3', 'T4', (-300, -282), 'D00'],
         bridges=[('BRHW', 'BRHE'), ('BRF1', 'BRF2')], tunnels=[('TUNS', 'TUNN')]),
    # Ocean Drive: the highway's town section, a palm boulevard along the crescent bay (downtown streets T into it).
    dict(id='ocean_drive', name='Ocean Drive', kind='avenue', lanes=(2, 2), pts=['D00', 'D10', 'D20', 'D30', 'D40', 'D50']),
    # Genesis Avenue west over the river to the Residences and the west coast.
    dict(id='genesis_w', name='Genesis Avenue', kind='avenue', lanes=(2, 2),
         pts=['D04', 'BR1A', 'BR1B', 'R1', (-456, 48), 'R2', (-580, 54), 'R3', (-706, 46), 'HW2'],
         bridges=[('BR1A', 'BR1B')]),
    # Farm Road: Genesis Avenue east of downtown, rolling out to the farm and the east coast.
    dict(id='farm_road', name='Farm Road', kind='avenue', lanes=(1, 1),
         pts=['D54', (262, 44), (340, 58), (424, 70), 'F1', (580, 92), 'F2', (700, 122), 'HE3']),
    # Hills Drive: Timeline Avenue north of downtown, winding up the Builder Hills terraces to the coast highway.
    dict(id='hills_drive', name='Hills Drive', kind='avenue', lanes=(1, 1),
         pts=['D56', (190, 204), 'B1', (262, 238), (344, 242), 'B2', (486, 274), (448, 300), (344, 296),
              'B3', (222, 330), (268, 360), (360, 364), 'B4', (520, 386), 'B5', (612, 470), 'HN1']),
    # Marina: the quay promenade along the basin and the west quay out to Marina Point.
    dict(id='marina_promenade', name='Marina Promenade', kind='street', lanes=(1, 1),
         pts=['HM1', (380, -300), 'MQ', (470, -320), (540, -322), (590, -314), (606, -292), 'HM2']),
    dict(id='marina_quay', name='Yacht Quay', kind='service', lanes=(1, 1), pts=['MQ', (398, -372), (388, -424), (374, -450)]),
    # Harbour Road into the Trenches, and Quay Street.
    dict(id='harbour_road', name='Harbour Road', kind='avenue', lanes=(1, 1),
         pts=['D01', (-236, -186), (-290, -208), 'T1', (-372, -250), 'T2', (-414, -312), 'T3']),
    dict(id='quay_road', name='Quay Street', kind='service', lanes=(1, 1), pts=['T1', (-334, -286), 'T4']),
    # Breakpoint Way: downtown north-west over the river to Breakpoint Park, then down to the Residences.
    dict(id='breakpoint_way', name='Breakpoint Way', kind='avenue', lanes=(1, 1),
         pts=['D16', (-156, 170), 'BR3A', 'BR3B', 'P1', (-288, 292), 'P2', (-390, 286), (-420, 220), 'R0'],
         bridges=[('BR3A', 'BR3B')]),
    dict(id='foothill_road', name='Foothill Road', kind='street', lanes=(1, 1),
         pts=['P1', (-176, 296), 'FH1', (-70, 334), 'FH2', 'BR4A', 'BR4B', 'FH3', (140, 282), 'B1'],
         bridges=[('BR4A', 'BR4B')]),
    # Summit Road: contour-following switchbacks up the summit's south-east face to the MtnDAO basecamp lodge.
    dict(id='summit_road', name='Summit Road', kind='mountain', lanes=(1, 1),
         switchback=dict(start='FH1', end='MR1', east=-108.0, west=-296.0, end_x=-238.0, grade=0.08, legs=3, first='west',
                         radius=20.0)),
    # Residences: a lane arcing north of Genesis Avenue, a crescent south of it, cul-de-sacs off both.
    dict(id='res_spine', name='Seed Phrase Lane', kind='suburban', lanes=(1, 1),
         pts=['R0', (-482, 152), 'RS1', (-618, 128), 'R3']),
    dict(id='res_link', name='Mnemonic Way', kind='suburban', lanes=(1, 1), pts=['R1', (-404, 86), 'R0']),
    dict(id='res_crescent', name='Cold Wallet Crescent', kind='suburban', lanes=(1, 1),
         pts=['R2', (-512, -6), 'RC1', (-414, -30), 'R1']),
    dict(id='res_close', name='Paper Hands Close', kind='suburban', lanes=(1, 1), pts=['RC1', (-470, -84), (-484, -132)]),
    dict(id='res_rise', name='Diamond Hands Rise', kind='suburban', lanes=(1, 1), pts=['RS1', (-552, 214), (-584, 246)]),
    dict(id='res_cliff', name='Cliffside Drive', kind='suburban', lanes=(1, 1), pts=['R3', (-664, -10), (-690, -96), (-700, -150)]),
    # Farm dirt roads.
    dict(id='farm_track', name='Harvest Track', kind='dirt', lanes=(1, 1), pts=['F2', (668, 180), (640, 236), (590, 250)]),
    dict(id='farm_lane', name='Sybil Lane', kind='dirt', lanes=(1, 1), pts=['F1', (520, 0), (588, -60), (650, -70)]),
    # Causeway to the islet.
    dict(id='causeway', name='Palm Causeway', kind='service', lanes=(1, 1), pts=['HE1', (790, -410), (800, -470), (802, -532), (802, -590)]),
]

# Junction kinds (default: signal where listed, stop elsewhere; roundabouts listed explicitly).
ROUNDABOUTS = {'B1': 15.0, 'HM1': 17.0, 'D12': 15.0, 'D45': 15.0}
SIGNALS = {'D04', 'D14', 'D24', 'D34', 'D44', 'D54', 'D00', 'D10', 'D20', 'D30', 'D40', 'D50', 'D31', 'D32', 'D33', 'D35',
           'D36', 'D16', 'D01', 'D23', 'D56', 'HE3', 'F1', 'T1', 'T3', 'HM2'}

# ------------------------------------------------------------------ venues
# Each public venue sits on a real street and faces it (any yaw). Placement:
#   ('front', route, near (x, z), side, gap): on that side of the route near the point, door facing the road;
#   ('end', route, node, gap): beyond a dead-end road, facing it;   ('at', x, z, yaw): explicit.
# gap = metres between the sidewalk edge and the venue frame.
VENUES = [
    dict(id='timeline_square', district='timeline', place=('at', 0.0, 0.0, 360.0 - DT_ROT, 0.0)),
    dict(id='spaces_studio', district='timeline', place=('front', 'genesis', (-96, 38), 'north', 2.0)),
    dict(id='discord_heights', district='timeline', place=('front', 'dt_v4', (118, 80), 'east', 4.0)),
    dict(id='ct_jail', district='timeline', place=('front', 'dt_h2', (100, -88), 'south', 4.0)),
    dict(id='conference_hall', district='breakpoint', place=('front', 'breakpoint_way', (-272, 287), 'north', 8.0)),
    dict(id='ratio_arena', district='breakpoint', place=('front', 'breakpoint_way', (-352, 300), 'south', 8.0)),
    dict(id='terminal_cafe', district='trenches', place=('front', 'harbour_road', (-296, -200), 'north', 4.0)),
    dict(id='perp_pit', district='trenches', place=('front', 'harbour_road', (-372, -252), 'south', 5.0)),
    dict(id='launchpad_tower', district='trenches', place=('front', 'quay_road', (-331, -252), 'east', 5.0)),
    dict(id='rekt_clinic', district='trenches', place=('front', 'coast', (-380, -334), 'north', 4.0)),
    dict(id='prediction_pit', district='boardwalk', place=('at', 96.0, -258.0, 180.0, SEA + 2.8)),   # arcade pier deck
    dict(id='cabal_lounge', district='dubai_marina', place=('front', 'marina_promenade', (470, -320), 'north', 4.0)),
    dict(id='penthouse_row', district='dubai_marina', place=('front', 'marina_promenade', (562, -322), 'north', 4.0)),
    dict(id='yacht_club', district='dubai_marina', place=('front', 'marina_quay', (396, -398), 'west', 4.0)),
    dict(id='checker_kiosk', district='airdrop_farm', place=('front', 'farm_road', (470, 68), 'south', 6.0)),
    dict(id='airdrop_fields', district='airdrop_farm', place=('front', 'farm_road', (612, 100), 'north', 6.0)),
    dict(id='sybil_barn', district='airdrop_farm', place=('front', 'farm_lane', (600, -62), 'south', 6.0)),
    dict(id='audit_row', district='builder_hills', place=('front', 'hills_drive', (300, 240), 'north', 5.0)),
    dict(id='vc_row', district='builder_hills', place=('front', 'hills_drive', (330, 296), 'north', 5.0)),
    dict(id='ore_refinery', district='mountain', place=('front', 'foothill_road', (124, 294), 'north', 6.0)),
    dict(id='hackathon_house', district='mountain', place=('end', 'summit_road', 'MR1', 4.6)),
]

# Venue building shells (W2): the street-side buildings round each room diorama. h = wall height (m), roof flat |
# gable | none, style = facade (plaster, glass, industrial, timber, arena, arcade), colours sRGB 0..1. The roof and the
# camera-side walls fade while the player is inside. Open-air venues (plaza, fields, decks) have no shell.
SHELLS = {
    'spaces_studio': dict(h=11.0, roof='flat', style='plaster', wall=(0.80, 0.42, 0.34), trim=(0.95, 0.92, 0.86), accent=(0.22, 0.24, 0.32)),
    'discord_heights': dict(h=17.0, roof='flat', style='glass', wall=(0.42, 0.44, 0.78), trim=(0.92, 0.93, 0.98), accent=(0.55, 0.48, 0.95)),
    'ct_jail': dict(h=9.0, roof='flat', style='plaster', wall=(0.62, 0.62, 0.60), trim=(0.40, 0.40, 0.42), accent=(0.86, 0.66, 0.20)),
    'conference_hall': dict(h=14.0, roof='flat', style='glass', wall=(0.86, 0.88, 0.90), trim=(0.30, 0.32, 0.36), accent=(0.58, 0.32, 0.86)),
    'ratio_arena': dict(h=13.0, roof='none', style='arena', wall=(0.86, 0.84, 0.80), trim=(0.36, 0.38, 0.44), accent=(0.86, 0.22, 0.32)),
    'terminal_cafe': dict(h=8.2, roof='flat', style='plaster', wall=(0.92, 0.82, 0.62), trim=(0.98, 0.96, 0.90), accent=(0.24, 0.52, 0.42)),
    'perp_pit': dict(h=10.0, roof='flat', style='industrial', wall=(0.36, 0.40, 0.46), trim=(0.85, 0.65, 0.20), accent=(0.20, 0.85, 0.55)),
    'launchpad_tower': dict(h=22.0, roof='flat', style='glass', wall=(0.30, 0.34, 0.40), trim=(0.92, 0.94, 0.98), accent=(0.95, 0.45, 0.20)),
    'rekt_clinic': dict(h=9.0, roof='flat', style='plaster', wall=(0.94, 0.94, 0.92), trim=(0.36, 0.70, 0.52), accent=(0.86, 0.24, 0.24)),
    'prediction_pit': dict(h=8.0, roof='flat', style='arcade', wall=(0.96, 0.86, 0.60), trim=(0.92, 0.30, 0.36), accent=(0.30, 0.85, 0.95)),
    'cabal_lounge': dict(h=10.0, roof='flat', style='glass', wall=(0.20, 0.20, 0.26), trim=(0.86, 0.72, 0.40), accent=(0.62, 0.30, 0.86)),
    'sybil_barn': dict(h=6.5, roof='gable', style='timber', wall=(0.68, 0.18, 0.14), trim=(0.94, 0.92, 0.86), accent=(0.30, 0.20, 0.14)),
    'audit_row': dict(h=10.0, roof='flat', style='plaster', wall=(0.86, 0.84, 0.76), trim=(0.30, 0.42, 0.62), accent=(0.24, 0.36, 0.60)),
    'vc_row': dict(h=12.0, roof='flat', style='glass', wall=(0.74, 0.80, 0.84), trim=(0.24, 0.26, 0.30), accent=(0.20, 0.70, 0.60)),
    'ore_refinery': dict(h=12.0, roof='flat', style='industrial', wall=(0.70, 0.42, 0.22), trim=(0.24, 0.24, 0.26), accent=(0.95, 0.75, 0.25)),
    'hackathon_house': dict(h=4.2, roof='gable', pitch=56.0, roof_col=(0.66, 0.20, 0.15), style='timber', wall=(0.56, 0.38, 0.24), trim=(0.92, 0.88, 0.80), accent=(0.86, 0.32, 0.20),
                            sign='BASECAMP LODGE'),
}

# Private homes: exteriors only (visits stay private), placed where each kind of home belongs.
HOMES = [
    dict(id='home_parents_basement', district='residences', place=('front', 'res_crescent', (-440, -36), 'south', 6.0)),
    dict(id='home_studio', district='residences', place=('front', 'genesis_w', (-470, 50), 'north', 6.0)),
    dict(id='home_hacker_house', district='builder_hills', place=('front', 'hills_drive', (300, 362), 'north', 5.0)),
    dict(id='home_penthouse', district='dubai_marina', place=('front', 'marina_promenade', (514, -321), 'north', 4.0)),
    dict(id='home_marina_villa', district='dubai_marina', place=('at', 350.0, -428.0, 300.0, None)),
]

# ------------------------------------------------------------------ landmarks (big, seen from afar) and helipads
# venue=...: the landmark is that venue's building (its frame centre).
LANDMARKS = [
    dict(id='mtndao_summit', name='MtnDAO summit', kind='summit', at=(-180, 495), egg='Mtndao'),
    dict(id='mtndao_lodge', name='Basecamp lodge', kind='lodge', venue='hackathon_house', egg='Mtndao'),
    dict(id='colosseum_hall', name='Arena hall', kind='colosseum', venue='ratio_arena', egg='Colosseum'),
    dict(id='breakpoint_centre', name='Convention centre', kind='convention', venue='conference_hall', egg='Breakpoint 2026'),
    dict(id='superteam_hq', name='Builder HQ', kind='tower', at=(392, 372), egg='Superteam'),
    dict(id='jupiter_observatory', name='Observatory', kind='dome', at=(846, 386), egg='Jupiter'),
    dict(id='seeker_shop', name='Phone shop', kind='shop', at=(-96, -58), egg='Seeker (Solana Mobile)'),
    dict(id='helius_depot', name='Delivery depot', kind='depot', at=(-252, -176), egg='Helius'),
    dict(id='lighthouse', name='Lighthouse', kind='lighthouse', at=(676, -532), egg=None),
    dict(id='island_palm', name='Palm islet', kind='palm', at=(800, -646), egg='IslandDAO'),
    dict(id='timeline_screens', name='Timeline screens', kind='billboards', at=(-10, 52), egg=None),
    dict(id='harbour_bridge', name='Harbour bridge', kind='bridge', at=(-510, -400), egg=None),
    dict(id='rug_room', name='The Rug Room', kind='casino', at=(126, -262), egg=None),
    dict(id='rekt_burger', name='Rekt Burger', kind='shop', at=(-301.8, -306.7), egg=None),
]
# Landmark buildings (W3): non-venue heroes built like venue shells (facade styles as SHELLS) on their own pads.
# at = centre, size = (w, d) m, yaw degrees (0 = the sign faces south), extra = a feature the builder adds.
LANDMARK_BUILDINGS = [
    dict(id='superteam_hq', at=(392, 372), size=(16, 16), yaw=200.0, h=30.0, roof='flat', style='glass', sign='BUILDER HQ',
         wall=(0.40, 0.24, 0.86), trim=(0.97, 0.66, 0.08), accent=(0.97, 0.66, 0.08), extra='bunting'),
    dict(id='jupiter_observatory', at=(846, 386), size=(18, 18), yaw=250.0, h=7.0, roof='dome', style='plaster', sign='OBSERVATORY',
         wall=(0.94, 0.94, 0.92), trim=(0.40, 0.72, 0.86), accent=(0.30, 0.62, 0.86), extra='dome'),
    dict(id='seeker_shop', at=(-96, -58), size=(12, 10), yaw=174.0, h=6.0, roof='flat', style='plaster', sign='PHONE SHOP',
         wall=(0.22, 0.22, 0.26), trim=(0.62, 0.40, 0.92), accent=(0.95, 0.50, 0.30), extra='phone'),
    dict(id='helius_depot', at=(-252, -176), size=(20, 14), yaw=40.0, h=7.0, roof='flat', style='industrial', sign='faster',
         wall=(0.16, 0.16, 0.18), trim=(0.91, 0.25, 0.14), accent=(0.91, 0.25, 0.14), extra='neon'),
    # The Rug Room (R3, 2026-10-24): the Cash-only casino at the arcade pier's east end, door north onto the beach and the
    # boardwalk, beside the Prediction Pit. Its pad extends the pier (fixed at the pier deck's level, not the median of a
    # seabed sample); walkin = the streamed room's door (w, h) in its front wall; sign_art = sign_rug_room.png comes from
    # art/source/casino (neon), not signs.py's board; extra neon_rug lights the board and the facade's neon tubes.
    dict(id='rug_room', at=(126, -262), size=(16, 20), yaw=180.0, h=9.0, roof='flat', style='plaster', sign='THE RUG ROOM',
         wall=(0.95, 0.92, 0.86), trim=(0.79, 0.63, 0.31), accent=(1.0, 0.25, 0.64), extra='neon_rug',
         fix_y=SEA + 2.8, walkin=(1.2, 2.3), sign_art=True),
    # Rekt Burger (2026-10-25, backend PR #16): the parody fast-food fallback job, a glass-fronted burger joint on the Coast
    # Highway in The Trenches, on the seaward side (the shore ~16 m behind), door facing the highway (NW); the lot clears the
    # highway's shoulder by 2 m and every other road, pad and landmark (the first lot let the asphalt into the room).
    # walkin = the streamed room's door; sign_art = sign_rekt_burger.png from art/source/rektburger; extra neon_rekt keeps the
    # board's own orange and mint under the emission.
    dict(id='rekt_burger', at=(-301.8, -306.7), size=(13, 14), yaw=145.0, h=6.2, roof='flat', style='plaster', sign='REKT BURGER',
         wall=(0.16, 0.17, 0.19), trim=(1.0, 0.42, 0.17), accent=(0.22, 0.88, 0.64), extra='neon_rekt',
         walkin=(1.2, 2.3), sign_art=True),
    dict(id='mtndao_cairn', at=(-182, 513), size=(4, 4), yaw=180.0, h=0.0, roof='none', style='none', sign=None,
         wall=(0.5, 0.5, 0.5), trim=(0.5, 0.5, 0.5), accent=(0.86, 0.32, 0.20), extra='cairn'),
]

# Hero statues (W3, Tripo3D originals that nod to community mascots): placed by dress.py on a free spot near `near`,
# facing the nearest road, on a stone plinth (w, d, h m). tint multiplies the statue's own texture (1 = as made).
HEROES = [
    dict(id='black_bull', proto='hero_black_bull', near=(-338, -222), plinth=(5.2, 2.8, 1.1), tint=None, egg='The Black Bull'),
    dict(id='chef_bull', proto='hero_chef_bull', near=(-150, -52), plinth=(3.0, 3.0, 0.8), tint=None, egg='Marinade'),
    dict(id='purple_ghost', proto='hero_purple_ghost', near=(-30, 104), plinth=(2.4, 2.4, 0.5), tint=(0.70, 0.55, 1.0), egg='Phantom'),
    dict(id='penguin_cart', proto='hero_penguin_cart', near=(34, 118), plinth=None, tint=None, egg='Pudgy Penguins'),
    dict(id='space_cat', proto='hero_space_cat', near=(822, 372), plinth=(3.2, 3.2, 1.0), tint=None, egg='Jupiter'),
]
# Helipads (x, z); ('venue', id, offset along the venue's side) puts one next to a venue.
HELIPADS = [(-226.0, 428.0), (640.0, -262.0), (-300.0, 214.0)]   # lodge pad: on the shoulder, north of the road end

# Dirt tracks and footpaths painted into the ground (name, points, width m): trails, field tracks, park and beach paths.
TRACKS = [
    ('summit_trail', [(-262, 420), (-246, 428), (-238, 418), (-228, 432), (-222, 446), (-210, 452), (-214, 466), (-200, 476),
                      (-204, 490), (-190, 500), (-184, 512)], 2.0),
    ('beach_path_w', [(-104, -210), (-108, -238)], 3.0),
    ('beach_path_c', [(52, -202), (56, -230)], 3.0),
    ('beach_path_e', [(150, -220), (160, -252)], 3.0),
    ('lake_loop', [(-318, 232), (-308, 256), (-286, 258), (-276, 238), (-288, 218), (-310, 216), (-318, 232)], 2.5),
    ('riverside_walk', [(-236, 176), (-250, 120), (-262, 60), (-270, 0), (-274, -60)], 2.5),
    ('lighthouse_path', [(640, -478), (652, -506), (676, -528)], 2.5),
    ('observatory_path', [(796, 344), (818, 362), (840, 382)], 2.5),
    ('field_track_n', [(600, 116), (606, 160), (604, 204)], 3.0),
    ('field_track_s', [(556, -26), (560, 20), (562, 56)], 3.0),
    ('orchard_track', [(650, -70), (700, -96), (744, -100)], 3.0),
]

# Farm patchwork: field blocks (polygon, row direction degrees, crop) used for ground colour now and crops later.
FIELDS = [
    ([(520, 110), (600, 118), (596, 196), (514, 186)], 12, 'wheat'),
    ([(604, 120), (690, 128), (684, 214), (600, 200)], 12, 'corn'),
    ([(696, 140), (744, 146), (742, 220), (690, 214)], 100, 'sunflower'),
    ([(470, -10), (546, -24), (556, 40), (478, 52)], 168, 'cabbage'),
    ([(560, -20), (640, -34), (652, 40), (570, 56)], 168, 'wheat'),
    ([(560, -110), (650, -120), (660, -80), (568, -70)], 4, 'lavender'),
    ([(660, -126), (742, -140), (750, -60), (668, -52)], 94, 'corn'),
    ([(470, -150), (552, -160), (560, -110), (478, -96)], 4, 'pumpkin'),
]
PARKS = [
    ('genesis_park', dt_block_poly((2, 4), (3, 6))),
    ('breakpoint_park', [(-330, 200), (-190, 210), (-196, 258), (-326, 266)]),
    ('riverside_park', [(-234, -120), (-224, 186), (-252, 186), (-276, 90), (-288, -60)]),
]
