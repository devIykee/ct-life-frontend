using System;
using System.Collections.Generic;
using System.Globalization;
using CTLife.Core;

namespace CTLife.CityLife.Casino
{
    /// <summary>
    /// Copy for the Rug Room (English). Policy (retention plan §1, P2/P4/P5): play money only, no cash-out wording, nothing that
    /// reads as convertible, no gambling-operator ads or links. Every screen shows the server's disclaimer.
    /// </summary>
    public static class CasinoText
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["app.title"] = "The Rug Room",
            ["app.label"] = "Rug Room",
            ["app.tagline"] = "Play-money slots, Moon or Rug and coinflip on the Boardwalk pier.",
            ["app.enter"] = "Enter the Rug Room",
            ["app.walk"] = "Or walk in: the Art-Deco pavilion at the arcade pier end.",
            ["app.today"] = "Today's limits",

            ["tab.slots"] = "SLOTS", ["tab.crash"] = "MOON OR RUG", ["tab.coinflip"] = "COINFLIP",
            ["tab.fair"] = "Fairness", ["tab.limits"] = "Limits",

            ["hud.cash"] = "CASH",
            ["hud.left"] = "{0} left today",
            ["hud.session"] = "{0} min this session",

            ["stake"] = "STAKE",
            ["stake.max"] = "Up to {0} today",

            // slots
            ["slots.spin"] = "SPIN",
            ["slots.spinning"] = "…",
            ["slots.win"] = "WIN {0}",
            ["slots.bigwin"] = "BIG WIN",
            ["slots.jackpot"] = "JACKPOT",
            ["slots.even"] = "Your {0} stake back · no gain",
            ["slots.partBack"] = "{0} back · you're down {1} on this spin",
            ["slots.idle"] = "Line up three on the payline",
            ["slots.nowin"] = "No win this time",
            ["slots.rug"] = "Rugged. The rug never pays.",
            ["slots.pays"] = "PAYS",
            ["slots.three"] = "×3",
            ["slots.two"] = "×2",
            ["slots.wildNote"] = "WILD stands in for anything but the rug. Three WILDs: jackpot.",
            ["slots.rtp"] = "Return to player {0} · server-drawn, verifiable",

            // crash
            ["crash.title"] = "MOON OR RUG",
            ["crash.launch"] = "LAUNCH",
            ["crash.take"] = "TAKE PROFIT",
            ["crash.takeAt"] = "TAKE {0}",
            ["crash.auto"] = "Auto take-profit",
            ["crash.autoAt"] = "at {0}",
            ["crash.autoSet"] = "Auto take-profit set: the server already decided this flight",
            ["crash.rugged"] = "RUGGED",
            ["crash.ruggedAt"] = "Rugged at {0}",
            ["crash.took"] = "TOOK PROFIT AT {0}",
            ["crash.wouldRug"] = "It rugged at {0}",
            ["crash.waiting"] = "Fuelling…",
            ["crash.hint"] = "The multiplier climbs until the rug gets pulled. Take profit before it does.",
            ["crash.resume"] = "Your rocket is still flying",
            ["crash.late"] = "Too late: the server says it rugged first.",

