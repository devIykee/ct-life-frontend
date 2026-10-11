using System;
using UnityEngine;

namespace CTLife.Core
{
    /// <summary>
    /// Auth tokens and the signed-in identity (contract section 3 step 7). The access token lives in memory only;
    /// the refresh token is persisted per environment so the app can resume without a new wallet prompt.
    /// [Inference] PlayerPrefs is plain shared-preferences storage on Android; move the refresh token to the
    /// Android Keystore before prod.
    /// </summary>
    public sealed class Session
    {
        public string AccessToken { get; private set; }
        public string RefreshToken { get; private set; }
        public DateTime AccessExpiresUtc { get; private set; }
        public string UserId { get; private set; }
        public string Wallet { get; private set; }
        public bool IsSeeker { get; private set; }
        public bool HasCharacter { get; set; }
        public string AuthMethod { get; private set; } = "none";

        public bool IsSignedIn => !string.IsNullOrEmpty(AccessToken);

        /// <summary>Refresh this long before the JWT `exp` (contract section 3: access tokens live 15 minutes).</summary>
        public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(90);

        public event Action Changed;

        private static string Key(string k) => $"ctlife.{Env.Current.id}.{k}";

        public void SetTokens(string access, string refresh, string userId, string wallet, bool isSeeker, bool hasCharacter, string method)
        {
            AccessToken = access;
            RefreshToken = refresh;
            UserId = userId;
            Wallet = wallet;
            IsSeeker = isSeeker;
            HasCharacter = hasCharacter;
            AuthMethod = method;
            AccessExpiresUtc = Jwt.RefreshAtUtc(access, DateTime.UtcNow, RefreshMargin);
            PlayerPrefs.SetString(Key("refresh"), refresh ?? "");
            PlayerPrefs.SetString(Key("wallet"), wallet ?? "");
            PlayerPrefs.SetString(Key("method"), method ?? "");
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public void UpdateTokens(string access, string refresh)
        {
            AccessToken = access;
            RefreshToken = refresh;
            AccessExpiresUtc = Jwt.RefreshAtUtc(access, DateTime.UtcNow, RefreshMargin);
            PlayerPrefs.SetString(Key("refresh"), refresh ?? "");
            PlayerPrefs.Save();
        }

        public string StoredRefreshToken => PlayerPrefs.GetString(Key("refresh"), "");
        public string StoredWallet => PlayerPrefs.GetString(Key("wallet"), "");
        public string StoredMethod => PlayerPrefs.GetString(Key("method"), "");

        public bool AccessTokenStale => !IsSignedIn || DateTime.UtcNow >= AccessExpiresUtc;

        public void Clear()
        {
            AccessToken = null;
            RefreshToken = null;
            UserId = null;
            Wallet = null;
            IsSeeker = false;
            HasCharacter = false;
            AuthMethod = "none";
            PlayerPrefs.DeleteKey(Key("refresh"));
            PlayerPrefs.DeleteKey(Key("method"));
            PlayerPrefs.DeleteKey(Key("wallet"));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
