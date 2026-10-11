using System.Collections.Generic;
using System.Linq;
using CTLife.Realtime;
using CTLife.World;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>Player-chosen interactions: classification, the state machine, availability and the wire payloads.</summary>
    public class InteractionTests
    {
        private static Interactable Seat(string id = "table_1", string wire = "terminal_cafe.table_1", InteractionKind kind = InteractionKind.Seat,
            Vector3? at = null) =>
            new Interactable
            {
                id = id, wireId = wire, kind = kind, anim = "act_sit", position = at ?? Vector3.zero,
                approach = (at ?? Vector3.zero) + Vector3.forward * 0.6f, yaw = 180f, range = 1.9f,
            };

        private static InteractionCommandKind[] Kinds(List<InteractionCommand> c) => c.Select(x => x.kind).ToArray();

        // ------------------------------------------------------------------ classification

        [TestCase("table_1", "seat", "act_sit", InteractionKind.Seat)]
        [TestCase("bench_3", "seat", "act_sit", InteractionKind.Bench)]
        [TestCase("sofa_left", "seat", "act_sit", InteractionKind.Sofa)]
        [TestCase("terminal_4", "terminal", "act_type", InteractionKind.Terminal)]
        [TestCase("stage_2", "stage", "act_sit", InteractionKind.StageSeat)]
        [TestCase("stage_host", "stage", "loco_idle", InteractionKind.Mic)]
        [TestCase("exit", "exit", "", InteractionKind.Door)]
        [TestCase("counter", "npc", "", InteractionKind.Counter)]
        public void ClassifiesClaimableSpots(string id, string kind, string anim, InteractionKind expected) =>
            Assert.AreEqual(expected, InteractableCatalog.Classify(id, kind, anim));

        [TestCase("center", "stand", "")]
        [TestCase("entrance", "exit", "")]
        [TestCase("seat_9", "seat", "")]     // not claimable (no anim) → not offered
        [TestCase("crier_1", "standing", "")]
        [TestCase("pulse_screen", "stand", "")]
        public void IgnoresNonInteractiveSpots(string id, string kind, string anim) =>
            Assert.IsNull(InteractableCatalog.Classify(id, kind, anim));

        [Test]
        public void EngagedPresentationPerKind()
        {
            Assert.AreEqual("act_type", InteractableCatalog.EngagedAnim(InteractionKind.Terminal, "act_type"));
            Assert.AreEqual("act_host_mic", InteractableCatalog.EngagedAnim(InteractionKind.Mic, "loco_idle"));
            Assert.AreEqual("idle_lean_rail", InteractableCatalog.EngagedAnim(InteractionKind.Counter, null));
            Assert.AreEqual("act_sit", InteractableCatalog.EngagedAnim(InteractionKind.Bench, ""));
        }

        // ------------------------------------------------------------------ state machine

        [Test]
        public void NothingHappensUntilThePlayerChooses()
        {
            var m = new InteractionMachine();
            Assert.AreEqual(InteractionPhase.Idle, m.Phase);
            Assert.IsEmpty(m.Tick(100));
            Assert.IsEmpty(m.Arrived(1));
            Assert.IsEmpty(m.ServerSpot("", null));
            Assert.AreEqual(InteractionPhase.Idle, m.Phase, "no automatic sit");
        }

        [Test]
        public void NetworkedSeat_ApproachClaimConfirmStand()
        {
            var m = new InteractionMachine();
            var seat = Seat();
            CollectionAssert.AreEqual(new[] { InteractionCommandKind.WalkTo }, Kinds(m.Use(seat, online: true, now: 0)));
            Assert.AreEqual(InteractionPhase.Approaching, m.Phase);
            var claim = m.Arrived(1);
            Assert.AreEqual(InteractionCommandKind.RequestSeat, claim.Single().kind);
            Assert.AreEqual("terminal_cafe.table_1", claim.Single().arg, "the wire id is what `sit` carries");
            Assert.AreEqual(InteractionPhase.Requesting, m.Phase);
            m.ServerSpot("terminal_cafe.table_1", id => id.EndsWith("table_1") ? seat : null);
            Assert.AreEqual(InteractionPhase.Engaged, m.Phase);
            Assert.IsFalse(m.LocalOnly);
            Assert.AreEqual("stand", m.Actions(null).Single(a => a.primary).id);
            CollectionAssert.AreEqual(new[] { InteractionCommandKind.ReleaseSeat }, Kinds(m.StandUp()));
            Assert.AreEqual(InteractionPhase.Idle, m.Phase);
        }

        [Test]
        public void Offline_EngagesLocallyAndNeverSends()
        {
            var m = new InteractionMachine();
            m.Use(Seat(), online: false, now: 0);
            Assert.IsTrue(m.LocalOnly);
            CollectionAssert.AreEqual(new[] { InteractionCommandKind.EngageLocal }, Kinds(m.Arrived(1)));
            Assert.AreEqual(InteractionPhase.Engaged, m.Phase);
            CollectionAssert.AreEqual(new[] { InteractionCommandKind.Disengage }, Kinds(m.StandUp()));
        }

        [Test]
        public void LocalOnlyKind_CounterEngagesLocallyEvenOnline()
        {
            var m = new InteractionMachine();
            var counter = new Interactable { id = "counter", kind = InteractionKind.Counter, anim = "idle_lean_rail", range = 2.2f };
            m.Use(counter, online: true, now: 0);
            Assert.IsTrue(m.LocalOnly);
            Assert.AreEqual(InteractionCommandKind.EngageLocal, m.Arrived(1).Single().kind);
            Assert.AreEqual("interact.stepAway", m.Actions(null).Single().labelKey);
        }

        [Test]
        public void Door_OpensTheMapWithoutWalking()
        {
            var m = new InteractionMachine();
            var door = new Interactable { id = "exit", kind = InteractionKind.Door };
            CollectionAssert.AreEqual(new[] { InteractionCommandKind.OpenMap }, Kinds(m.Use(door, true, 0)));
            Assert.AreEqual(InteractionPhase.Idle, m.Phase);
        }

        [Test]
        public void Rejection_ReturnsToIdle()
        {
            var m = new InteractionMachine();
            m.Use(Seat(), true, 0); m.Arrived(1);
            Assert.IsEmpty(m.Rejected());
            Assert.AreEqual(InteractionPhase.Idle, m.Phase);
            Assert.IsNull(m.Target);
        }

        [Test]
        public void NoAnswer_TimesOutReleasesAndTellsThePlayer()
        {
            var m = new InteractionMachine();
            m.Use(Seat(), true, 0); m.Arrived(10);
            Assert.IsEmpty(m.Tick(10 + InteractionMachine.RequestTimeoutSeconds - 0.1));
            var cmds = m.Tick(10 + InteractionMachine.RequestTimeoutSeconds + 0.1);
            CollectionAssert.AreEqual(new[] { InteractionCommandKind.ReleaseSeat, InteractionCommandKind.Toast }, Kinds(cmds));
            Assert.AreEqual("interact.noResponse", cmds[1].arg);
            Assert.AreEqual(InteractionPhase.Idle, m.Phase);
        }

        [Test]
        public void TakingControl_CancelsApproachAndPendingClaim()
        {
            var m = new InteractionMachine();
            m.Use(Seat(), true, 0);
            CollectionAssert.AreEqual(new[] { InteractionCommandKind.CancelWalk }, Kinds(m.PlayerMoved()));
            Assert.AreEqual(InteractionPhase.Idle, m.Phase);
            m.Use(Seat(), true, 0); m.Arrived(1);
            CollectionAssert.AreEqual(new[] { InteractionCommandKind.ReleaseSeat, InteractionCommandKind.CancelWalk }, Kinds(m.PlayerMoved()));
        }

        [Test]
        public void LateEmptyEcho_DoesNotCancelAPendingClaim_ButAServerStandEndsTheSeat()
        {
            var m = new InteractionMachine();
            var seat = Seat();
            m.Use(seat, true, 0); m.Arrived(1);
            m.ServerSpot("", id => seat);
            Assert.AreEqual(InteractionPhase.Requesting, m.Phase);
            m.ServerSpot(seat.wireId, id => seat);
            m.ServerSpot("", id => seat);
            Assert.AreEqual(InteractionPhase.Idle, m.Phase);
        }

        [Test]
        public void ServerSeatOnRejoin_FollowsTheServer()
        {
            var m = new InteractionMachine();
            var seat = Seat("bench_2", "timeline_square.bench_2", InteractionKind.Bench);
            m.ServerSpot("timeline_square.bench_2", id => seat);
            Assert.AreEqual(InteractionPhase.Engaged, m.Phase);
            Assert.AreSame(seat, m.Target);
        }

        [Test]
        public void BusyMachine_IgnoresAnotherUse()
        {
            var m = new InteractionMachine();
            m.Use(Seat("a"), true, 0);
            Assert.IsEmpty(m.Use(Seat("b"), true, 0));
            Assert.AreEqual("a", m.Target.id);
        }

        [Test]
        public void Stood_EndsAnyEngagement()
        {
            var m = new InteractionMachine();
            m.Use(Seat(), false, 0); m.Arrived(1);
            m.Stood();
            Assert.AreEqual(InteractionPhase.Idle, m.Phase);
        }

        [Test]
        public void Actions_OfferTheVerbThenStandAndMicExtras()
        {
            var m = new InteractionMachine();
            var bench = Seat("bench_1", "timeline_square.bench_1", InteractionKind.Bench);
            var use = m.Actions(bench).Single();
            Assert.IsTrue(use.primary);
            Assert.AreEqual("use", use.id);
            Assert.AreEqual("interact.sitBench", use.labelKey);
            Assert.IsEmpty(m.Actions(null), "nothing near: no CTA");
            var mic = new Interactable { id = "stage_host", wireId = "spaces_studio.stage_host", kind = InteractionKind.Mic, anim = "act_host_mic" };
            m.Use(mic, true, 0); m.Arrived(1); m.ServerSpot(mic.wireId, _ => mic);
            var ids = m.Actions(null).Select(a => a.id).ToArray();
            CollectionAssert.AreEqual(new[] { "stand", "emote:emote_wave", "emote:emote_wagmi" }, ids);
            var emote = m.Secondary("emote:emote_wave").Single();
            Assert.AreEqual(InteractionCommandKind.Emote, emote.kind);
            Assert.AreEqual("emote_wave", emote.arg);
        }

        // ------------------------------------------------------------------ availability

        [Test]
        public void Occupancy_PlayersAndNpcsHoldSeats_KeysIgnoreTheVenuePrefix()
        {
            var o = new SeatOccupancy("terminal_cafe");
            var seat = Seat();
            Assert.IsTrue(o.IsFree(seat, "me"));
            o.Set("other-session", "terminal_cafe.table_1");
            Assert.IsFalse(o.IsFree(seat, "me"));
            Assert.IsTrue(o.IsFree(seat, "other-session"), "my own seat is free for me");
            o.Set("other-session", null);
            Assert.IsTrue(o.IsFree(seat, "me"));
            o.Set("npc:barista", "table_1");
            Assert.AreEqual("npc:barista", o.OccupantOf("terminal_cafe.table_1"));
            o.Set("npc:barista", "table_2"); // moving frees the old seat
            Assert.IsTrue(o.IsFree(seat, "me"));
            Assert.IsTrue(o.IsFree(new Interactable { id = "exit", kind = InteractionKind.Door }, "me"), "doors are never exclusive");
        }

        [Test]
        public void Finder_NearestInRange_FacingAndHysteresis()
        {
            var a = Seat("a", "v.a", at: new Vector3(1.0f, 0, 0));
            var b = Seat("b", "v.b", at: new Vector3(-1.2f, 0, 0));
            var far = Seat("far", "v.far", at: new Vector3(10, 0, 0));
            var all = new List<Interactable> { a, b, far };
            bool Free(Interactable _) => true;
            Assert.AreSame(a, InteractionFinder.Best(Vector3.zero, Vector3.right, all, Free, null));
            // Facing b makes it preferable despite being slightly farther.
            Assert.AreSame(b, InteractionFinder.Best(Vector3.zero, Vector3.left, all, Free, null));
            // Hysteresis: a stays offered a little beyond its range while it is the current target.
            var player = new Vector3(1.0f + a.range * 1.1f, 0, 0);
            Assert.IsNull(InteractionFinder.Best(player, Vector3.left, new List<Interactable> { a }, Free, null));
            Assert.AreSame(a, InteractionFinder.Best(player, Vector3.left, new List<Interactable> { a }, Free, a));
            // Occupied seats are never offered; a tapped seat is offered from farther away.
            Assert.IsNull(InteractionFinder.Best(Vector3.zero, Vector3.right, new List<Interactable> { a }, _ => false, null));
            Assert.AreSame(far, InteractionFinder.Best(Vector3.zero, Vector3.right, all, Free, null, focused: far));
        }

        // ------------------------------------------------------------------ wire payloads

        [Test]
        public void Payloads_MatchTheContract()
        {
            var sit = InteractionNet.Sit(Seat());
            Assert.AreEqual("{\"spotId\":\"terminal_cafe.table_1\"}", InteractionNet.Json(sit));
            Assert.IsTrue(InteractionNet.Valid(sit, id => id == "terminal_cafe.table_1"));
            Assert.IsFalse(InteractionNet.Valid(sit, id => false), "only registry spots are claimable");
            Assert.AreEqual("{}", InteractionNet.Json(InteractionNet.Stand()));
            Assert.IsNull(InteractionNet.Sit(new Interactable { id = "counter", kind = InteractionKind.Counter }), "local-only kinds send nothing");
            Assert.IsFalse(InteractionNet.Valid(new SitMessage { spotId = "" }, null), "contract: spotId min length 1");
        }

        // ------------------------------------------------------------------ speed-matched locomotion

        [Test]
        public void LocoRate_KeepsTheWalkAndMatchesTheRun()
        {
            // Below the walk threshold: identical to the accepted walk (speed / 1.0 m/s).
            Assert.AreEqual(0.5f, LocomotionTuning.Rate(0.5f, 1f, 4.5f), 1e-4f);
            Assert.AreEqual(2.2f, LocomotionTuning.Rate(2.2f, 1f, 4.5f), 1e-4f);
            // In the run band: speed / run ground speed (feet move with the body).
            Assert.AreEqual(1f, LocomotionTuning.Rate(4.5f, 1f, 4.5f), 1e-4f);
            Assert.AreEqual(3.6f / 4.5f, LocomotionTuning.Rate(3.6f, 1f, 4.5f), 1e-4f);
            // Continuous across the crossover, and stopped = 1.
            float prev = LocomotionTuning.Rate(2.2f, 1f, 4.5f);
            for (float v = 2.25f; v <= 3.6f; v += 0.05f)
            {
                float r = LocomotionTuning.Rate(v, 1f, 4.5f);
                Assert.Less(Mathf.Abs(r - prev), 0.25f, $"jump at {v}");
                prev = r;
            }
            Assert.AreEqual(1f, LocomotionTuning.Rate(0f, 1f, 4.5f));
        }
    }
}