            // coinflip
            ["flip.title"] = "COINFLIP",
            ["flip.house"] = "VS THE HOUSE",
            ["flip.heads"] = "BULL",
            ["flip.tails"] = "BEAR",
            ["flip.headsSub"] = "heads",
            ["flip.tailsSub"] = "tails",
            ["flip.flip"] = "FLIP",
            ["flip.pays"] = "Win pays {0}×",
            ["flip.won"] = "{0}! +{1}",
            ["flip.lost"] = "{0}. Not this time.",
            ["flip.h2h"] = "HEAD-TO-HEAD",
            ["flip.challenge"] = "Challenge a friend",
            ["flip.h2hNote"] = "Bragging rights only: no Cash changes hands. They get {0} s to accept.",
            ["flip.h2hWon"] = "{0}! You win the flip.",
            ["flip.h2hLost"] = "{0}. They take this one.",
            ["flip.incoming"] = "INCOMING",
            ["flip.outgoing"] = "SENT",
            ["flip.recent"] = "RECENT",
            ["flip.none"] = "No challenges yet. Challenge someone you've messaged.",
            ["flip.accept"] = "Accept",
            ["flip.decline"] = "Decline",
            ["flip.cancel"] = "Cancel",
            ["flip.vs"] = "{0} vs {1}",
            ["flip.theyChallenge"] = "@{0} · {1} · you're {2}",
            ["flip.youChallenge"] = "@{0} · {1} · you're {2}",
            ["flip.youAre"] = "you're {0}",
            ["flip.expiresIn"] = "{0}s",
            ["flip.status.settled.won"] = "You won",
            ["flip.status.settled.lost"] = "Lost",
            ["flip.status.expired"] = "Expired",
            ["flip.status.declined"] = "Declined",
            ["flip.status.cancelled"] = "Cancelled",
            ["flip.pick"] = "Who do you want to flip?",
            ["flip.pickSub"] = "You're {0}. Bragging rights only, no Cash. They get 120 s to accept.",
            ["flip.pickNone"] = "Nobody to challenge yet: send someone a DM first (Messages).",
            ["flip.send"] = "SEND CHALLENGE",
            ["flip.sent"] = "Challenge sent to @{0}",
            ["flip.toast"] = "@{0} challenged you to a coinflip",
            ["flip.expired"] = "That challenge expired.",
            ["flip.anon"] = "player",

            // fairness
            ["fair.title"] = "Provably fair",
            ["fair.how"] = "How it works",
            ["fair.step1"] = "Before a round, the server commits to a secret seed by showing its SHA-256 fingerprint (the seed hash).",
            ["fair.step2"] = "You send no seed and the server can't change it after the round starts.",
            ["fair.step3"] = "When the round settles the seed is revealed. SHA-256(seed) must equal the hash shown before.",
            ["fair.step4"] = "Every draw is HMAC-SHA256(seed, \"game:roundId:n\"); the first 53 bits make a number from 0 to 1. Slots take three (one per reel, by reel weight), Moon or Rug and coinflip take one (under 0.5 is heads).",
            ["fair.verify"] = "Verify on this phone",
            ["fair.verified"] = "Verified: the hash matches and the seed gives {0}",
            ["fair.failed"] = "Doesn't match: hash {0}, outcome {1}",
            ["fair.open"] = "Seed revealed when the round settles",
            ["fair.rounds"] = "YOUR ROUNDS",
            ["fair.none"] = "No rounds yet.",
            ["fair.hash"] = "SEED HASH (BEFORE)",
            ["fair.seed"] = "SEED (AFTER)",
            ["fair.outcome"] = "OUTCOME",
            ["fair.copy"] = "Copy",

            // limits + self-exclusion
            ["lim.title"] = "Limits and breaks",
            ["lim.stake"] = "Staked today",
            ["lim.loss"] = "Net loss today",
            ["lim.of"] = "{0} of {1}",
            ["lim.resets"] = "Resets at 00:00 UTC · in {0}",
            ["lim.session"] = "This session: {0} min",
            ["lim.exclude"] = "Take a break from the Rug Room",
            ["lim.excludeBody"] = "Lock yourself out of every casino game for a while. It can be made longer, but it can't be shortened or lifted early.",
            ["lim.days"] = "{0} days",
            ["lim.day"] = "1 day",
            ["lim.excludeBtn"] = "Lock me out for {0}",
            ["lim.confirmTitle"] = "Lock the Rug Room until {0}?",
            ["lim.confirmBody"] = "You won't be able to place any bet until then. This can't be undone or shortened.",
            ["lim.confirm"] = "Hold to confirm",
            ["lim.keep"] = "Not now",
            ["lim.done"] = "You're on a break until {0}.",

