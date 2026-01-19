using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Manages the mode switching UI for wheelchair control.
/// Shows clear visual indication of Drive vs Seat mode.
/// Designed for Eoin - large, clear buttons with visual feedback.
/// Only active in DriveChair scene.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class ModeUI : MonoBehaviour
{
    private const string DRIVE_CHAIR_SCENE = "DriveChair";
    [Header("References")]
    [SerializeField] private ModeController modeController;
    [SerializeField] private WheelchairState wheelchairState;

    [Header("Colors")]
    [SerializeField] private Color driveActiveColor = new Color(0.30f, 0.69f, 0.31f);  // Green
    [SerializeField] private Color seatActiveColor = new Color(0.13f, 0.59f, 0.95f);   // Blue
    [SerializeField] private Color inactiveColor = new Color(0.6f, 0.6f, 0.6f);        // Gray
    [SerializeField] private Color warningColor = new Color(1f, 0.76f, 0.03f);         // Yellow
    [SerializeField] private Color dangerColor = new Color(0.96f, 0.26f, 0.21f);       // Red

    private VisualElement _root;
    private VisualElement _modeContainer;
    private Button _driveModeBtn;
    private Button _seatModeBtn;
    private VisualElement _confidenceIndicator;
    private Label _confidenceLabel;
    private VisualElement _actuatorSelector;
    private Button[] _actuatorButtons;
    private Label _selectedActuatorLabel;
    private VisualElement _syncPanel;
    private Button _powerCycleBtn;
    private Button _verifyStateBtn;

    void OnEnable()
    {
        // Only initialize in DriveChair scene
        if (SceneManager.GetActiveScene().name != DRIVE_CHAIR_SCENE)
        {
            enabled = false;
            return;
        }

        StartCoroutine(InitializeDelayed());
    }

    private IEnumerator InitializeDelayed()
    {
        yield return null;

        // Double-check we're in the right scene
        if (SceneManager.GetActiveScene().name != DRIVE_CHAIR_SCENE)
        {
            yield break;
        }

        // Find references
        if (modeController == null)
            modeController = FindFirstObjectByType<ModeController>();

        if (wheelchairState == null)
            wheelchairState = FindFirstObjectByType<WheelchairState>();

        if (wheelchairState == null && modeController != null)
            wheelchairState = modeController.GetComponent<WheelchairState>();

        _root = GetComponent<UIDocument>().rootVisualElement;

        // Subscribe to events
        if (wheelchairState != null)
        {
            wheelchairState.OnModeChanged += OnModeChanged;
            wheelchairState.OnActuatorSelected += OnActuatorSelected;
            wheelchairState.OnConfidenceChanged += OnConfidenceChanged;
            wheelchairState.OnSyncRequested += ShowSyncPanel;
        }

        if (modeController != null)
        {
            modeController.OnProfileLoaded += OnProfileLoaded;
        }

        CreateUI();
        UpdateModeDisplay();
    }

    void OnDisable()
    {
        if (wheelchairState != null)
        {
            wheelchairState.OnModeChanged -= OnModeChanged;
            wheelchairState.OnActuatorSelected -= OnActuatorSelected;
            wheelchairState.OnConfidenceChanged -= OnConfidenceChanged;
            wheelchairState.OnSyncRequested -= ShowSyncPanel;
        }

        if (modeController != null)
        {
            modeController.OnProfileLoaded -= OnProfileLoaded;
        }
    }

    private void CreateUI()
    {
        // Find or create the mode container in the left menu
        var leftMenu = _root.Q<VisualElement>("LeftMenu");
        if (leftMenu == null)
        {
            Debug.LogError("[ModeUI] LeftMenu not found");
            return;
        }

        // Find MacroButtonsContainer to insert mode controls above it
        var macroContainer = leftMenu.Q<VisualElement>("MacroButtonsContainer");

        // Create mode control panel
        _modeContainer = new VisualElement { name = "ModeControlPanel" };
        _modeContainer.style.backgroundColor = new StyleColor(new Color(1, 1, 1, 0.95f));
        _modeContainer.style.borderTopLeftRadius = 8;
        _modeContainer.style.borderTopRightRadius = 8;
        _modeContainer.style.borderBottomLeftRadius = 8;
        _modeContainer.style.borderBottomRightRadius = 8;
        _modeContainer.style.paddingTop = 10;
        _modeContainer.style.paddingBottom = 10;
        _modeContainer.style.paddingLeft = 8;
        _modeContainer.style.paddingRight = 8;
        _modeContainer.style.marginTop = 10;
        _modeContainer.style.marginBottom = 10;

        // Header
        var header = new Label("Mode");
        header.style.color = new StyleColor(Color.black);
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 8;
        _modeContainer.Add(header);

        // Mode buttons row
        var modeRow = new VisualElement();
        modeRow.style.flexDirection = FlexDirection.Row;
        modeRow.style.justifyContent = Justify.SpaceBetween;
        modeRow.style.marginBottom = 8;

        _driveModeBtn = CreateModeButton("DRIVE", OnDriveModeClicked);
        _seatModeBtn = CreateModeButton("SEAT", OnSeatModeClicked);

        modeRow.Add(_driveModeBtn);
        modeRow.Add(_seatModeBtn);
        _modeContainer.Add(modeRow);

        // Confidence indicator
        var confidenceRow = new VisualElement();
        confidenceRow.style.flexDirection = FlexDirection.Row;
        confidenceRow.style.alignItems = Align.Center;
        confidenceRow.style.marginTop = 4;

        _confidenceIndicator = new VisualElement();
        _confidenceIndicator.style.width = 10;
        _confidenceIndicator.style.height = 10;
        _confidenceIndicator.style.borderTopLeftRadius = 5;
        _confidenceIndicator.style.borderTopRightRadius = 5;
        _confidenceIndicator.style.borderBottomLeftRadius = 5;
        _confidenceIndicator.style.borderBottomRightRadius = 5;
        _confidenceIndicator.style.marginRight = 6;
        _confidenceIndicator.style.backgroundColor = new StyleColor(driveActiveColor);

        _confidenceLabel = new Label("Synced");
        _confidenceLabel.style.color = new StyleColor(Color.black);
        _confidenceLabel.style.fontSize = 11;

        confidenceRow.Add(_confidenceIndicator);
        confidenceRow.Add(_confidenceLabel);
        _modeContainer.Add(confidenceRow);

        // Insert before MacroButtonsContainer
        if (macroContainer != null)
        {
            int index = leftMenu.IndexOf(macroContainer);
            leftMenu.Insert(index, _modeContainer);
        }
        else
        {
            leftMenu.Add(_modeContainer);
        }

        // Create actuator selector (hidden by default)
        CreateActuatorSelector(macroContainer);

        // Create sync panel (hidden by default)
        CreateSyncPanel();
    }

    private Button CreateModeButton(string text, System.Action onClick)
    {
        var btn = new Button(onClick);
        btn.text = text;
        btn.style.flexGrow = 1;
        btn.style.height = 44;
        btn.style.fontSize = 14;
        btn.style.unityFontStyleAndWeight = FontStyle.Bold;
        btn.style.marginLeft = 2;
        btn.style.marginRight = 2;
        btn.style.borderTopLeftRadius = 6;
        btn.style.borderTopRightRadius = 6;
        btn.style.borderBottomLeftRadius = 6;
        btn.style.borderBottomRightRadius = 6;
        btn.style.borderTopWidth = 2;
        btn.style.borderBottomWidth = 2;
        btn.style.borderLeftWidth = 2;
        btn.style.borderRightWidth = 2;
        return btn;
    }

    private void CreateActuatorSelector(VisualElement insertBefore)
    {
        _actuatorSelector = new VisualElement { name = "ActuatorSelector" };
        _actuatorSelector.style.backgroundColor = new StyleColor(new Color(1, 1, 1, 0.95f));
        _actuatorSelector.style.borderTopLeftRadius = 8;
        _actuatorSelector.style.borderTopRightRadius = 8;
        _actuatorSelector.style.borderBottomLeftRadius = 8;
        _actuatorSelector.style.borderBottomRightRadius = 8;
        _actuatorSelector.style.paddingTop = 10;
        _actuatorSelector.style.paddingBottom = 10;
        _actuatorSelector.style.paddingLeft = 8;
        _actuatorSelector.style.paddingRight = 8;
        _actuatorSelector.style.marginBottom = 10;
        _actuatorSelector.style.display = DisplayStyle.None;

        // Header
        var header = new Label("Select Actuator");
        header.style.color = new StyleColor(Color.black);
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 8;
        _actuatorSelector.Add(header);

        // Direction grid (mimics joystick directions)
        //       [Up]
        // [Left]    [Right]
        //      [Down]
        var gridContainer = new VisualElement();
        gridContainer.style.alignItems = Align.Center;

        // Top row (Up)
        var topRow = new VisualElement();
        topRow.style.marginBottom = 4;
        var upBtn = CreateDirectionButton("up", "Back Rest");
        topRow.Add(upBtn);
        gridContainer.Add(topRow);

        // Middle row (Left, Right)
        var middleRow = new VisualElement();
        middleRow.style.flexDirection = FlexDirection.Row;
        middleRow.style.justifyContent = Justify.Center;
        var leftBtn = CreateDirectionButton("left", "Tilt");
        var rightBtn = CreateDirectionButton("right", "Leg");
        leftBtn.style.marginRight = 30;
        rightBtn.style.marginLeft = 30;
        middleRow.Add(leftBtn);
        middleRow.Add(rightBtn);
        gridContainer.Add(middleRow);

        // Bottom row (Down)
        var bottomRow = new VisualElement();
        bottomRow.style.marginTop = 4;
        var downBtn = CreateDirectionButton("down", "Lift");
        bottomRow.Add(downBtn);
        gridContainer.Add(bottomRow);

        _actuatorSelector.Add(gridContainer);

        // Selected actuator label
        _selectedActuatorLabel = new Label("No actuator selected");
        _selectedActuatorLabel.style.color = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
        _selectedActuatorLabel.style.fontSize = 11;
        _selectedActuatorLabel.style.marginTop = 10;
        _selectedActuatorLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        _actuatorSelector.Add(_selectedActuatorLabel);

        // Store buttons for later updates
        _actuatorButtons = new Button[] { upBtn, leftBtn, rightBtn, downBtn };

        // Insert in left menu
        var leftMenu = _root.Q<VisualElement>("LeftMenu");
        if (insertBefore != null && leftMenu != null)
        {
            int index = leftMenu.IndexOf(insertBefore);
            leftMenu.Insert(index, _actuatorSelector);
        }
    }

    private Button CreateDirectionButton(string direction, string label)
    {
        var btn = new Button(() => OnActuatorDirectionClicked(direction));
        btn.name = $"ActuatorBtn_{direction}";
        btn.text = label;
        btn.style.width = 50;
        btn.style.height = 50;
        btn.style.fontSize = 10;
        btn.style.unityFontStyleAndWeight = FontStyle.Bold;
        btn.style.backgroundColor = new StyleColor(seatActiveColor);
        btn.style.color = new StyleColor(Color.white);
        btn.style.borderTopLeftRadius = 25;
        btn.style.borderTopRightRadius = 25;
        btn.style.borderBottomLeftRadius = 25;
        btn.style.borderBottomRightRadius = 25;
        return btn;
    }

    private void CreateSyncPanel()
    {
        _syncPanel = new VisualElement { name = "SyncPanel" };
        _syncPanel.style.position = Position.Absolute;
        _syncPanel.style.top = 0;
        _syncPanel.style.left = 0;
        _syncPanel.style.right = 0;
        _syncPanel.style.bottom = 0;
        _syncPanel.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.7f));
        _syncPanel.style.justifyContent = Justify.Center;
        _syncPanel.style.alignItems = Align.Center;
        _syncPanel.style.display = DisplayStyle.None;

        var panel = new VisualElement();
        panel.style.backgroundColor = new StyleColor(Color.white);
        panel.style.borderTopLeftRadius = 12;
        panel.style.borderTopRightRadius = 12;
        panel.style.borderBottomLeftRadius = 12;
        panel.style.borderBottomRightRadius = 12;
        panel.style.paddingTop = 20;
        panel.style.paddingBottom = 20;
        panel.style.paddingLeft = 30;
        panel.style.paddingRight = 30;
        panel.style.maxWidth = 350;

        var title = new Label("State May Be Out of Sync");
        title.style.fontSize = 18;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new StyleColor(dangerColor);
        title.style.marginBottom = 15;
        title.style.unityTextAlign = TextAnchor.MiddleCenter;
        panel.Add(title);

        var message = new Label("The wheelchair state might not match what the app shows.\nPlease choose an option:");
        message.style.fontSize = 14;
        message.style.color = new StyleColor(Color.black);
        message.style.marginBottom = 20;
        message.style.unityTextAlign = TextAnchor.MiddleCenter;
        message.style.whiteSpace = WhiteSpace.Normal;
        panel.Add(message);

        // Power cycle button
        _powerCycleBtn = new Button(OnPowerCycleClicked);
        _powerCycleBtn.text = "I Power Cycled the Chair";
        _powerCycleBtn.style.height = 50;
        _powerCycleBtn.style.fontSize = 14;
        _powerCycleBtn.style.marginBottom = 10;
        _powerCycleBtn.style.backgroundColor = new StyleColor(driveActiveColor);
        _powerCycleBtn.style.color = new StyleColor(Color.white);
        _powerCycleBtn.style.borderTopLeftRadius = 8;
        _powerCycleBtn.style.borderTopRightRadius = 8;
        _powerCycleBtn.style.borderBottomLeftRadius = 8;
        _powerCycleBtn.style.borderBottomRightRadius = 8;
        panel.Add(_powerCycleBtn);

        // Verify button
        _verifyStateBtn = new Button(OnVerifyStateClicked);
        _verifyStateBtn.text = "Chair Matches App Display";
        _verifyStateBtn.style.height = 50;
        _verifyStateBtn.style.fontSize = 14;
        _verifyStateBtn.style.marginBottom = 10;
        _verifyStateBtn.style.backgroundColor = new StyleColor(seatActiveColor);
        _verifyStateBtn.style.color = new StyleColor(Color.white);
        _verifyStateBtn.style.borderTopLeftRadius = 8;
        _verifyStateBtn.style.borderTopRightRadius = 8;
        _verifyStateBtn.style.borderBottomLeftRadius = 8;
        _verifyStateBtn.style.borderBottomRightRadius = 8;
        panel.Add(_verifyStateBtn);

        // Dismiss button
        var dismissBtn = new Button(HideSyncPanel);
        dismissBtn.text = "Dismiss";
        dismissBtn.style.height = 40;
        dismissBtn.style.fontSize = 12;
        dismissBtn.style.backgroundColor = new StyleColor(inactiveColor);
        dismissBtn.style.color = new StyleColor(Color.white);
        dismissBtn.style.borderTopLeftRadius = 8;
        dismissBtn.style.borderTopRightRadius = 8;
        dismissBtn.style.borderBottomLeftRadius = 8;
        dismissBtn.style.borderBottomRightRadius = 8;
        panel.Add(dismissBtn);

        _syncPanel.Add(panel);
        _root.Add(_syncPanel);
    }

    // Event handlers
    private void OnDriveModeClicked()
    {
        modeController?.SwitchToDriveMode();
    }

    private void OnSeatModeClicked()
    {
        modeController?.SwitchToSeatMode();
    }

    private void OnActuatorDirectionClicked(string direction)
    {
        modeController?.SelectActuatorByDirection(direction);
    }

    private void OnPowerCycleClicked()
    {
        wheelchairState?.OnPowerCycleConfirmed();
        HideSyncPanel();
    }

    private void OnVerifyStateClicked()
    {
        wheelchairState?.OnStateVerified();
        HideSyncPanel();
    }

    // State update handlers
    private void OnModeChanged(WheelchairState.WheelchairMode mode)
    {
        UpdateModeDisplay();
    }

    private void OnActuatorSelected(string actuatorId)
    {
        UpdateActuatorDisplay();
    }

    private void OnConfidenceChanged(WheelchairState.ConfidenceLevel confidence)
    {
        UpdateConfidenceDisplay();
    }

    private void OnProfileLoaded()
    {
        UpdateActuatorButtons();
    }

    private void UpdateModeDisplay()
    {
        if (wheelchairState == null) return;

        bool isDrive = wheelchairState.CurrentMode == WheelchairState.WheelchairMode.Drive;

        // Update Drive button
        _driveModeBtn.style.backgroundColor = new StyleColor(isDrive ? driveActiveColor : inactiveColor);
        _driveModeBtn.style.color = new StyleColor(Color.white);
        _driveModeBtn.style.borderTopColor = new StyleColor(isDrive ? driveActiveColor : Color.clear);
        _driveModeBtn.style.borderBottomColor = new StyleColor(isDrive ? driveActiveColor : Color.clear);
        _driveModeBtn.style.borderLeftColor = new StyleColor(isDrive ? driveActiveColor : Color.clear);
        _driveModeBtn.style.borderRightColor = new StyleColor(isDrive ? driveActiveColor : Color.clear);

        // Update Seat button
        _seatModeBtn.style.backgroundColor = new StyleColor(!isDrive ? seatActiveColor : inactiveColor);
        _seatModeBtn.style.color = new StyleColor(Color.white);
        _seatModeBtn.style.borderTopColor = new StyleColor(!isDrive ? seatActiveColor : Color.clear);
        _seatModeBtn.style.borderBottomColor = new StyleColor(!isDrive ? seatActiveColor : Color.clear);
        _seatModeBtn.style.borderLeftColor = new StyleColor(!isDrive ? seatActiveColor : Color.clear);
        _seatModeBtn.style.borderRightColor = new StyleColor(!isDrive ? seatActiveColor : Color.clear);

        // Show/hide actuator selector
        if (_actuatorSelector != null)
        {
            _actuatorSelector.style.display = isDrive ? DisplayStyle.None : DisplayStyle.Flex;
        }

        UpdateConfidenceDisplay();
    }

    private void UpdateActuatorDisplay()
    {
        if (wheelchairState == null || modeController == null) return;

        var selected = modeController.GetSelectedActuator();
        if (selected != null)
        {
            _selectedActuatorLabel.text = $"Selected: {selected.label}";
            _selectedActuatorLabel.style.color = new StyleColor(seatActiveColor);
        }
        else
        {
            _selectedActuatorLabel.text = "Tap a direction to select";
            _selectedActuatorLabel.style.color = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
        }

        // Highlight selected button
        if (_actuatorButtons != null)
        {
            foreach (var btn in _actuatorButtons)
            {
                string dir = btn.name.Replace("ActuatorBtn_", "");
                bool isSelected = selected != null && selected.selectionDirection.ToLower() == dir;

                btn.style.borderTopWidth = isSelected ? 3 : 0;
                btn.style.borderBottomWidth = isSelected ? 3 : 0;
                btn.style.borderLeftWidth = isSelected ? 3 : 0;
                btn.style.borderRightWidth = isSelected ? 3 : 0;
                btn.style.borderTopColor = new StyleColor(Color.white);
                btn.style.borderBottomColor = new StyleColor(Color.white);
                btn.style.borderLeftColor = new StyleColor(Color.white);
                btn.style.borderRightColor = new StyleColor(Color.white);
            }
        }
    }

    private void UpdateConfidenceDisplay()
    {
        if (wheelchairState == null) return;

        var confidence = wheelchairState.Confidence;
        Color color;
        string text;

        switch (confidence)
        {
            case WheelchairState.ConfidenceLevel.High:
                color = driveActiveColor;
                text = "Synced";
                break;
            case WheelchairState.ConfidenceLevel.Medium:
                color = warningColor;
                text = "Likely synced";
                break;
            case WheelchairState.ConfidenceLevel.Low:
                color = dangerColor;
                text = "May need sync";
                break;
            default:
                color = inactiveColor;
                text = "";
                break;
        }

        _confidenceIndicator.style.backgroundColor = new StyleColor(color);
        _confidenceLabel.text = text;
        _confidenceLabel.style.color = new StyleColor(Color.black);
    }

    private void UpdateActuatorButtons()
    {
        if (modeController == null) return;

        var actuators = modeController.GetAllActuators();
        if (_actuatorButtons == null || actuators == null) return;

        // Update button labels based on profile
        foreach (var actuator in actuators)
        {
            string dir = actuator.selectionDirection.ToLower();
            foreach (var btn in _actuatorButtons)
            {
                if (btn.name == $"ActuatorBtn_{dir}")
                {
                    btn.text = actuator.label.Length > 6
                        ? actuator.label.Substring(0, 6)
                        : actuator.label;
                    break;
                }
            }
        }
    }

    public void ShowSyncPanel()
    {
        if (_syncPanel != null)
        {
            _syncPanel.style.display = DisplayStyle.Flex;
        }
    }

    public void HideSyncPanel()
    {
        if (_syncPanel != null)
        {
            _syncPanel.style.display = DisplayStyle.None;
        }
    }
}
