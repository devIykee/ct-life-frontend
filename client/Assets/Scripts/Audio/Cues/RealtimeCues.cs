using CTLife.Core;
using CTLife.Realtime;
using UnityEngine;
using UnityEngine.Scripting;

// Nothing references this assembly; keep it (and its RuntimeInitializeOnLoadMethod) through managed code stripping.
[assembly: AlwaysLinkAssembly]

namespace CTLife.Audio
{
    /// <summary>
    /// Bus subscriptions for realtime messages that Net already publishes (Channels.cs), so these cues fire without
    /// any other lane calling audio:
    /// <list type="bullet">
    /// <item><c>dm</c> → <c>sfx_dm_ping</c></item>
    /// <item><c>call_invite</c> → <c>sfx_call_ring</c> keep-alive loop (re-armed by each ring signal; stops on
    /// <c>sfx_call_connect</c> / <c>sfx_call_end</c>, a kick, or one cycle after the last ring)</item>
    /// <item>market <c>tape</c> print ≥ 10× tapeThresholdCents → <c>sfx_whale_horn</c> (20 s cooldown)</item>
    /// <item>venue <c>said</c>: a "gm" line → <c>sfx_gm</c> (the CT morning ritual, 12 s cooldown); someone else's
    /// <c>@yourhandle</c> (PlayerProfile alias, or Sfx.SetLocalHandle) → <c>sfx_mention</c></item>
    /// <item>city <c>payment_confirmed</c> → <c>sfx_cash_in</c> (masks the toast tick)</item>
    /// </list>
    /// City <c>event</c>s and <c>liquidated</c> are voiced by the UI cards that show them (IncidentPresentation, RektCard);
    /// CueGate cooldowns make a duplicate from another path a single play.
    /// </summary>
    public static class RealtimeCues
    {
        private static Subscriptions _subs;

        public static bool Installed => _subs != null;

        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Install()
        {
            _subs?.Dispose();
            _subs = new Subscriptions();
            _subs.On<DmMessage>(OnDm);
            _subs.On<CallInviteMessage>(OnCallInvite);
            _subs.On<KickedMessage>(_ => Sfx.StopLoop(SfxIds.CallRing));
            _subs.On<TapeMessage>(OnTape);
            _subs.On<SaidMessage>(OnSaid);
            _subs.On<PaymentConfirmedMessage>(_ => Sfx.Play(SfxIds.CashIn));
        }

        public static void Uninstall()
        {
            _subs?.Dispose();
            _subs = null;
        }

        private static string LocalUserId => Services.TryGet<Session>(out var s) ? s.UserId : null;

        private static void OnDm(DmMessage m)
        {
            if (m == null || (m.fromUserId != null && m.fromUserId == LocalUserId)) return;
            Sfx.Play(SfxIds.DmPing);
        }

        private static void OnCallInvite(CallInviteMessage m)
        {
            if (m == null || (m.fromUserId != null && m.fromUserId == LocalUserId)) return;
            // Keep-alive ring: the call owner (Social CallController) re-publishes sfx_call_ring every 3 s while the
            // invite is pending and publishes sfx_call_connect / sfx_call_end, which stop it at once.
            Sfx.Play(SfxIds.CallRing);
        }

        private static void OnTape(TapeMessage m)
        {
            if (m == null) return;
            long threshold = CueRules.DefaultTapeThresholdCents;
            try
            {
                if (Services.TryGet<ConfigStore>(out var cfg))
                    threshold = cfg.Config("market").Long("tapeThresholdCents", CueRules.DefaultTapeThresholdCents);
            }
            catch (System.Exception) { threshold = CueRules.DefaultTapeThresholdCents; } // malformed config: keep the rc1 value
            var cue = CueRules.ForTape(m.side, m.cashCents, threshold);
            if (cue != null) Sfx.Play(cue);
        }

        private static void OnSaid(SaidMessage m)
        {
            if (m == null || string.IsNullOrEmpty(m.text)) return;
            bool mine = m.userId != null && m.userId == LocalUserId;
            if (CueRules.IsGm(m.text)) { Sfx.Play(SfxIds.Gm, mine ? 0.8f : 0.6f); return; }
            var handle = AudioManager.Instance != null ? AudioManager.Instance.LocalHandle : null;
            if (string.IsNullOrEmpty(handle) && Services.TryGet<CTLife.Net.PlayerProfile>(out var profile) && profile.Me != null)
                handle = profile.Alias;   // the character alias is the in-game @handle
            if (!mine && CueRules.MentionsHandle(m.text, handle)) Sfx.Play(SfxIds.Mention);
        }
    }
}
