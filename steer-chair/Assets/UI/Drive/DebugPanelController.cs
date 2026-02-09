using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Manages the debug overlay panel and relay 5/6 toggle buttons.
/// Shows telemetry data from JoystickController, MacroController, and DriveEvents.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class DebugPanelController : MonoBehaviour
{
    private const string DRIVE_CHAIR_SCENE = "DriveChair";
    private const long UPDATE_INTERVAL_MS = 100;

    private VisualElement _root;
    private VisualElement _debugPanel;
    private VisualElement _debugPanelContent;
    private Button _debugToggleBtn;
    private Button _relay5Btn;
    private Button _relay6Btn;

    // Bar-level serial status (always visible)
    private VisualElement _barStatusIndicator;
    private Label _barStatusLabel;
    private Label _barPortLabel;

    // Telemetry labels
    private Label _lblLastCmd;
    private Label _lblSpeed;
    private Label _lblDirection;
    private Label _lblIsIdle;
    private Label _lblIsConnected;
    private Label _lblPortName;
    private Label _lblReconnects;
    private Label _lblErrors;
    private Label _lblTimingMin;
    private Label _lblTimingMax;
    private Label _lblMacroState;
    private Label _lblMacroCmd;
    private Label _lblDriveState;

    // References
    private JoystickController _joystickController;
    private MacroController _macroController;
    private DriveEvents _driveEvents;

    // Relay toggle state
    private bool _relay5Active;
    private bool _relay6Active;
    private Coroutine _relay5Coroutine;
    private Coroutine _relay6Coroutine;
    private bool _panelVisible;

    // Scheduler handles
    private IVisualElementScheduledItem _updateSchedule;
    private IVisualElementScheduledItem _barStatusSchedule;

    void OnEnable()
    {
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

        if (SceneManager.GetActiveScene().name != DRIVE_CHAIR_SCENE)
            yield break;

        var uiDoc = GetComponent<UIDocument>();
        _root = uiDoc?.rootVisualElement;
        if (_root == null)
        {
            Debug.LogWarning("[DebugPanelController] rootVisualElement is null, aborting");
            yield break;
        }

        // Find controllers
        _joystickController = FindAnyObjectByType<JoystickController>();
        _macroController = FindAnyObjectByType<MacroController>();
        _driveEvents = GetComponent<DriveEvents>();

        // Find UI elements
        _debugPanel = _root.Q<VisualElement>(DriveUIElementNames.DebugPanel);
        _debugPanelContent = _root.Q<VisualElement>(DriveUIElementNames.DebugPanelContent);
        _debugToggleBtn = _root.Q<Button>(DriveUIElementNames.DebugToggleBtn);
        _relay5Btn = _root.Q<Button>(DriveUIElementNames.Relay5ToggleBtn);
        _relay6Btn = _root.Q<Button>(DriveUIElementNames.Relay6ToggleBtn);

        // Bar-level serial status (always visible)
        _barStatusIndicator = _root.Q<VisualElement>(DriveUIElementNames.SerialStatusIndicator);
        _barStatusLabel = _root.Q<Label>(DriveUIElementNames.SerialStatusLabel);
        _barPortLabel = _root.Q<Label>("DbgBarPort");

        // Find labels
        _lblLastCmd = _root.Q<Label>(DriveUIElementNames.DbgLastCmd);
        _lblSpeed = _root.Q<Label>(DriveUIElementNames.DbgSpeed);
        _lblDirection = _root.Q<Label>(DriveUIElementNames.DbgDirection);
        _lblIsIdle = _root.Q<Label>(DriveUIElementNames.DbgIsIdle);
        _lblIsConnected = _root.Q<Label>(DriveUIElementNames.DbgIsConnected);
        _lblPortName = _root.Q<Label>(DriveUIElementNames.DbgPortName);
        _lblReconnects = _root.Q<Label>(DriveUIElementNames.DbgReconnects);
        _lblErrors = _root.Q<Label>(DriveUIElementNames.DbgErrors);
        _lblTimingMin = _root.Q<Label>(DriveUIElementNames.DbgTimingMin);
        _lblTimingMax = _root.Q<Label>(DriveUIElementNames.DbgTimingMax);
        _lblMacroState = _root.Q<Label>(DriveUIElementNames.DbgMacroState);
        _lblMacroCmd = _root.Q<Label>(DriveUIElementNames.DbgMacroCmd);
        _lblDriveState = _root.Q<Label>(DriveUIElementNames.DbgDriveState);

        if (_debugPanel == null || _debugToggleBtn == null)
        {
            Debug.LogWarning("[DebugPanelController] Debug panel elements not found in UXML");
            yield break;
        }

        // Show the debug panel container (bar always visible)
        _debugPanel.style.display = DisplayStyle.Flex;

        // Register callbacks
        _debugToggleBtn.RegisterCallback<ClickEvent>(OnDebugToggle);

        if (_relay5Btn != null)
            _relay5Btn.RegisterCallback<ClickEvent>(OnRelay5Toggle);
        if (_relay6Btn != null)
            _relay6Btn.RegisterCallback<ClickEvent>(OnRelay6Toggle);

        // Setup scheduler for telemetry updates (starts paused)
        _updateSchedule = _debugPanelContent.schedule.Execute(UpdateTelemetry).Every(UPDATE_INTERVAL_MS);
        _updateSchedule.Pause();

        // Bar-level serial status updates always run
        _barStatusSchedule = _debugPanel.schedule.Execute(UpdateBarStatus).Every(500);

        Debug.Log("[DebugPanelController] Initialized");
    }

    void OnDisable()
    {
        // Safety: stop keep-alive coroutines and deactivate any active relays
        _relay5Active = false;
        _relay6Active = false;

        if (_relay5Coroutine != null)
        {
            StopCoroutine(_relay5Coroutine);
            _relay5Coroutine = null;
        }
        if (_relay6Coroutine != null)
        {
            StopCoroutine(_relay6Coroutine);
            _relay6Coroutine = null;
        }

        DeactivateRelay(5);
        DeactivateRelay(6);

        _updateSchedule?.Pause();
        _barStatusSchedule?.Pause();

        if (_debugToggleBtn != null)
            _debugToggleBtn.UnregisterCallback<ClickEvent>(OnDebugToggle);
        if (_relay5Btn != null)
            _relay5Btn.UnregisterCallback<ClickEvent>(OnRelay5Toggle);
        if (_relay6Btn != null)
            _relay6Btn.UnregisterCallback<ClickEvent>(OnRelay6Toggle);
    }

    private void OnDebugToggle(ClickEvent evt)
    {
        _panelVisible = !_panelVisible;

        if (_debugPanelContent != null)
            _debugPanelContent.style.display = _panelVisible ? DisplayStyle.Flex : DisplayStyle.None;

        if (_panelVisible)
            _updateSchedule?.Resume();
        else
            _updateSchedule?.Pause();

        Debug.Log($"[DebugPanelController] Panel {(_panelVisible ? "shown" : "hidden")}");
    }

    private void OnRelay5Toggle(ClickEvent evt)
    {
        _relay5Active = !_relay5Active;
        if (_relay5Active)
        {
            _relay5Btn?.AddToClassList("relay-toggle-active");
            _relay5Coroutine = StartCoroutine(RelayKeepAlive(5));
        }
        else
        {
            StopRelay(5, ref _relay5Coroutine, _relay5Btn);
        }
    }

    private void OnRelay6Toggle(ClickEvent evt)
    {
        _relay6Active = !_relay6Active;
        if (_relay6Active)
        {
            _relay6Btn?.AddToClassList("relay-toggle-active");
            _relay6Coroutine = StartCoroutine(RelayKeepAlive(6));
        }
        else
        {
            StopRelay(6, ref _relay6Coroutine, _relay6Btn);
        }
    }

    private IEnumerator RelayKeepAlive(int relayNumber)
    {
        int relayIndex = relayNumber - 1;
        string cmd = $"S31D31R{relayIndex}";
        Debug.Log($"[DebugPanelController] Relay {relayNumber} ON: {cmd}");

        bool isActive = relayNumber == 5 ? _relay5Active : _relay6Active;
        while (isActive)
        {
            if (_macroController != null)
                _macroController.SendRawCommand(cmd);
            yield return new WaitForSeconds(0.1f);
            isActive = relayNumber == 5 ? _relay5Active : _relay6Active;
        }
    }

    private void StopRelay(int relayNumber, ref Coroutine coroutine, Button btn)
    {
        if (coroutine != null)
        {
            StopCoroutine(coroutine);
            coroutine = null;
        }

        int relayIndex = relayNumber - 1;
        if (_macroController != null)
        {
            string cmd = $"S31D31R{relayIndex}";
            _macroController.SendRawCommand(cmd);
            Debug.Log($"[DebugPanelController] Relay {relayNumber} OFF: {cmd}");
        }

        btn?.RemoveFromClassList("relay-toggle-active");
    }

    private void DeactivateRelay(int relayNumber)
    {
        int relayIndex = relayNumber - 1;
        if (_macroController != null)
        {
            string cmd = $"S31D31R{relayIndex}";
            _macroController.SendRawCommand(cmd);
            Debug.Log($"[DebugPanelController] Relay {relayNumber} OFF: {cmd}");
        }
    }

    private void UpdateBarStatus()
    {
        if (_joystickController == null) return;

        bool connected = _joystickController.IsConnected;
        if (_barStatusIndicator != null)
            _barStatusIndicator.style.backgroundColor = connected
                ? new Color(0.2f, 0.8f, 0.2f)
                : new Color(0.8f, 0.2f, 0.2f);
        SetLabel(_barStatusLabel, connected ? "Connected" : "Disconnected");

        var t = _joystickController.GetTelemetry();
        SetLabel(_barPortLabel, string.IsNullOrEmpty(t.PortName) ? "" : t.PortName);
    }

    private void UpdateTelemetry()
    {
        // Joystick telemetry
        if (_joystickController != null)
        {
            var t = _joystickController.GetTelemetry();
            SetLabel(_lblLastCmd, $"Cmd: {t.LastCommand}");
            SetLabel(_lblSpeed, $"Speed: {t.Speed}");
            SetLabel(_lblDirection, $"Dir: {t.Direction}");
            SetLabel(_lblIsIdle, $"Idle: {t.IsIdle}");
            SetLabel(_lblIsConnected, $"Connected: {t.IsConnected}");
            SetLabel(_lblPortName, $"Port: {t.PortName}");
            SetLabel(_lblReconnects, $"Reconnects: {t.ReconnectAttempts}");
            SetLabel(_lblErrors, $"Errors: {t.ConsecutiveErrors}");
            SetLabel(_lblTimingMin, $"Tmin: {t.TimeDiffMin:F3}s");
            SetLabel(_lblTimingMax, $"Tmax: {t.TimeDiffMax:F3}s");
        }

        // Macro state
        if (_macroController != null)
        {
            var (isExec, label, lastCmd) = _macroController.GetDebugState();
            SetLabel(_lblMacroState, $"Macro: {(isExec ? label : "idle")}");
            SetLabel(_lblMacroCmd, $"MCmd: {lastCmd}");
        }

        // Drive state
        if (_driveEvents != null)
        {
            SetLabel(_lblDriveState, $"Drive: {_driveEvents.CurrentDriveState}");
        }
    }

    private static void SetLabel(Label lbl, string text)
    {
        if (lbl != null)
            lbl.text = text;
    }
}
