using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Tracks and displays the current operating mode (Driving vs Seat Adjustment).
/// Mode is switched via the Mode button which activates relay 5 for 5 seconds.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class ModeIndicator : MonoBehaviour
{
    public enum OperatingMode
    {
        Driving,
        SeatAdjustment
    }

    [Header("Colors")]
    [SerializeField] private Color drivingColor = new Color(0.30f, 0.69f, 0.31f); // Green
    [SerializeField] private Color seatAdjustmentColor = new Color(0.13f, 0.59f, 0.95f); // Blue

    [Header("Labels")]
    [SerializeField] private string drivingLabel = "Driving";
    [SerializeField] private string seatAdjustmentLabel = "Seat Adjustment";

    private VisualElement _root;
    private VisualElement _modeIndicator;
    private Label _modeLabel;
    private OperatingMode _currentMode = OperatingMode.Driving;

    public OperatingMode CurrentMode => _currentMode;

    public delegate void ModeChangedHandler(OperatingMode newMode);
    public event ModeChangedHandler OnModeChanged;

    void OnEnable()
    {
        StartCoroutine(InitializeDelayed());
    }

    private IEnumerator InitializeDelayed()
    {
        yield return null;

        _root = GetComponent<UIDocument>().rootVisualElement;
        if (_root == null) yield break;

        _modeIndicator = _root.Q<VisualElement>(DriveUIElementNames.ModeIndicator);
        _modeLabel = _root.Q<Label>(DriveUIElementNames.ModeLabel);

        if (_modeIndicator == null)
        {
            Debug.LogWarning("[ModeIndicator] ModeIndicator element not found");
        }
        if (_modeLabel == null)
        {
            Debug.LogWarning("[ModeIndicator] ModeLabel element not found");
        }

        // Set initial state
        UpdateDisplay();

        Debug.Log("[ModeIndicator] Initialized");
    }

    /// <summary>
    /// Sets the current operating mode and updates the display.
    /// </summary>
    public void SetMode(OperatingMode mode)
    {
        if (_currentMode != mode)
        {
            _currentMode = mode;
            UpdateDisplay();
            OnModeChanged?.Invoke(mode);
            Debug.Log($"[ModeIndicator] Mode changed to: {mode}");
        }
    }

    /// <summary>
    /// Toggles between Driving and Seat Adjustment modes.
    /// </summary>
    public void ToggleMode()
    {
        SetMode(_currentMode == OperatingMode.Driving
            ? OperatingMode.SeatAdjustment
            : OperatingMode.Driving);
    }

    /// <summary>
    /// Temporarily switches to a mode for a specified duration, then returns to the original mode.
    /// </summary>
    public void TemporaryModeSwitch(OperatingMode temporaryMode, float duration)
    {
        StartCoroutine(TemporaryModeSwitchCoroutine(temporaryMode, duration));
    }

    private IEnumerator TemporaryModeSwitchCoroutine(OperatingMode temporaryMode, float duration)
    {
        var originalMode = _currentMode;
        SetMode(temporaryMode);
        yield return new WaitForSeconds(duration);
        SetMode(originalMode);
    }

    private void UpdateDisplay()
    {
        if (_modeIndicator != null)
        {
            Color indicatorColor = _currentMode == OperatingMode.Driving
                ? drivingColor
                : seatAdjustmentColor;
            _modeIndicator.style.backgroundColor = new StyleColor(indicatorColor);
        }

        if (_modeLabel != null)
        {
            string labelText = _currentMode == OperatingMode.Driving
                ? drivingLabel
                : seatAdjustmentLabel;
            _modeLabel.text = labelText;
        }
    }

    /// <summary>
    /// Checks if the system is in driving mode.
    /// </summary>
    public bool IsDrivingMode => _currentMode == OperatingMode.Driving;

    /// <summary>
    /// Checks if the system is in seat adjustment mode.
    /// </summary>
    public bool IsSeatAdjustmentMode => _currentMode == OperatingMode.SeatAdjustment;
}
