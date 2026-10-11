using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
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
        public async Task<AuthResult> SignInWithWallet(IWalletSignIn wallet, CancellationToken ct = default)
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
            var expected = Env.Current.SiwsDomain;
            if (!string.Equals(req.domain, expected, StringComparison.OrdinalIgnoreCase))
                Log.Warn("auth", $"SIWS domain mismatch: the server issued '{req.domain}', the app identity is '{expected}' (EnvConfig.AppIdentityUri). " +
                                 "Set the server's SIWS_DOMAIN/SIWS_URI to the identity; the wallet shows the identity, the server checks its own domain.");

            var signed = await wallet.SignIn(req, ct);
            if (signed == null || string.IsNullOrEmpty(signed.address))
                throw new WalletException("The wallet returned no sign-in result.");
            Log.Info("auth", $"Wallet signed SIWS for {signed.address} ({signed.signatureType})");

            // Contract: signedMessage is base64 (backend 7243073 decodes base64 and checks every SIWS field).
            var response = await Verify(signed, signed.signedMessageBase64, req.nonce, ct);
            var session = AuthResult.Parse(response, knownWallet: signed.address);
            Apply(session, "siws");
            return session;
        }

        private Task<JToken> Verify(SiwsResponse signed, string signedMessage, string nonce, CancellationToken ct)
        {
            var body = new JObject
            {
                ["address"] = signed.address,
                ["signedMessage"] = signedMessage,
                ["signature"] = signed.signatureBase64,
                ["nonce"] = nonce,
            };
            return _rest.Post<JToken>("/auth/verify", body.ToString(), auth: false, ct: ct);
        }

        /// <summary>Dev bypass. Compiled out of staging/prod servers; the client also refuses unless the env allows it.</summary>
        public async Task<AuthResult> SignInDev(string wallet, bool isSeeker = false, CancellationToken ct = default)
        {
            if (!DevAuthAllowed) throw new InvalidOperationException("Dev auth is disabled for " + Env.Current.id);
            // Contract 1.0.0-rc1: body is {wallet} only; isSeeker is decided server-side.
            var body = new JObject { ["wallet"] = wallet };
            var response = await _rest.Post<JToken>("/auth/dev", body.ToString(), auth: false, ct: ct);
            var session = AuthResult.Parse(response, knownWallet: wallet);
            Apply(session, "dev");
            return session;
        }

        /// <summary>
        /// Resumes with the stored refresh token. Returns false if there is none or the server rejected it.
        /// Network failures propagate so the caller can offer a retry instead of a fresh wallet prompt.
        /// </summary>
        public async Task<bool> TryResume(CancellationToken ct = default)
        {
            var stored = _session.StoredRefreshToken;
            if (string.IsNullOrEmpty(stored)) return false;
            try
            {
                var body = new JObject { ["refreshToken"] = stored };
                var response = await _rest.Post<JToken>("/auth/refresh", body.ToString(), auth: false, ct: ct);
                var s = AuthResult.Parse(response, knownWallet: _session.StoredWallet);
                Apply(s, _session.StoredMethod);
                return true;
            }
            catch (ApiException e) when (!e.IsNetwork && e.Status >= 400 && e.Status < 500)
            {
                Log.Info("auth", "Stored session rejected: " + e.Code);
                _session.Clear();
                return false;
            }
        }

        /// <summary>Refreshes when the access token is within its refresh margin. Rooms call this before joining.</summary>
        public async Task EnsureFresh(CancellationToken ct = default)
        {
            if (!_session.IsSignedIn || !_session.AccessTokenStale) return;
            if (string.IsNullOrEmpty(_session.RefreshToken) && string.IsNullOrEmpty(_session.StoredRefreshToken)) return;
            await Refresh();
            ct.ThrowIfCancellationRequested();
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
                var parsed = AuthResult.Parse(Json.ParseRaw(s.body), _session.UserId, _session.Wallet, _session.IsSeeker);
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

        private void Apply(AuthResult s, string method)
        {
            _session.SetTokens(s.AccessToken, s.RefreshToken, s.UserId, s.Wallet, s.IsSeeker, s.HasCharacter ?? false, method);
            Log.Info("auth", $"Signed in ({method}) as {s.Wallet} user={s.UserId} seeker={s.IsSeeker} hasCharacter={s.HasCharacter}");
        }
    }
}
