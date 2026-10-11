using System.Collections.Generic;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Every venue diorama this pipeline builds. Venue ids are the existing spot-set ids in
    /// docs/fe-contract-proposals/ids/spots.json (map names in titles: Conference Center = conference_hall,
    /// Compliance Jail = ct_jail, Points Fields = airdrop_fields). checker_kiosk, yacht_club and penthouse_row are new
    /// spot sets appended there additively. Homes all use the shared "home" spot set and VenueSceneRoot.venueId "home".
    /// </summary>
    public static class VenueCatalog
    {
        private static Color C(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }

        private static VenueSpec V(string id, string title, string district, string identity, System.Action<VB> build) =>
            new VenueSpec { id = id, venueId = id, spotsKey = id, title = title, district = district, identity = identity, build = build };

        private static VenueSpec H(string homeId, string title, string identity, System.Action<VB> build) =>
            new VenueSpec
            {
                id = "home_" + homeId, venueId = "home", spotsKey = "home", homeId = homeId, title = title, district = "",
                identity = identity, build = build,
            }.Camera(9.0f, 34f);

        public static List<VenueSpec> All => new List<VenueSpec>
        {
            // Repacked pre-pipeline venues: same authored layout/anchors/components, merged + atlased + rebaked.
            // Timeline Square v2 (round 3 look-dev): layout build to the in-game concept; the old repack source is no longer used.
            new VenueSpec
            {
                id = "timeline_square", venueId = "timeline_square", spotsKey = "timeline_square", title = "Timeline Square", district = "timeline",
                identity = "Sunny Mediterranean square at golden hour: tiered fountain on a round deck, benches, café terrace, bakery, toyshop, market stall, " +
                           "clock tower and town hall; Timeline screen, Pixel Wall and two billboards (live surfaces); the 15 Tier A community eggs.",
                build = VenuesTimelineSquare.TimelineSquare, cellSize = 22f,
            }.SunAt(32f, 48f).Camera(14.5f, 33f).Look(C("ffd29a"), 1.25f, C("a9b4e6"), C("e2c39c"), C("7a6650"))
             .Grade(C("c9bcf0"), 0.06f, 15f, 16f, 7f, C("fff6ea"), bloom: 0.3f, bloomAt: 0.95f, vignetteValue: 0.15f),
            new VenueSpec
            {
                id = "terminal_cafe", venueId = "terminal_cafe", spotsKey = "terminal_cafe", title = "Terminal Café", district = "trenches",
                identity = "Trading café: 8 terminals, social tables, coffee bar, pulse screen, brick and glass shell (seat motion profile kept).",
                repackSource = "Assets/Art/Candidates/PlayableVenues20261007/terminal_cafe.prefab", cellSize = 12.5f, cellSizeZ = 6f, cellSplit = true, lod0Threshold = 0.65f,
                dress = VenuesCafeDress.Dress,
            }.Grade(C("d9cdf2"), 0.08f, 12f, 10f, 9f, C("fff2e2"), bloom: 0.4f, bloomAt: 0.9f, vignetteValue: 0.18f),
            // Spaces Studio, redesigned in round 3 (layout build; the old repack source is no longer used).
            V("spaces_studio", "Spaces Studio", "timeline",
                "Evening broadcast studio / rooftop (v4, to the in-game concept): round two-step stage with amber LED nosing, cream rug, 4 tan armchairs in a talk-show semicircle with boom mics, host desk + sun lamp, speaker pods with three-lens phones on mic stands, teal LED waveform wall + on-air lamp (live hooks), fluted purple/teal panels, truss spots, bean bags and poufs in arcs on curved risers, green-room lounge beside a rainbow neon arched door + lanyard frame, open glazing onto a terrace with lanterns, bunting and the city.",
                VenuesSpaces.SpacesStudio).SeatMotionFrom("Assets/Art/Candidates/PlayableVenues20261007/spaces_studio.prefab").Camera(11.6f, 29f, -12f, 45f, 0.3f).Look(C("ffb27a"), 0.55f, C("5b5a9e"), C("8a6a7a"), C("2a2238"))
                .Grade(C("3b3f7c"), 0.05f, 18f, 2f, -3f, C("f2ecff"), bloom: 0.65f, bloomAt: 0.85f, vignetteValue: 0.22f),
            // The Trenches
            V("perp_pit", "Perp Pit", "trenches",
                "Perp-DEX trading floor: leverage board + 24h casualties board (live hooks), 8 terminals, liquidation witness circle, CROSS/ISOLATED lounges, lever-o-meter.",
                VenuesTrenches.PerpPit).Camera(13f, 32f).Look(C("dfe8ff"), 0.9f, C("6a7a95"), C("5a5e66"), C("2a2d33")),
            V("rekt_clinic", "Rekt Clinic", "trenches",
                "Recovery ward: 6 beds with vitals monitors and curtains, triage desk, eye test (WAGMI/NGMI), waiting room, memorial wall and tombstone 'touch grass' garden.",
                VenuesTrenches.RektClinic).Camera(11.5f, 33f).Look(C("fff8ee"), 1.1f, C("a9c4d6"), C("b8c2c4"), C("5a6060")),
            V("launchpad_tower", "Launchpad Tower", "trenches",
                "Launch room: Pulse-ish board (FRESH MINTS/FINAL LAP/GRADUATED) + next-launch clock (live hooks), launch desk with red button in the swarm ring, bonding-curve floor line, rocket, skyline windows.",
                VenuesTrenches.LaunchpadTower).Camera(12f, 32f).Look(C("ffe2bf"), 1.0f, C("7b6f9c"), C("6a5f7a"), C("2e2838")),
            V("prediction_pit", "Prediction Pit", "trenches",
                "Sportsbook pit: ODDSBOARD (YES ¢ / NO ¢ / % chance, live hook), 6 betting booths, YES/NO floor halves, category banner, the Oracle, 'game Cash only' rules.",
                VenuesTrenches.PredictionPit).Camera(12f, 32f).Look(C("e8eeff"), 0.95f, C("6f86b8"), C("55617a"), C("262c3a")),
            // Timeline
            V("ratio_arena", "Ratio Arena", "timeline",
                "Quote-tweet fight ring: red corner (the OP) vs blue corner (the reply guy), RATIO METER (live hook), bleacher benches + crowd, L + RATIO / community-note banners, spotlights.",
                VenuesTimeline.RatioArena).Camera(12.5f, 33f).Look(C("ffe9f4"), 0.75f, C("6b5a85"), C("4a4258"), C("221d2a")),
            V("ct_jail", "Compliance Jail", "timeline",
                "Four barred cells labelled by offence (unregistered offering, wash trading, rug pull, forgot to KYC) with bunks, 'Know Your Degen' KYC desk, mugshot height wall, guard post.",
                VenuesTimeline.ComplianceJail).Camera(12f, 32f).Look(C("fff1df"), 0.95f, C("8f97a0"), C("7c7f82"), C("383a3c")),
            V("discord_heights", "Discord Heights", "timeline",
                "Discordia Heights server lounge: channel doors (#general #alpha-maybe #gm #wen-token #support-ticket #memes), #announcements board (live hook), verify-to-enter kiosk, mod desk + ban hammer, role-coloured runs.",
                VenuesTimeline.DiscordHeights).Camera(12f, 32f).Look(C("ece8ff"), 0.85f, C("6d63b0"), C("4e4a72"), C("23213a")),
            // Builder Hills
            V("conference_hall", "Conference Center", "builder_hills",
                "COIN2050 conference hall: main stage LED wall (live hook) with podium and truss lights, 12 audience seats, expo booths (Startup Hamlet, IV drip bar), booth mascot, lanyard registration, pitch timer, After2050 / Checkpoint London signs.",
                VenuesBuilderHills.ConferenceCenter).Camera(12.5f, 32f).Look(C("eef6ff"), 1.0f, C("7a92ad"), C("66717d"), C("2c3238")),
            V("hackathon_house", "Hackathon House", "builder_hills",
                "Buildooor Station: 16 m hacking table with 8 desks, submissions countdown (live hook), whiteboard plan, couch + beanbags + pizza, bunk and sleeping bags, energy-drink fridge, demo podium.",
                VenuesBuilderHills.HackathonHouse).Camera(12f, 32f).Look(C("fff0d8"), 1.05f, C("a6b4c4"), C("a39a8a"), C("4a443c")),
            V("audit_row", "Audit Row", "builder_hills",
                "Four audit booths behind glass, EXPLOIT ALARM board + red beacon (live hooks), REKT-O-BOARD, bug-bounty corkboard, 'funds are probably fine', giant magnifier, client queue.",
                VenuesBuilderHills.AuditRow).Camera(12f, 32f).Look(C("eef8f6"), 0.95f, C("7aa3a6"), C("5e6e72"), C("273033")),
            V("vc_row", "VC Row", "builder_hills",
                "Partner floor: glass pitch room with 6 seats and pitch-deck screen (live hook), WE ARE SO EARLY neon, term sheet, thesis/portfolio art, partner desk, library lounge.",
                VenuesBuilderHills.VcRow).Camera(12f, 32f).Look(C("fff1dc"), 1.0f, C("b3a58f"), C("a08f76"), C("4a3f32")),
            // Airdrop Farm
            V("ore_refinery", "ORE Refinery", "airdrop_farm",
                "Refinery hall: giant live 5×5 ORE board + round timer + motherlode (hooks), furnace bay with tintable glow hook, conveyors of glowing ore, 4 stake/unstake/claim/compound terminals, miner post, 'mine on ore.com, not here'.",
                VenuesAirdropFarm.OreRefinery).Camera(14f, 33f).Look(C("ffdcb0"), 0.9f, C("8a7a68"), C("6e5a46"), C("2e2318")),
            V("airdrop_fields", "Points Fields", "airdrop_farm",
                "Open-air points farm: beds of glowing points sprouts, SEASON ∞ leaderboard (live hook), airdrop crates (one still parachuting), Sybil scarecrow, season mile posts, claim-closed sign, barn on the horizon.",
                VenuesAirdropFarm.PointsFields).Camera(13f, 34f).Look(C("fff1d6"), 1.2f, C("9cc4e6"), C("a6b48c"), C("5a5a40")),
            V("sybil_barn", "Sybil Barn", "airdrop_farm",
                "Red barn: three clone pens of identical wallet farmers (WALLETS 0001–∞), phone-farm rack, wallets_final_v7.csv desk, WANTED: SYBIL SQUAD, hay-bale seats, lanterns.",
                VenuesAirdropFarm.SybilBarn).Camera(12f, 32f).Look(C("ffe3c0"), 0.95f, C("a28a72"), C("8a6a50"), C("3c2a1e")),
            V("checker_kiosk", "Checker Kiosk", "airdrop_farm",
                "Eligibility plaza: three checker kiosks under a canopy, ELIGIBILITY board (live hook), queue stanchions (est. wait: forever), NOT ELIGIBLE (lol) board, tiers poster, cope bench, confetti.",
                VenuesAirdropFarm.CheckerKiosk).Camera(10.5f, 34f).Look(C("fff4dc"), 1.2f, C("9fcbe8"), C("b6bba6"), C("5c5c4a")),
            // Dubai Marina
            V("cabal_lounge", "Cabal Lounge", "dubai_marina",
                "Members-only lounge: velvet booths under CABAL CHAT (7 members · 0 leaks, live hook), gold neon, guest-list rope + podium, glowing bottle bar with insider menu, '100% NOT BUNDLED' neon.",
                VenuesMarina.CabalLounge).Camera(12f, 32f).Look(C("ffd9b0"), 0.6f, C("5a3a52"), C("4a2f3c"), C("1e1418")),
            V("yacht_club", "Yacht Club", "dubai_marina",
                "Marina dock: teak deck, 6 loungers, bar with 4 stools, moored yacht NUMBER GO UP + gangway, EXIT LIQUIDITY in the marina, departures board (live hook), palms, orange supercar, tower canyon.",
                VenuesMarina.YachtClub).Camera(12.5f, 33f).Look(C("ffd6a6"), 1.15f, C("9cc0e0"), C("d9b48e"), C("4a5a6a")),
            V("penthouse_row", "Penthouse Row", "dubai_marina",
                "Rooftop of three penthouse fronts (PH-1..3, rent $1,500/week game Cash), concierge, infinity pool + 4 loungers, fire-bowl sofa lounge, glass balustrade, dusk skyline.",
                VenuesMarina.PenthouseRow).Camera(12.5f, 33f).Look(C("ffc89a"), 1.0f, C("8a7ab0"), C("b08a8a"), C("3a3040")),
            // Private homes (Parent's Basement already ships).
            H("hacker_house", "Hacker House", "Builder crash pad: bunk bed, triple-monitor desk + extra workstation, server rack, SHIP IT whiteboard, GM / WAGMI neon, pizza, beanbag, sleeping bag.",
                VenuesHomes.HackerHouse).Look(C("ffe6c8"), 1.0f, C("a39a9a"), C("8a7a70"), C("3a3230")),
            H("studio", "Studio", "First apartment: single bed, desk, small sofa + chart TV, kitchenette, daylight windows, rug and plants.",
                VenuesHomes.Studio).Look(C("fff2dc"), 1.1f, C("b4c4d4"), C("b0a898"), C("4e4a42")),
            H("penthouse", "Penthouse", "Marble penthouse: king bed, L-couch, chandelier, bar cart, night-skyline windows, 'I bought the top' frame.",
                VenuesHomes.Penthouse).Look(C("ffe2c4"), 0.95f, C("8a84a8"), C("9a8e8a"), C("3a3438")),
            H("marina_villa", "Marina Villa", "Waterfront villa: king bed, L-couch, fireplace, open glass back onto the pool terrace, palms, yacht and the marina towers.",
                VenuesHomes.MarinaVilla).Look(C("ffd9ae"), 1.15f, C("9cc4e4"), C("d4b894"), C("4e5866")),
        };
    }
}
