using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using Sensocto.SDK;

/// <summary>
/// Authentication UI modal for managing Sensocto server connection.
/// Shows connection status, QR code scanning, and manual entry options.
///
/// Usage: Call AuthUI.ShowModal() from anywhere to display the auth panel.
/// The UI will automatically attach to any UIDocument in the scene.
/// </summary>
public class AuthUI : MonoBehaviour
{
    private const string DEFAULT_SERVER_URL = "wss://sensocto.fly.dev/socket";
    private const string LOCALHOST_SERVER_URL = "wss://localhost:4001/socket";

    [Header("Colors")]
    [SerializeField] private Color connectedColor = new Color(0.30f, 0.69f, 0.31f);
    [SerializeField] private Color disconnectedColor = new Color(0.96f, 0.26f, 0.21f);

    private VisualElement _root;
    private VisualElement _modalOverlay;
    private VisualElement _modalPanel;
    private VisualElement _statusIndicator;
    private Label _statusLabel;
    private Label _userLabel;
    private Label _serverLabel;
    private TextField _serverUrlField;
    private TextField _manualUrlField;
    private Button _scanQRBtn;
    private Button _enterManuallyBtn;
    private Button _signOutBtn;
    private Button _closeBtn;
    private VisualElement _manualEntryPanel;
    private VisualElement _scannerPanel;
    private QRCodeScanner _qrScanner;

    private bool _isShowingManualEntry;
    private bool _isShowingScanner;
    private bool _isInitialized;

    private static AuthUI _instance;

    /// <summary>
    /// Show the auth modal. Creates the UI if needed.
    /// Call this from anywhere to display the authentication panel.
    /// </summary>
    public static void ShowModal()
    {
        // Check if existing instance is still valid (might be destroyed from scene change)
        if (_instance != null && _instance.gameObject == null)
        {
            _instance = null;
        }

        // Find or create instance
        if (_instance == null)
        {
            _instance = FindFirstObjectByType<AuthUI>();
        }

        if (_instance == null)
        {
            // Create new GameObject with AuthUI
            var go = new GameObject("AuthUI");
            _instance = go.AddComponent<AuthUI>();
            Debug.Log("[AuthUI] Created new AuthUI instance");
        }

        _instance.Show();
    }

    /// <summary>
    /// Hide the auth modal if it's showing.
    /// </summary>
    public static void HideModal()
    {
        if (_instance != null)
        {
            _instance.Hide();
        }
    }

    void OnEnable()
    {
        StartCoroutine(InitializeDelayed());
    }

    void OnDisable()
    {
        AuthManager.OnAuthChanged -= OnAuthChanged;

        if (_qrScanner != null)
        {
            _qrScanner.OnQRCodeScanned -= OnQRCodeScanned;
            _qrScanner.OnScanError -= OnScanError;
        }
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        // Clean up UI elements
        if (_modalOverlay != null && _root != null)
        {
            _root.Remove(_modalOverlay);
        }
        if (_scannerPanel != null && _root != null)
        {
            _root.Remove(_scannerPanel);
        }
    }

    private IEnumerator InitializeDelayed()
    {
        yield return null;

        // Re-initialize if root is gone (scene changed)
        if (_isInitialized && _root == null)
        {
            Debug.Log("[AuthUI] Re-initializing after scene change");
            _isInitialized = false;
            _modalOverlay = null;
            _scannerPanel = null;
        }

        if (_isInitialized) yield break;

        // Find any UIDocument in the scene
        var uiDoc = FindFirstObjectByType<UIDocument>();
        if (uiDoc == null)
        {
            Debug.LogError("[AuthUI] No UIDocument found in scene");
            yield break;
        }

        _root = uiDoc.rootVisualElement;
        if (_root == null)
        {
            Debug.LogError("[AuthUI] UIDocument has no root element");
            yield break;
        }

        // Subscribe to auth changes (unsubscribe first to avoid duplicates)
        AuthManager.OnAuthChanged -= OnAuthChanged;
        AuthManager.OnAuthChanged += OnAuthChanged;

        // Find or create QR scanner
        _qrScanner = FindFirstObjectByType<QRCodeScanner>();
        if (_qrScanner == null)
        {
            _qrScanner = gameObject.AddComponent<QRCodeScanner>();
        }

        _qrScanner.OnQRCodeScanned -= OnQRCodeScanned;
        _qrScanner.OnScanError -= OnScanError;
        _qrScanner.OnQRCodeScanned += OnQRCodeScanned;
        _qrScanner.OnScanError += OnScanError;

        CreateUI();
        UpdateStatusDisplay();
        Hide(); // Start hidden

        _isInitialized = true;
        Debug.Log("[AuthUI] Initialized successfully");
    }

