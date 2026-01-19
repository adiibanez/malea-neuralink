using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sensocto.SDK
{
    /// <summary>
    /// Handles deep link URLs for authentication.
    ///
    /// URL Format: sensocto://auth?token=xxx&user=Alice&user_id=123&server=wss://...
    ///
    /// Supported parameters:
    /// - token: Bearer token for authentication (required)
    /// - user: User display name (optional)
    /// - user_id: User unique ID (optional)
    /// - server: Server URL override (optional)
    /// - scene: Scene to load after auth (optional)
    /// </summary>
    public class DeepLinkHandler : MonoBehaviour
    {
        private static DeepLinkHandler _instance;

        [Header("Settings")]
        [SerializeField] private string defaultScene = "";
        [SerializeField] private bool autoLoadScene = false;

        [Header("Debug")]
        [SerializeField] private bool logDeepLinks = true;
        [SerializeField] private string testDeepLink = "";

        /// <summary>
        /// Event fired when a deep link is successfully processed.
        /// </summary>
        public static event Action<DeepLinkData> OnDeepLinkReceived;

        /// <summary>
        /// Parsed deep link data.
        /// </summary>
        public class DeepLinkData
        {
            public string Token { get; set; }
            public string UserName { get; set; }
            public string UserId { get; set; }
            public string ServerUrl { get; set; }
            public string Scene { get; set; }
            public Dictionary<string, string> ExtraParams { get; set; } = new Dictionary<string, string>();
        }

        private void Awake()
        {
            // Singleton pattern - persist across scenes
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Subscribe to deep link events
            Application.deepLinkActivated += OnDeepLinkActivated;

            // Check if app was launched with a deep link
            if (!string.IsNullOrEmpty(Application.absoluteURL))
            {
                OnDeepLinkActivated(Application.absoluteURL);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                Application.deepLinkActivated -= OnDeepLinkActivated;
                _instance = null;
            }
        }

        private void OnDeepLinkActivated(string url)
        {
            if (logDeepLinks)
            {
                Debug.Log($"[DeepLinkHandler] Received deep link: {url}");
            }

            var data = ParseDeepLink(url);
            if (data == null)
            {
                Debug.LogWarning($"[DeepLinkHandler] Failed to parse deep link: {url}");
                return;
            }

            // Store credentials
            if (!string.IsNullOrEmpty(data.Token))
            {
                AuthManager.SetCredentials(
                    data.Token,
                    data.UserName,
                    data.UserId,
                    data.ServerUrl
                );

                Debug.Log($"[DeepLinkHandler] Authenticated as: {data.UserName ?? "(anonymous)"}");
            }

            // Fire event
            OnDeepLinkReceived?.Invoke(data);

            // Load scene if specified
            if (autoLoadScene && !string.IsNullOrEmpty(defaultScene))
            {
                var sceneName = !string.IsNullOrEmpty(data.Scene) ? data.Scene : defaultScene;
                LoadScene(sceneName);
            }
        }

        /// <summary>
        /// Parse a deep link URL into structured data.
        /// </summary>
        public static DeepLinkData ParseDeepLink(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            try
            {
                // Handle both sensocto:// and sensocto:/// formats
                // Also handle https:// callback URLs
                Uri uri;
                if (url.StartsWith("sensocto://"))
                {
                    // Custom scheme - parse manually
                    var queryStart = url.IndexOf('?');
                    if (queryStart < 0)
                        return new DeepLinkData();

                    var query = url.Substring(queryStart + 1);
                    return ParseQueryString(query);
                }
                else if (Uri.TryCreate(url, UriKind.Absolute, out uri))
                {
                    return ParseQueryString(uri.Query.TrimStart('?'));
                }
                else
                {
                    Debug.LogWarning($"[DeepLinkHandler] Invalid URL format: {url}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DeepLinkHandler] Error parsing deep link: {ex.Message}");
                return null;
            }
        }

        private static DeepLinkData ParseQueryString(string query)
        {
            var data = new DeepLinkData();

            if (string.IsNullOrEmpty(query))
                return data;

            var pairs = query.Split('&');
            foreach (var pair in pairs)
            {
                var keyValue = pair.Split(new[] { '=' }, 2);
                if (keyValue.Length != 2)
                    continue;

                var key = Uri.UnescapeDataString(keyValue[0]).ToLowerInvariant();
                var value = Uri.UnescapeDataString(keyValue[1]);

                switch (key)
                {
                    case "token":
                        data.Token = value;
                        break;
                    case "user":
                    case "user_name":
                    case "username":
                        data.UserName = value;
                        break;
                    case "user_id":
                    case "userid":
                        data.UserId = value;
                        break;
                    case "server":
                    case "server_url":
                        data.ServerUrl = value;
                        break;
                    case "scene":
                        data.Scene = value;
                        break;
                    default:
                        data.ExtraParams[key] = value;
                        break;
                }
            }

            return data;
        }

        private void LoadScene(string sceneName)
        {
            // Check if scene exists
            var sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
            if (sceneIndex < 0)
            {
                // Try without path
                for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                {
                    var path = SceneUtility.GetScenePathByBuildIndex(i);
                    if (path.Contains(sceneName))
                    {
                        sceneIndex = i;
                        break;
                    }
                }
            }

            if (sceneIndex >= 0)
            {
                Debug.Log($"[DeepLinkHandler] Loading scene: {sceneName}");
                SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            }
            else
            {
                Debug.LogWarning($"[DeepLinkHandler] Scene not found: {sceneName}");
            }
        }

        #region Editor Testing

        [ContextMenu("Test Deep Link")]
        private void TestDeepLinkInEditor()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Enter Play mode to test deep links");
                return;
            }

            if (string.IsNullOrEmpty(testDeepLink))
            {
                testDeepLink = "sensocto://auth?token=test-token-123&user=TestUser";
            }

            OnDeepLinkActivated(testDeepLink);
        }

        [ContextMenu("Clear Auth")]
        private void ClearAuth()
        {
            AuthManager.ClearCredentials();
        }

        [ContextMenu("Show Current Auth")]
        private void ShowCurrentAuth()
        {
            Debug.Log($"[AuthManager] Token: {(AuthManager.HasValidToken() ? "SET" : "NOT SET")}");
            Debug.Log($"[AuthManager] User: {AuthManager.UserName}");
            Debug.Log($"[AuthManager] UserId: {AuthManager.UserId}");
            Debug.Log($"[AuthManager] Server: {AuthManager.GetServerUrl()}");
        }

        #endregion
    }
}
