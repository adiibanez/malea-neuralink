using UnityEngine;
using Sensocto.SDK;

namespace Sensocto.Auth
{
    /// <summary>
    /// Runtime deep link receiver that handles sensocto:// URL scheme.
    /// Auto-initializes on app start and processes incoming auth links.
    ///
    /// Supports:
    /// - sensocto://auth?token=JWT_TOKEN
    /// - sensocto://auth?token=JWT_TOKEN&user=UserName
    /// - sensocto://auth?token=JWT_TOKEN&user=UserName&server=wss://sensocto.fly.dev/socket
    /// </summary>
    public class DeepLinkReceiver : MonoBehaviour
    {
        private static DeepLinkReceiver _instance;
        private static bool _initialized;

        [Header("Debug")]
        [SerializeField] private bool logDeepLinks = true;

        /// <summary>
        /// Auto-initialize when the app starts.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (_initialized) return;
            _initialized = true;

            // Check if we already have an instance
            _instance = FindFirstObjectByType<DeepLinkReceiver>();
            if (_instance == null)
            {
                var go = new GameObject("DeepLinkReceiver");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<DeepLinkReceiver>();
                Debug.Log("[DeepLinkReceiver] Auto-created");
            }
        }

        void Awake()
        {
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

        void OnDestroy()
        {
            Application.deepLinkActivated -= OnDeepLinkActivated;
            if (_instance == this)
                _instance = null;
        }

        private void OnDeepLinkActivated(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            if (logDeepLinks)
                Debug.Log($"[DeepLinkReceiver] Received: {url}");

            // Only process sensocto:// URLs
            if (!url.StartsWith("sensocto://"))
            {
                if (logDeepLinks)
                    Debug.Log($"[DeepLinkReceiver] Ignoring non-sensocto URL: {url}");
                return;
            }

            ProcessDeepLink(url);
        }

        private void ProcessDeepLink(string url)
        {
            try
            {
                var data = DeepLinkHandler.ParseDeepLink(url);

                if (data == null)
                {
                    Debug.LogWarning($"[DeepLinkReceiver] Failed to parse deep link: {url}");
                    return;
                }

                if (string.IsNullOrEmpty(data.Token))
                {
                    Debug.LogWarning("[DeepLinkReceiver] Deep link has no token");
                    return;
                }

                // Extract server URL from deep link or use default
                var serverUrl = data.ServerUrl;
                if (string.IsNullOrEmpty(serverUrl))
                {
                    // Check if we have a stored server URL, otherwise use production
                    serverUrl = AuthManager.GetServerUrl();
                    if (string.IsNullOrEmpty(serverUrl))
                    {
                        serverUrl = "wss://sensocto.fly.dev/socket";
                    }
                }

                // Set credentials
                AuthManager.SetCredentials(data.Token, data.UserName, data.UserId, serverUrl);

                if (logDeepLinks)
                {
                    Debug.Log($"[DeepLinkReceiver] Authenticated: user={data.UserName ?? "(none)"}, server={serverUrl}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DeepLinkReceiver] Error processing deep link: {ex.Message}");
            }
        }

        /// <summary>
        /// Manually process a deep link URL (useful for testing).
        /// </summary>
        public static void ProcessUrl(string url)
        {
            if (_instance != null)
            {
                _instance.OnDeepLinkActivated(url);
            }
            else
            {
                Debug.LogWarning("[DeepLinkReceiver] No instance available");
            }
        }
    }
}
