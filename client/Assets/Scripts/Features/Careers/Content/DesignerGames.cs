using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// Graphic Designer: billboards, Pixel Wall tiles, sponsored phone-app art (spec "Projects" layer 3) and NFT
    /// collections. Anchors: WCAG 2.1 contrast 4.5:1; the spec's "Sponsored" label rule; NFT trait rarity work.
    /// </summary>
    internal static class DesignerGames
    {
        public static IEnumerable<MiniGameSpec> Specs()
        {
            yield return new MiniGameSpec
            {
                Id = "designer_canvas", CareerId = "graphic_designer", InContract = true, Engine = MiniGameEngine.Sort,
                Title = "Readable or Not", Tagline = "Billboards nobody can read sell nothing.",
                Rules = new[]
                {
                    "Each card is a billboard colour pair, shown for real.",
                    "Readable: you could read it from across Timeline Square. Unreadable: it smears.",
                    "The studio rule is a contrast ratio of at least 4.5 to 1. Borderline pairs never appear.",
                },
                ScoringRule = "Right calls minus wrong calls, out of the stack.",
                RealTask = "Checking ad and UI colour pairs against the WCAG 4.5:1 contrast rule.",
                ActionLabel = "Readable|Unreadable",
                DefaultDuration = 40,
                Create = init => new SortRound(new[] { "Readable", "Unreadable" }, ContrastDeck(new DetRng(init.Seed), 14), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "designer_trait_mixer", CareerId = "graphic_designer", Engine = MiniGameEngine.Combo,
                Title = "Trait Rarity Mixer", Tagline = "Build the mint the brief asks for.",
                Rules = new[]
                {
                    "Pick one trait for each slot. Every trait shows its rarity points.",
                    "The mint's total must land inside the brief's band.",
                    "No running total: add it up yourself, then mint.",
                },
                ScoringRule = "One point per mint that lands in its band.",
                RealTask = "Balancing trait rarity tables for an NFT collection.",
                DefaultDuration = 50,
                Create = init => new ComboRound(TraitBoards(new DetRng(init.Seed), 4), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "designer_billboard_proof", CareerId = "graphic_designer", Engine = MiniGameEngine.Flag,
                Title = "Billboard Proof", Tagline = "Catch it before it goes up on Solana Street.",
                Rules = new[]
                {
                    "Each spec sheet describes one placement. Flag every line that breaks the house rules:",
                    "'Sponsored' label on every placement · 40 px safe area · text at least 24 px · contrast at least 4.5:1.",
                    "No fake system pop-ups · no promised returns · no real faces without consent.",
                },
                ScoringRule = "Per sheet: rule breaks found minus clean lines flagged (never below zero).",
                RealTask = "Pre-flight proofing of sponsored placements before review.",
                DefaultDuration = 50,
                Create = init => new FlagRound(ContentKit.Boards(new DetRng(init.Seed), Proofs, 4), init.MaxScore, init.Duration),
            };
        }

        // ------------------------------------------------------------ contrast (WCAG 2.1 relative luminance)

        public static double Luminance(uint rgb)
        {
            double Ch(uint v)
            {
                double c = v / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Ch((rgb >> 16) & 0xFF) + 0.7152 * Ch((rgb >> 8) & 0xFF) + 0.0722 * Ch(rgb & 0xFF);
        }

        public static double Contrast(uint a, uint b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        public const double ReadableAtLeast = 5.0, UnreadableAtMost = 2.6;

        private static readonly string[] Placements =
        {
            "Billboard 03 · Timeline Square", "Pixel Wall tile 118", "Billboard 11 · Solana Street", "Phone app splash",
            "Spaces Studio backdrop", "Perp Pit scoreboard ad", "Launchpad Tower lobby screen", "Conference Week booth",
        };

        private static readonly string[] Samples = { "LIVE NOW", "MINT OPENS 18:00", "GM CT CITY", "LP BURNED", "SEASON 2", "NEW PERPS", "ORE WEEK", "TAP TO PLAY" };

        private static uint RandomColour(DetRng rng) => (uint)rng.Next(0x1000000);

        public static List<SortCard> ContrastDeck(DetRng rng, int size)
        {
            var cards = new List<SortCard>();
            for (int i = 0; i < size; i++)
            {
                bool readable = i % 2 == 0;
                uint bg = RandomColour(rng), fg = 0;
                double ratio = 0;
                bool found = false;
                for (int attempt = 0; attempt < 400 && !found; attempt++)
                {
                    fg = RandomColour(rng);
                    ratio = Contrast(fg, bg);
                    found = readable ? ratio >= ReadableAtLeast : ratio >= 1.25 && ratio <= UnreadableAtMost;
                    if (!found && attempt % 50 == 49) bg = RandomColour(rng);
                }
                if (!found)
                {
                    // Deterministic fallbacks that always satisfy their class.
                    bg = readable ? 0x14213Fu : 0x8A8A8Au;
                    fg = readable ? 0xFFFFFFu : 0x9C9C9Cu;
                    ratio = Contrast(fg, bg);
                }
                string coin = rng.Pick(ContentKit.Coins);
                string text = coin + " " + rng.Pick(Samples);
                cards.Add(new SortCard
                {
                    Title = rng.Pick(Placements), Body = null, Bin = readable ? 0 : 1, Swatch = new Swatch(fg, bg, text),
                    Why = "Contrast " + ratio.ToString("0.0", ContentKit.Inv) + ":1 — " + (readable ? "passes" : "fails") + " the 4.5:1 rule.",
                });
            }
            rng.Shuffle(cards);
            return cards;
        }

        // ------------------------------------------------------------ trait mixer

        private static readonly (string slot, string[] traits)[] TraitSlots =
        {
            ("Background", new[] { "Trench Mud", "Marina Sunset", "Terminal Green", "Rekt Pink", "Refinery Glow", "Spaces Purple" }),
            ("Body", new[] { "Frog", "Anime Girl", "Anime Boy", "Hooded Anon", "Robot Agent", "Rekt Guy" }),
            ("Headwear", new[] { "Hard Hat", "Party Hat", "Halo", "Crown of Copium", "Ring Light", "Straw Hat" }),
            ("Eyes", new[] { "Laser", "Sleepy", "3D Glasses", "Diamond", "Chart Squint", "Cold Wallet Stare" }),
        };

        private static readonly string[] Collections = { "Froge Club", "Anime PFP Society", "Rekt Clinic Patients", "Refinery Crew", "Reply Guy Club" };

        public static List<ComboBoard> TraitBoards(DetRng rng, int count)
        {
            var boards = new List<ComboBoard>();
            for (int b = 0; b < count; b++)
            {
                var slots = new List<ComboSlot>();
                foreach (var (slot, traits) in TraitSlots)
                {
                    var picks = rng.Sample(traits, 3);
                    var values = new[] { rng.Range(2, 9), rng.Range(11, 25), rng.Range(28, 49) };
                    rng.Shuffle(values);
                    slots.Add(new ComboSlot
                    {
                        Name = slot,
                        Options = picks.Select((t, i) => new ComboOption(t + " · " + values[i] + " pts", values[i])).ToArray(),
                    });
                }
                var board = new ComboBoard { Title = rng.Pick(Collections) + " · mint #" + rng.Range(100, 9999), Unit = "pts", Slots = slots.ToArray() };
                // Target one real combination with a narrow band; widen only if nothing else changes the answer count.
                int[] target = slots.Select(s => rng.Next(s.Options.Length)).ToArray();
                int total = target.Select((o, i) => slots[i].Options[o].Value).Sum();
                board.Min = total - 3;
                board.Max = total + 3;
                if (board.CountSolutions() > 4) { board.Min = total - 1; board.Max = total + 1; }
                board.Brief = "Brief: this mint must score " + board.Min + "–" + board.Max + " rarity points.";
                boards.Add(board);
            }
            return boards;
        }

        // ------------------------------------------------------------ billboard proofs

        private static FlagLine Ok(string t, string why = null) => new FlagLine(t, false, why);
        private static FlagLine Bad(string t, string why) => new FlagLine(t, true, why);

        private static readonly FlagBoard[] Proofs =
        {
            new FlagBoard
            {
                Title = "Billboard 07 · Timeline Square · $WAGMEOW", Brief = "Find 2 rule breaks.",
                Lines = new[]
                {
                    Ok("Sponsored label: top-left, 48 px."),
                    Ok("Headline 'WAGMEOW IS LIVE', 120 px, white on navy (12:1)."),
                    Bad("Subline: '100x incoming', 60 px.", "A promised return. Not allowed on any placement."),
                    Bad("Logo sits 20 px from the right edge.", "Inside the 40 px safe area: it gets cropped on the street."),
                    Ok("QR code to the project's docs page, 60 px from the edge."),
                    Ok("Background: plain two-tone gradient."),
                },
            },
            new FlagBoard
            {
                Title = "Pixel Wall tile 042 · $SOGGY", Brief = "Find 3 rule breaks.",
                Lines = new[]
                {
                    Bad("Sponsored label: left off, the tile is small.", "Every placement carries the label, tiny ones included."),
                    Bad("Text 'Free mint' at 18 px.", "Below the 24 px minimum."),
                    Bad("Yellow text on white (1.4:1).", "Contrast far below 4.5:1."),
                    Ok("Link goes to the reviewed destination."),
                    Ok("Art: original frog, drawn in-house."),
                },
            },
            new FlagBoard
            {
                Title = "Phone app splash · Solana Street Swap", Brief = "Find 2 rule breaks.",
                Lines = new[]
                {
                    Ok("'Sponsored' tag on the app tile."),
                    Bad("Art includes a fake 'Connect wallet' system pop-up.", "Fake system UI tricks people into tapping."),
                    Ok("Body copy 28 px, ink on white (15:1)."),
                    Ok("Fee table on the first screen."),
                    Ok("Safe area respected on all sides."),
                    Bad("Mascot says 'You can't lose money here!'", "A promise no trading app can make."),
                },
            },
            new FlagBoard
            {
                Title = "Conference Week booth banner", Brief = "Find 2 rule breaks.",
                Lines = new[]
                {
                    Ok("Sponsored label: bottom-right, 40 px."),
                    Bad("Uses a famous exchange founder's face; no consent on file.", "A real person's likeness needs permission."),
                    Ok("Booth number in 96 px."),
                    Bad("QR code 30 px from the edge.", "Inside the 40 px safe area."),
                    Ok("Black on gold (10:1)."),
                    Ok("Tagline: 'Builders welcome'."),
                },
            },
            new FlagBoard
            {
                Title = "Spaces Studio backdrop · $GLORPO", Brief = "Find 2 rule breaks.",
                Lines = new[]
                {
                    Ok("Sponsored strip along the top edge, 44 px."),
                    Ok("Host name in 64 px."),
                    Bad("Corner ticker: '$GLORPO GUARANTEED'.", "A guarantee is a promised return."),
                    Bad("Room rules in light grey on white (1.6:1).", "Contrast far below 4.5:1: nobody reads the rules."),
                    Ok("Room rules: no shilling during Q&A."),
                },
            },
            new FlagBoard
            {
                Title = "Perp Pit scoreboard ad · Leverage Lounge", Brief = "Find 2 rule breaks.",
                Lines = new[]
                {
                    Ok("Sponsored label above the ad, 36 px."),
                    Bad("Fine print 'Losses possible' at 12 px.", "Risk text still has to meet the 24 px minimum."),
                    Ok("Leverage tiers shown as a table."),
                    Bad("'Ape now' button styled like the phone's system dialog.", "Fake system UI."),
                    Ok("White on charcoal (13:1)."),
                },
            },
        };
    }
}
