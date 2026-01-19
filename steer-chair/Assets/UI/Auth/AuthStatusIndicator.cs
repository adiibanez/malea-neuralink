using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Sensocto.SDK;

/// <summary>
/// Compact authentication status indicator for Drive/Simulator scenes.
/// Shows connection state with color-coded indicator.
/// Click to open full AuthUI panel.
/// </summary>
public class AuthStatusIndicator : MonoBehaviour
{
    [Header("UI Settings")]
    [SerializeField] private Color connectedColor = new Color(0.30f, 0.69f, 0.31f);  // Green

    private VisualElement _root;
    private VisualElement _container;
    private VisualElement _statusIndicator;
    private Label _statusLabel;
    private bool _isInitialized;

    private static AuthStatusIndicator _instance;

    /// <summary>
    /// Ensures an AuthStatusIndicator exists in the scene.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        // Subscribe to scene changes
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        // Initialize for current scene
        TryCreateForCurrentScene();
    }

    private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        TryCreateForCurrentScene();
    }

    private static void TryCreateForCurrentScene()
    {
        var sceneName = SceneManager.GetActiveScene().name;
        if (sceneName != "DriveChair" && sceneName != "Simulator")
            return;

        // Check if instance is still valid
        if (_instance != null && _instance.gameObject == null)
            _instance = null;

        if (_instance == null)
            _instance = FindFirstObjectByType<AuthStatusIndicator>();

        if (_instance == null)
        {
            var go = new GameObject("AuthStatusIndicator");
            _instance = go.AddComponent<AuthStatusIndicator>();
            Debug.Log($"[AuthStatusIndicator] Auto-created for {sceneName}");
        }
    }

    void OnEnable()
    {
        _instance = this;
        StartCoroutine(InitializeDelayed());
    }

    void OnDisable()
    {
        AuthManager.OnAuthChanged -= OnAuthChanged;
    }

    private IEnumerator InitializeDelayed()
    {
        yield return null;

        // Find any UIDocument in the scene
        var uiDoc = FindFirstObjectByType<UIDocument>();
        if (uiDoc == null)
        {
            Debug.LogWarning("[AuthStatusIndicator] No UIDocument found");
            yield break;
        }

        _root = uiDoc.rootVisualElement;
        if (_root == null) yield break;

        // Subscribe to auth changes
        AuthManager.OnAuthChanged -= OnAuthChanged;
        AuthManager.OnAuthChanged += OnAuthChanged;

        // Find or create status elements
        SetupStatusElements();

        // Set initial state
        UpdateStatusDisplay();

        _isInitialized = true;
        Debug.Log("[AuthStatusIndicator] Initialized");
    }

    private void SetupStatusElements()
    {
        // Look for existing container in UXML
        _container = _root.Q<VisualElement>("AuthStatusContainer");

        if (_container != null)
        {
            // Use existing elements from UXML
            _statusIndicator = _container.Q<VisualElement>("AuthStatusIndicator");
            _statusLabel = _container.Q<Label>("AuthStatusLabel");

            if (_statusIndicator == null)
                Debug.LogWarning("[AuthStatusIndicator] AuthStatusIndicator element not found in UXML");
            if (_statusLabel == null)
                Debug.LogWarning("[AuthStatusIndicator] AuthStatusLabel element not found in UXML");
        }
        else
        {
            // Create the container dynamically
            CreateStatusContainer();
        }

        // Make container clickable
        if (_container != null)
        {
            _container.RegisterCallback<ClickEvent>(OnContainerClicked);
            _container.AddToClassList("auth-status-container");
        }
    }

    private void CreateStatusContainer()
    {
        // Find LeftMenu to insert our container
        var leftMenu = _root.Q<VisualElement>("LeftMenu");
        if (leftMenu == null)
        {
            Debug.LogWarning("[AuthStatusIndicator] LeftMenu not found in UXML");
            return;
        }

        // Find SerialStatusContainer to insert after
        var serialContainer = leftMenu.Q<VisualElement>("SerialStatusContainer");

        // Create auth status container
        _container = new VisualElement { name = "AuthStatusContainer" };
        _container.style.marginTop = 10;
        _container.style.backgroundColor = new StyleColor(new Color(1, 1, 1, 0.9f));
        _container.style.borderTopLeftRadius = 8;
        _container.style.borderTopRightRadius = 8;
        _container.style.borderBottomLeftRadius = 8;
        _container.style.borderBottomRightRadius = 8;
        _container.style.paddingTop = 8;
        _container.style.paddingBottom = 8;
        _container.style.paddingLeft = 8;
        _container.style.paddingRight = 8;

        // Header
        var header = new Label("Account");
        header.style.color = new StyleColor(Color.black);
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 4;
        _container.Add(header);

        // Status row
        var statusRow = new VisualElement();
        statusRow.style.flexDirection = FlexDirection.Row;
        statusRow.style.alignItems = Align.Center;

        _statusIndicator = new VisualElement { name = "AuthStatusIndicator" };
        _statusIndicator.style.width = 12;
        _statusIndicator.style.height = 12;
        _statusIndicator.style.borderTopLeftRadius = 6;
        _statusIndicator.style.borderTopRightRadius = 6;
        _statusIndicator.style.borderBottomLeftRadius = 6;
        _statusIndicator.style.borderBottomRightRadius = 6;
        _statusIndicator.style.marginRight = 8;

        _statusLabel = new Label { name = "AuthStatusLabel" };
        _statusLabel.style.color = new StyleColor(Color.black);
        _statusLabel.style.fontSize = 12;

        statusRow.Add(_statusIndicator);
        statusRow.Add(_statusLabel);
        _container.Add(statusRow);

        // Insert after SerialStatusContainer or at end of LeftMenu
        if (serialContainer != null)
        {
            int index = leftMenu.IndexOf(serialContainer);
            leftMenu.Insert(index + 1, _container);
        }
        else
        {
            leftMenu.Add(_container);
        }
    }

    private void OnContainerClicked(ClickEvent evt)
    {
        AuthUI.ShowModal();
    }

    private void OnAuthChanged()
    {
        UpdateStatusDisplay();
    }

    private void UpdateStatusDisplay()
    {
        if (_statusIndicator == null || _statusLabel == null) return;

        bool isAuth = AuthManager.IsAuthenticated;
        var userName = AuthManager.UserName;

        // Gray for offline, green for connected
        var offlineColor = new Color(0.5f, 0.5f, 0.5f);
        _statusIndicator.style.backgroundColor = new StyleColor(isAuth ? connectedColor : offlineColor);

        if (isAuth)
        {
            _statusLabel.text = !string.IsNullOrEmpty(userName) ? userName : "Connected";
        }
        else
        {
            _statusLabel.text = "Offline";
        }

        Debug.Log($"[AuthStatusIndicator] Status: {(isAuth ? "Connected" : "Offline")}, user={userName}");
    }

    public void RefreshStatus()
    {
        UpdateStatusDisplay();
    }
}