            // gates
            ["gate.ageTitle"] = "18+ only",
            ["gate.ageBody"] = "The Rug Room is for players aged 18 or over. Your account hasn't confirmed that, so the tables are closed to you.",
            ["gate.ageNote"] = "The 18+ confirmation is part of creating your character.",
            ["gate.exTitle"] = "You're on a break",
            ["gate.exBody"] = "You locked yourself out of the Rug Room until {0}. Nothing can be staked before then; it can't be lifted early.",
            ["gate.exLeft"] = "{0} to go",
            ["gate.capTitle"] = "That's today's limit",
            ["gate.capStake"] = "You've staked your daily maximum. The tables open again at the reset.",
            ["gate.capLoss"] = "You've reached today's loss limit. The tables open again at the reset.",
            ["gate.capBack"] = "Back in {0}",
            ["gate.loading"] = "Opening the Rug Room…",
            ["gate.offline"] = "The Rug Room is closed right now. Try again in a moment.",

            // session reminder
            ["rem.title"] = "Time for a break?",
            ["rem.body"] = "You've been in the Rug Room for {0} minutes.",
            ["rem.keep"] = "Keep playing",
            ["rem.leave"] = "Leave the Rug Room",
            ["rem.limits"] = "Limits and breaks",

            // errors
            ["err.AGE_REQUIRED"] = "The Rug Room is 18+ only.",
            ["err.SELF_EXCLUDED"] = "You're on a break from the Rug Room.",
            ["err.STAKE_CAP_REACHED"] = "That stake would pass today's stake limit.",
            ["err.LOSS_CAP_REACHED"] = "That stake would pass today's loss limit.",
            ["err.INSUFFICIENT_CASH"] = "Not enough Cash for that stake.",
            ["err.ROUND_IN_PROGRESS"] = "Finish your flying rocket first.",
            ["err.ROUND_SETTLED"] = "That round is already over.",
            ["err.CHALLENGE_EXPIRED"] = "That challenge expired; the stake was refunded.",
            ["err.CHALLENGE_NOT_OPEN"] = "That challenge is no longer open.",
            ["err.RATE_LIMITED"] = "Easy: one bet at a time.",
        };

        public static string T(string key) => key == "app.title" || key == "app.label" ? NameTable.Get("app.rugroom", "Casino")   // the title is the name itself
            : En.TryGetValue(key, out var v) ? NameTable.Apply(v) : key;
        public static string F(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);
        public static bool Has(string key) => En.ContainsKey(key);

        public static string Money(long cents) => CTLife.Core.Money.Cash(cents);

        /// <summary>Whole dollars without cents for chips ("$5"), cents otherwise.</summary>
        public static string Chip(long cents) => cents % 100 == 0 ? "$" + (cents / 100).ToString("#,0", CultureInfo.InvariantCulture) : Money(cents);

        public static string X(double m) => m.ToString("0.00", CultureInfo.InvariantCulture) + "×";

        public static string Countdown(double seconds)
        {
            var s = (long)Math.Max(0, Math.Ceiling(seconds));
            if (s >= 86400) return (s / 86400) + "d " + (s % 86400 / 3600) + "h";
            if (s >= 3600) return (s / 3600) + "h " + (s % 3600 / 60).ToString("00") + "m";
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        public static string Date(long epochMs) =>
            DateTimeOffset.FromUnixTimeMilliseconds(epochMs).ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

        public static string Side(string side) => side == CasinoGames.Tails ? T("flip.tails") : T("flip.heads");

        public static string GameName(string game) => game switch
        {
            CasinoGames.Slots => "Slots",
            CasinoGames.Crash => "Moon or Rug",
            CasinoGames.Coinflip => "Coinflip",
            CasinoGames.CoinflipH2H => "Head-to-head",
            _ => game,
        };

        /// <summary>A friendly line for a failed casino call: the casino codes first, then the shared City Life messages.</summary>
        public static string Error(CityError err)
        {
            if (err == null) return CityLifeStrings.T("err.generic");
            var key = "err." + err.Code;
            return En.ContainsKey(key) ? En[key] : err.Message;
        }
    }
}
