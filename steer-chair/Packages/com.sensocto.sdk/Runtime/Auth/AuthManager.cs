using System;
using UnityEngine;

namespace Sensocto.SDK
{
    /// <summary>
    /// Manages authentication tokens for Sensocto connections.
    /// Stores tokens in PlayerPrefs for persistence across sessions.
    /// </summary>
    public static class AuthManager
    {
        private const string TOKEN_KEY = "sensocto_bearer_token";
        private const string USER_KEY = "sensocto_user_name";
        private const string USER_ID_KEY = "sensocto_user_id";
        private const string SERVER_KEY = "sensocto_server_url";

        private static string _cachedToken;
        private static string _cachedUser;
        private static string _cachedUserId;
        private static string _cachedServer;

        /// <summary>
        /// Event fired when authentication changes (login/logout).
        /// </summary>
        public static event Action OnAuthChanged;

        /// <summary>
        /// Whether a valid token is stored.
        /// </summary>
        public static bool IsAuthenticated => !string.IsNullOrEmpty(GetToken());

        /// <summary>
        /// Current authenticated user name.
        /// </summary>
        public static string UserName => GetUserName();

        /// <summary>
        /// Current authenticated user ID.
        /// </summary>
        public static string UserId => GetUserId();

        /// <summary>
        /// Get the stored bearer token.
        /// </summary>
        public static string GetToken()
        {
            if (_cachedToken == null)
            {
                _cachedToken = PlayerPrefs.GetString(TOKEN_KEY, "");
            }
            return _cachedToken;
        }

        /// <summary>
        /// Get the stored user name.
        /// </summary>
        public static string GetUserName()
        {
            if (_cachedUser == null)
            {
                _cachedUser = PlayerPrefs.GetString(USER_KEY, "");
            }
            return _cachedUser;
        }

        /// <summary>
        /// Get the stored user ID.
        /// </summary>
        public static string GetUserId()
        {
            if (_cachedUserId == null)
            {
                _cachedUserId = PlayerPrefs.GetString(USER_ID_KEY, "");
            }
            return _cachedUserId;
        }

        /// <summary>
        /// Get the stored server URL (if overridden via deep link).
        /// </summary>
        public static string GetServerUrl()
        {
            if (_cachedServer == null)
            {
                _cachedServer = PlayerPrefs.GetString(SERVER_KEY, "");
            }
            return _cachedServer;
        }

        /// <summary>
        /// Store authentication credentials from a deep link or login.
        /// </summary>
        /// <param name="token">Bearer token for API authentication.</param>
        /// <param name="userName">Display name of the user.</param>
        /// <param name="userId">Unique user ID.</param>
        /// <param name="serverUrl">Optional server URL override.</param>
        public static void SetCredentials(string token, string userName = null, string userId = null, string serverUrl = null)
        {
            _cachedToken = token ?? "";
            _cachedUser = userName ?? "";
            _cachedUserId = userId ?? "";
            _cachedServer = serverUrl ?? "";

            PlayerPrefs.SetString(TOKEN_KEY, _cachedToken);
            PlayerPrefs.SetString(USER_KEY, _cachedUser);
            PlayerPrefs.SetString(USER_ID_KEY, _cachedUserId);
            PlayerPrefs.SetString(SERVER_KEY, _cachedServer);
            PlayerPrefs.Save();

            Debug.Log($"[AuthManager] Credentials stored for user: {userName ?? "(anonymous)"}");
            OnAuthChanged?.Invoke();
        }

        /// <summary>
        /// Clear all stored credentials (logout).
        /// </summary>
        public static void ClearCredentials()
        {
            _cachedToken = "";
            _cachedUser = "";
            _cachedUserId = "";
            _cachedServer = "";

            PlayerPrefs.DeleteKey(TOKEN_KEY);
            PlayerPrefs.DeleteKey(USER_KEY);
            PlayerPrefs.DeleteKey(USER_ID_KEY);
            PlayerPrefs.DeleteKey(SERVER_KEY);
            PlayerPrefs.Save();

            Debug.Log("[AuthManager] Credentials cleared");
            OnAuthChanged?.Invoke();
        }

        /// <summary>
        /// Check if we have a token and it's not empty.
        /// </summary>
        public static bool HasValidToken()
        {
            var token = GetToken();
            return !string.IsNullOrEmpty(token) && token.Length > 10;
        }
    }
}
