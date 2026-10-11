using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Rest.Model;
using Newtonsoft.Json.Linq;

namespace CTLife.Net
{
    /// <summary>
    /// Sign In With Solana through MWA (contract section 3), plus the mock/local dev bypass `POST /v1/auth/dev`.
    /// The wallet itself is behind <see cref="IWalletSignIn"/> (CTLife.Solana) so the SDK can be swapped for the
    /// Kotlin MWA plugin fallback without touching this class.
    /// </summary>
    public sealed class AuthService : IAuthService
    {
        private readonly IRestClient _rest;
        private readonly Session _session;
        private Task<bool> _refreshing;

        public AuthService(IRestClient rest, Session session)
        {
            _rest = rest;
            _session = session;
            _rest.RefreshHandler = Refresh;
        }

        public bool DevAuthAllowed => Env.Current.allowDevAuth;

        /// <summary>Steps 1–7: challenge → MWA authorize with sign_in_payload → verify → tokens.</summary>
        public async Task<AuthVerifyResponse> SignInWithWallet(IWalletSignIn wallet, CancellationToken ct = default)
        {
            if (wallet == null || !wallet.IsAvailable)
                throw new WalletException("Mobile Wallet Adapter is not available on this device.");

            // Raw parse: the wallet must sign the exact strings the server issued.
            var challenge = await _rest.Send<JToken>("GET", "/auth/challenge", null, auth: false, ct: ct);
            var req = new SiwsRequest
            {
                domain = challenge.Str("domain"),
                statement = challenge.Str("statement"),
                uri = challenge.Str("uri"),
                version = challenge.Str("version", "1"),
                chainId = challenge.Str("chainId"),
                nonce = challenge.Str("nonce"),
                issuedAt = challenge.Str("issuedAt"),
                expirationTime = challenge.Str("expirationTime"),
            };
            Log.Info("auth", $"SIWS challenge for {req.domain} on {req.chainId}");

            var signed = await wallet.SignIn(req, ct);
            if (signed == null || string.IsNullOrEmpty(signed.address))
                throw new WalletException("The wallet returned no sign-in result.");

            var body = new JObject
            {
                ["address"] = signed.address,
                ["signedMessage"] = signed.signedMessageBase64,
                ["signature"] = signed.signatureBase64,
                ["nonce"] = req.nonce,
            };
            var session = await _rest.Post<AuthVerifyResponse>("/auth/verify", body.ToString(), auth: false, ct: ct);
            Apply(session, "siws");
            return session;
        }

        /// <summary>Dev bypass. Compiled out of staging/prod servers; the client also refuses unless the env allows it.</summary>
        public async Task<AuthVerifyResponse> SignInDev(string wallet, bool isSeeker = false, CancellationToken ct = default)
        {
            if (!DevAuthAllowed) throw new InvalidOperationException("Dev auth is disabled for " + Env.Current.id);
            // Contract 1.0.0-rc1: body is {wallet} only; isSeeker is decided server-side.
            var body = new JObject { ["wallet"] = wallet };
            var session = await _rest.Post<AuthVerifyResponse>("/auth/dev", body.ToString(), auth: false, ct: ct);
            Apply(session, "dev");
            return session;
        }

        /// <summary>Resumes with the stored refresh token. Returns false if there is none or it was revoked.</summary>
        public async Task<bool> TryResume(CancellationToken ct = default)
        {
            var stored = _session.StoredRefreshToken;
            if (string.IsNullOrEmpty(stored)) return false;
            try
            {
                var body = new JObject { ["refreshToken"] = stored };
                var s = await _rest.Post<AuthVerifyResponse>("/auth/refresh", body.ToString(), auth: false, ct: ct);
                Apply(s, _session.StoredMethod);
                return true;
            }
            catch (ApiException e) when (!e.IsNetwork)
            {
                Log.Info("auth", "Stored session rejected: " + e.Code);
                _session.Clear();
                return false;
            }
        }

        /// <summary>Step 9. Concurrent callers share one refresh.</summary>
        public Task<bool> Refresh()
        {
            if (_refreshing != null && !_refreshing.IsCompleted) return _refreshing;
            _refreshing = DoRefresh();
            return _refreshing;
        }

        private async Task<bool> DoRefresh()
        {
            var token = !string.IsNullOrEmpty(_session.RefreshToken) ? _session.RefreshToken : _session.StoredRefreshToken;
            if (string.IsNullOrEmpty(token)) return false;
            try
            {
                var body = new JObject { ["refreshToken"] = token };
                var s = await _rest.SendRaw("POST", "/auth/refresh", body.ToString(), false, Ids.NewIdempotencyKey(), null, CancellationToken.None);
                var parsed = Json.Deserialize<AuthVerifyResponse>(s.body);
                _session.UpdateTokens(parsed.AccessToken, parsed.RefreshToken);
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("auth", "Refresh failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Step 10: revoke the refresh token, then MWA deauthorize.</summary>
        public async Task SignOut(IWalletSignIn wallet)
        {
            try
            {
                if (!string.IsNullOrEmpty(_session.RefreshToken))
                    await _rest.Post<NoContent>("/auth/logout", new JObject { ["refreshToken"] = _session.RefreshToken }.ToString());
            }
            catch (Exception e)
            {
                Log.Warn("auth", "Logout call failed: " + e.Message);
            }
            if (wallet != null && _session.AuthMethod == "siws")
            {
                try { await wallet.SignOut(); } catch (Exception e) { Log.Warn("auth", "Deauthorize failed: " + e.Message); }
            }
            _session.Clear();
        }

        private void Apply(AuthVerifyResponse s, string method)
        {
            var u = s.User;
            _session.SetTokens(s.AccessToken, s.RefreshToken, u?.Id.ToString(), u?.Wallet, u != null && u.IsSeeker,
                u != null && u.HasCharacter, method);
            Log.Info("auth", $"Signed in ({method}) as {u?.Wallet} seeker={u?.IsSeeker} hasCharacter={u?.HasCharacter}");
        }
    }
}
