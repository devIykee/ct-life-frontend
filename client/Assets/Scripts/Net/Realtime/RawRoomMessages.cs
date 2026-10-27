using System;

namespace CTLife.Net
{
    /// <summary>
    /// Room messages the client has no generated types for yet (Phase 4-6 proposal, docs/fe-contract-proposals/phase4-6). The city and
    /// venue channels forward them here as decoded maps (`Dictionary&lt;string, object&gt;`); the feature lanes parse them
    /// (Ventures: VenturesRealtime.Dispatch, Competition: CompetitionRuntime.OnRaw). Each lane ignores types that are not its own.
    /// </summary>
    public static class RawRoomMessages
    {
        /// <summary>On the `city` room. Phase 5 (Ventures) and Phase 6 (Competition) types.</summary>
        public static readonly string[] CityTypes =
        {
            "ad_booking_update", "ads_changed", "vc_offer", "hire_offer", "project_event",
            "district_flipped", "season_rollover", "league_result", "beef_challenge", "beef_update", "beef_result", "sybil_status",
            "cabal_invite", "x_link_update",
            // Duo Shop delivery (backend PR #8): the courier leaves / the order arrived (HomeShopService, DeliveryDirector)
            "delivery_out", "delivery_arrived",
            // gm streak + quests (backend PR #9): a server event moved one of your quests (RetentionService)
            "quest_progress",
            // the real-world event calendar (backend PR #11): Breakpoint goes live / ends (EventsService)
            "event_live", "event_ended", "irl_checkin",
            // careers + Rekt Burger (backend PR #16): a ladder rank rose / a job opened for you (RektBurgerService)
            "career_promoted", "job_unlocked",
            // Sleuth heat + police (backend PR #17): a pursuit starts / ends, CT Jail (HeatService); the street room sends the
            // pursuit pair to bystanders too and the vehicles room the stolen / returned pair (both forwarded here)
            "police_pursuit", "police_pursuit_end", "jailed", "released",
            // group chats (backend #33): a message in one of my groups / a direct invite (GroupsController)
            "group_msg", "group_invite",
        };

        /// <summary>The server also broadcasts these on every `ratio_arena` venue instance (the feed drops the duplicate).</summary>
        public static readonly string[] VenueTypes = { "beef_update", "beef_result" };

        public static event Action<string, object> Received;

        internal static void Publish(string type, object payload) => Received?.Invoke(type, payload);
    }
}