    private void CreateUI()
    {
        // Create modal overlay (full-screen dark background)
        _modalOverlay = new VisualElement { name = "AuthModalOverlay" };
        _modalOverlay.style.position = Position.Absolute;
        _modalOverlay.style.top = 0;
        _modalOverlay.style.left = 0;
        _modalOverlay.style.right = 0;
        _modalOverlay.style.bottom = 0;
        _modalOverlay.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.7f));
        _modalOverlay.style.justifyContent = Justify.Center;
        _modalOverlay.style.alignItems = Align.Center;
        _modalOverlay.style.display = DisplayStyle.None;

        // Modal panel
        _modalPanel = new VisualElement { name = "AuthModalPanel" };
        _modalPanel.style.backgroundColor = new StyleColor(Color.white);
        _modalPanel.style.borderTopLeftRadius = 12;
        _modalPanel.style.borderTopRightRadius = 12;
        _modalPanel.style.borderBottomLeftRadius = 12;
        _modalPanel.style.borderBottomRightRadius = 12;
        _modalPanel.style.paddingTop = 20;
        _modalPanel.style.paddingBottom = 20;
        _modalPanel.style.paddingLeft = 25;
        _modalPanel.style.paddingRight = 25;
        _modalPanel.style.minWidth = 350;
        _modalPanel.style.maxWidth = 400;

        // Header row
        var headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.justifyContent = Justify.SpaceBetween;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.marginBottom = 15;

        var title = new Label("Account");
        title.style.fontSize = 20;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new StyleColor(Color.black);

        _closeBtn = new Button(Hide);
        _closeBtn.text = "X";
        _closeBtn.style.width = 30;
        _closeBtn.style.height = 30;
        _closeBtn.style.fontSize = 16;
        _closeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
        _closeBtn.style.backgroundColor = new StyleColor(new Color(0.9f, 0.9f, 0.9f));
        _closeBtn.style.borderTopLeftRadius = 15;
        _closeBtn.style.borderTopRightRadius = 15;
        _closeBtn.style.borderBottomLeftRadius = 15;
        _closeBtn.style.borderBottomRightRadius = 15;

        headerRow.Add(title);
        headerRow.Add(_closeBtn);
        _modalPanel.Add(headerRow);

        // Status section
        var statusSection = new VisualElement();
        statusSection.style.backgroundColor = new StyleColor(new Color(0.95f, 0.95f, 0.95f));
        statusSection.style.borderTopLeftRadius = 8;
        statusSection.style.borderTopRightRadius = 8;
        statusSection.style.borderBottomLeftRadius = 8;
        statusSection.style.borderBottomRightRadius = 8;
        statusSection.style.paddingTop = 12;
        statusSection.style.paddingBottom = 12;
        statusSection.style.paddingLeft = 12;
        statusSection.style.paddingRight = 12;
        statusSection.style.marginBottom = 15;

        var statusRow = new VisualElement();
        statusRow.style.flexDirection = FlexDirection.Row;
        statusRow.style.alignItems = Align.Center;
        statusRow.style.marginBottom = 8;

        _statusIndicator = new VisualElement();
        _statusIndicator.style.width = 12;
        _statusIndicator.style.height = 12;
        _statusIndicator.style.borderTopLeftRadius = 6;
        _statusIndicator.style.borderTopRightRadius = 6;
        _statusIndicator.style.borderBottomLeftRadius = 6;
        _statusIndicator.style.borderBottomRightRadius = 6;
        _statusIndicator.style.marginRight = 8;

        _statusLabel = new Label("Offline (Single Player)");
        _statusLabel.style.fontSize = 14;
        _statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        _statusLabel.style.color = new StyleColor(Color.black);

        statusRow.Add(_statusIndicator);
        statusRow.Add(_statusLabel);
        statusSection.Add(statusRow);

        _userLabel = new Label("");
        _userLabel.style.fontSize = 12;
        _userLabel.style.color = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
        _userLabel.style.marginBottom = 4;
        _userLabel.style.display = DisplayStyle.None;
        statusSection.Add(_userLabel);

        _serverLabel = new Label("");
        _serverLabel.style.fontSize = 11;
        _serverLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
        statusSection.Add(_serverLabel);

        _modalPanel.Add(statusSection);

        // Optional login note
        var optionalNote = new Label("Login is optional. Single player mode works offline.");
        optionalNote.style.fontSize = 11;
        optionalNote.style.color = new StyleColor(new Color(0.4f, 0.4f, 0.4f));
        optionalNote.style.marginBottom = 12;
        optionalNote.style.unityTextAlign = TextAnchor.MiddleCenter;
        _modalPanel.Add(optionalNote);

        // Server URL field
        var serverLabel = new Label("Server URL");
        serverLabel.style.fontSize = 12;
        serverLabel.style.color = new StyleColor(Color.black);
        serverLabel.style.marginBottom = 4;
        _modalPanel.Add(serverLabel);

        _serverUrlField = new TextField();
        _serverUrlField.value = GetCurrentServerUrl();
        _serverUrlField.style.marginBottom = 8;
        _modalPanel.Add(_serverUrlField);

        // Server presets row
        var serverPresetsRow = new VisualElement();
        serverPresetsRow.style.flexDirection = FlexDirection.Row;
        serverPresetsRow.style.marginBottom = 15;

        var productionBtn = new Button(() => _serverUrlField.value = DEFAULT_SERVER_URL);
        productionBtn.text = "Production";
        productionBtn.style.flexGrow = 1;
        productionBtn.style.height = 28;
        productionBtn.style.fontSize = 11;
        productionBtn.style.marginRight = 5;
        productionBtn.style.backgroundColor = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
        productionBtn.style.borderTopLeftRadius = 4;
        productionBtn.style.borderTopRightRadius = 4;
        productionBtn.style.borderBottomLeftRadius = 4;
        productionBtn.style.borderBottomRightRadius = 4;

        var localhostBtn = new Button(() => _serverUrlField.value = LOCALHOST_SERVER_URL);
        localhostBtn.text = "Localhost";
        localhostBtn.style.flexGrow = 1;
        localhostBtn.style.height = 28;
        localhostBtn.style.fontSize = 11;
        localhostBtn.style.marginLeft = 5;
        localhostBtn.style.backgroundColor = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
        localhostBtn.style.borderTopLeftRadius = 4;
        localhostBtn.style.borderTopRightRadius = 4;
        localhostBtn.style.borderBottomLeftRadius = 4;
        localhostBtn.style.borderBottomRightRadius = 4;

        serverPresetsRow.Add(productionBtn);
        serverPresetsRow.Add(localhostBtn);
        _modalPanel.Add(serverPresetsRow);

        // Action buttons
        _scanQRBtn = CreateActionButton("Scan QR Code", OnScanQRClicked);
        _scanQRBtn.style.marginBottom = 10;
        _modalPanel.Add(_scanQRBtn);

        _enterManuallyBtn = CreateActionButton("Paste Link / Token", OnEnterManuallyClicked);
        _enterManuallyBtn.style.marginBottom = 10;
        _modalPanel.Add(_enterManuallyBtn);

        _signOutBtn = CreateActionButton("Sign Out", OnSignOutClicked);
        _signOutBtn.style.backgroundColor = new StyleColor(new Color(0.7f, 0.2f, 0.2f));
        _signOutBtn.style.marginBottom = 0;
        _modalPanel.Add(_signOutBtn);

        // Manual entry panel (hidden by default)
        CreateManualEntryPanel();

        _modalOverlay.Add(_modalPanel);
        _root.Add(_modalOverlay);

        // Scanner panel (hidden by default)
        CreateScannerPanel();
    }

    private void CreateManualEntryPanel()
    {
        _manualEntryPanel = new VisualElement { name = "ManualEntryPanel" };
        _manualEntryPanel.style.marginTop = 15;
        _manualEntryPanel.style.display = DisplayStyle.None;

        var label = new Label("Paste deep link URL or token:");
        label.style.fontSize = 12;
        label.style.color = new StyleColor(Color.black);
        label.style.marginBottom = 4;
        _manualEntryPanel.Add(label);

        _manualUrlField = new TextField();
        _manualUrlField.multiline = false;
        _manualUrlField.style.marginBottom = 10;
        _manualEntryPanel.Add(_manualUrlField);

        var buttonsRow = new VisualElement();
        buttonsRow.style.flexDirection = FlexDirection.Row;
        buttonsRow.style.justifyContent = Justify.SpaceBetween;

        var cancelBtn = new Button(HideManualEntry);
        cancelBtn.text = "Cancel";
        cancelBtn.style.flexGrow = 1;
        cancelBtn.style.marginRight = 5;
        cancelBtn.style.height = 40;
        cancelBtn.style.backgroundColor = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
        cancelBtn.style.borderTopLeftRadius = 8;
        cancelBtn.style.borderTopRightRadius = 8;
        cancelBtn.style.borderBottomLeftRadius = 8;
        cancelBtn.style.borderBottomRightRadius = 8;

        var submitBtn = new Button(OnManualSubmit);
        submitBtn.text = "Connect";
        submitBtn.style.flexGrow = 1;
        submitBtn.style.marginLeft = 5;
        submitBtn.style.height = 40;
        submitBtn.style.backgroundColor = new StyleColor(connectedColor);
        submitBtn.style.color = new StyleColor(Color.white);
        submitBtn.style.borderTopLeftRadius = 8;
        submitBtn.style.borderTopRightRadius = 8;
        submitBtn.style.borderBottomLeftRadius = 8;
        submitBtn.style.borderBottomRightRadius = 8;

        buttonsRow.Add(cancelBtn);
        buttonsRow.Add(submitBtn);
        _manualEntryPanel.Add(buttonsRow);

        _modalPanel.Add(_manualEntryPanel);
    }

    private void CreateScannerPanel()
    {
        _scannerPanel = new VisualElement { name = "ScannerPanel" };
        _scannerPanel.style.position = Position.Absolute;
        _scannerPanel.style.top = 0;
        _scannerPanel.style.left = 0;
        _scannerPanel.style.right = 0;
        _scannerPanel.style.bottom = 0;
        _scannerPanel.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.95f));
        _scannerPanel.style.justifyContent = Justify.Center;
        _scannerPanel.style.alignItems = Align.Center;
        _scannerPanel.style.display = DisplayStyle.None;

        var previewContainer = new VisualElement { name = "CameraPreviewContainer" };
        previewContainer.style.width = 300;
        previewContainer.style.height = 300;
        previewContainer.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f));
        previewContainer.style.borderTopLeftRadius = 12;
        previewContainer.style.borderTopRightRadius = 12;
        previewContainer.style.borderBottomLeftRadius = 12;
        previewContainer.style.borderBottomRightRadius = 12;
        previewContainer.style.marginBottom = 20;
        previewContainer.style.justifyContent = Justify.Center;
        previewContainer.style.alignItems = Align.Center;

        var cameraImage = new VisualElement { name = "QRCameraImage" };
        cameraImage.style.position = Position.Absolute;
        cameraImage.style.top = 0;
        cameraImage.style.left = 0;
        cameraImage.style.right = 0;
        cameraImage.style.bottom = 0;
        cameraImage.style.borderTopLeftRadius = 12;
        cameraImage.style.borderTopRightRadius = 12;
        cameraImage.style.borderBottomLeftRadius = 12;
        cameraImage.style.borderBottomRightRadius = 12;
        previewContainer.Add(cameraImage);

        var viewfinder = new VisualElement { name = "Viewfinder" };
        viewfinder.style.width = 200;
        viewfinder.style.height = 200;
        viewfinder.style.borderTopWidth = 3;
        viewfinder.style.borderBottomWidth = 3;
        viewfinder.style.borderLeftWidth = 3;
        viewfinder.style.borderRightWidth = 3;
        viewfinder.style.borderTopColor = new StyleColor(Color.white);
        viewfinder.style.borderBottomColor = new StyleColor(Color.white);
        viewfinder.style.borderLeftColor = new StyleColor(Color.white);
        viewfinder.style.borderRightColor = new StyleColor(Color.white);
        viewfinder.style.borderTopLeftRadius = 12;
        viewfinder.style.borderTopRightRadius = 12;
        viewfinder.style.borderBottomLeftRadius = 12;
        viewfinder.style.borderBottomRightRadius = 12;
        previewContainer.Add(viewfinder);

        _scannerPanel.Add(previewContainer);

        var instructions = new Label("Point camera at QR code");
        instructions.style.color = new StyleColor(Color.white);
        instructions.style.fontSize = 16;
        instructions.style.marginBottom = 20;
        _scannerPanel.Add(instructions);

        var cancelScanBtn = new Button(HideScanner);
        cancelScanBtn.text = "Cancel";
        cancelScanBtn.style.width = 150;
        cancelScanBtn.style.height = 44;
        cancelScanBtn.style.fontSize = 16;
        cancelScanBtn.style.backgroundColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
        cancelScanBtn.style.color = new StyleColor(Color.white);
        cancelScanBtn.style.borderTopLeftRadius = 22;
        cancelScanBtn.style.borderTopRightRadius = 22;
        cancelScanBtn.style.borderBottomLeftRadius = 22;
        cancelScanBtn.style.borderBottomRightRadius = 22;
        _scannerPanel.Add(cancelScanBtn);

        _root.Add(_scannerPanel);
    }

    private Button CreateActionButton(string text, System.Action onClick)
    {
        var btn = new Button(onClick);
        btn.text = text;
        btn.style.height = 44;
        btn.style.fontSize = 14;
        btn.style.unityFontStyleAndWeight = FontStyle.Bold;
        btn.style.backgroundColor = new StyleColor(connectedColor);
        btn.style.color = new StyleColor(Color.white);
        btn.style.borderTopLeftRadius = 8;
        btn.style.borderTopRightRadius = 8;
        btn.style.borderBottomLeftRadius = 8;
        btn.style.borderBottomRightRadius = 8;
        btn.style.borderTopWidth = 0;
        btn.style.borderBottomWidth = 0;
        btn.style.borderLeftWidth = 0;
        btn.style.borderRightWidth = 0;
        return btn;
    }

    private string GetCurrentServerUrl()
    {
        var stored = AuthManager.GetServerUrl();
        return string.IsNullOrEmpty(stored) ? DEFAULT_SERVER_URL : stored;
    }

    // UI Actions
    private void OnScanQRClicked()
    {
        HideManualEntry();
        ShowScanner();
    }

    private void OnEnterManuallyClicked()
    {
        HideScanner();
        ShowManualEntry();
    }

    private void OnSignOutClicked()
    {
        AuthManager.ClearCredentials();
        Debug.Log("[AuthUI] Signed out");
    }

    private void OnManualSubmit()
    {
        var input = _manualUrlField.value?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            Debug.LogWarning("[AuthUI] No URL or token entered");
            return;
        }

        ProcessAuthInput(input);
        HideManualEntry();
    }

    private void ProcessAuthInput(string input)
    {
        if (input.StartsWith("sensocto://") || input.StartsWith("http"))
        {
            var data = DeepLinkHandler.ParseDeepLink(input);
            if (data != null && !string.IsNullOrEmpty(data.Token))
            {
                var serverUrl = _serverUrlField.value?.Trim();
                if (string.IsNullOrEmpty(serverUrl))
                    serverUrl = data.ServerUrl;

                AuthManager.SetCredentials(data.Token, data.UserName, data.UserId, serverUrl);
                Debug.Log($"[AuthUI] Authenticated via URL: {data.UserName}");
                return;
            }
        }

        // Treat as raw token
        var server = _serverUrlField.value?.Trim();
        if (string.IsNullOrEmpty(server))
            server = DEFAULT_SERVER_URL;

        AuthManager.SetCredentials(input, null, null, server);
        Debug.Log("[AuthUI] Authenticated with token");
    }

    private void ShowManualEntry()
    {
        _isShowingManualEntry = true;
        _manualEntryPanel.style.display = DisplayStyle.Flex;
        _manualUrlField.value = "";
        _manualUrlField.Focus();
    }

    private void HideManualEntry()
    {
        _isShowingManualEntry = false;
        _manualEntryPanel.style.display = DisplayStyle.None;
    }

    private void ShowScanner()
    {
        // Check if ZXing is available
        if (!QRCodeScanner.IsLibraryAvailable)
        {
            Debug.LogWarning("[AuthUI] QR scanner library not installed");
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.DisplayDialog("QR Scanner Not Installed",
                "ZXing library is required for QR scanning.\n\n" +
                "Would you like to install it now?",
                "Install", "Cancel"))
            {
                UnityEditor.EditorApplication.ExecuteMenuItem("Sensocto/Auth/Setup QR Scanner (Download ZXing)");
            }
#endif
            return;
        }

        _isShowingScanner = true;
        _scannerPanel.style.display = DisplayStyle.Flex;

        if (_qrScanner != null)
        {
            var cameraImage = _scannerPanel.Q<VisualElement>("QRCameraImage");
            _qrScanner.StartScanning(cameraImage);
        }
    }

    private void HideScanner()
    {
        _isShowingScanner = false;
        _scannerPanel.style.display = DisplayStyle.None;

        if (_qrScanner != null)
            _qrScanner.StopScanning();
    }

    // Event handlers
    private void OnAuthChanged()
    {
        UpdateStatusDisplay();
    }

    private void OnQRCodeScanned(string url)
    {
        Debug.Log($"[AuthUI] QR Code scanned: {url}");
        HideScanner();
        ProcessAuthInput(url);
    }

    private void OnScanError(string error)
    {
        Debug.LogWarning($"[AuthUI] Scan error: {error}");
    }

    private void UpdateStatusDisplay()
    {
        if (_statusIndicator == null)
        {
            Debug.LogWarning("[AuthUI] UpdateStatusDisplay called but _statusIndicator is null");
            return;
        }

        // Read directly from AuthManager
        bool hasToken = AuthManager.HasValidToken();
        bool isAuth = AuthManager.IsAuthenticated;
        var token = AuthManager.GetToken();
        var userName = AuthManager.UserName;
        var serverUrl = AuthManager.GetServerUrl();

        Debug.Log($"[AuthUI] UpdateStatusDisplay: hasToken={hasToken}, isAuth={isAuth}, tokenLen={token?.Length ?? 0}, user='{userName}', server='{serverUrl}'");

        var offlineColor = new Color(0.5f, 0.5f, 0.5f);

        // Update status indicator and label
        if (isAuth)
        {
            _statusIndicator.style.backgroundColor = new StyleColor(connectedColor);
            _statusLabel.text = "Connected";
            Debug.Log("[AuthUI] Set status to Connected (green)");
        }
        else
        {
            _statusIndicator.style.backgroundColor = new StyleColor(offlineColor);
            _statusLabel.text = "Offline (Single Player)";
            Debug.Log("[AuthUI] Set status to Offline (gray)");
        }

        _userLabel.text = !string.IsNullOrEmpty(userName) ? $"User: {userName}" : "";
        _userLabel.style.display = !string.IsNullOrEmpty(userName) ? DisplayStyle.Flex : DisplayStyle.None;

        if (string.IsNullOrEmpty(serverUrl))
            serverUrl = DEFAULT_SERVER_URL;
        _serverLabel.text = $"Server: {serverUrl}";

        if (_serverUrlField != null)
            _serverUrlField.value = serverUrl;

        _signOutBtn.style.display = isAuth ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // Public API
    public void Show()
    {
        Debug.Log($"[AuthUI] Show called. isInitialized={_isInitialized}, modalOverlay={_modalOverlay != null}, root={_root != null}");

        // Check if we need to re-initialize (scene might have changed)
        if (_isInitialized && (_root == null || _modalOverlay == null))
        {
            Debug.Log("[AuthUI] Need to re-initialize, root or modal is gone");
            _isInitialized = false;
        }

        if (!_isInitialized)
        {
            Debug.Log("[AuthUI] Not initialized yet, initializing...");
            StartCoroutine(ShowAfterInit());
            return;
        }

        if (_modalOverlay != null)
        {
            _modalOverlay.style.display = DisplayStyle.Flex;
            UpdateStatusDisplay();
            Debug.Log("[AuthUI] Modal shown");
        }
        else
        {
            Debug.LogError("[AuthUI] _modalOverlay is null after initialization!");
        }
    }

    private IEnumerator ShowAfterInit()
    {
        // Trigger initialization if not already running
        if (!_isInitialized)
        {
            StartCoroutine(InitializeDelayed());
        }

        // Wait for initialization with timeout
        float timeout = 3f;
        while (!_isInitialized && timeout > 0)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (!_isInitialized)
        {
            Debug.LogError("[AuthUI] Initialization timed out!");
            yield break;
        }

        // Now show
        if (_modalOverlay != null)
        {
            _modalOverlay.style.display = DisplayStyle.Flex;
            UpdateStatusDisplay();
            Debug.Log("[AuthUI] Modal shown after init");
        }
    }

    public void Hide()
    {
        HideManualEntry();
        HideScanner();

        if (_modalOverlay != null)
            _modalOverlay.style.display = DisplayStyle.None;
    }

    public bool IsVisible => _modalOverlay?.style.display == DisplayStyle.Flex;
}
