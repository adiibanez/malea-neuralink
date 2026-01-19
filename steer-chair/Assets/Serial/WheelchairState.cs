using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages the assumed state of the wheelchair since we have one-way communication.
/// Tracks mode (Drive/Seat), selected actuator, and confidence level.
/// Designed for Eoin - clear state tracking with easy recovery.
/// Only active in DriveChair scene.
/// </summary>
public class WheelchairState : MonoBehaviour
{
    private const string DRIVE_CHAIR_SCENE = "DriveChair";
    public enum WheelchairMode
    {
        Drive,  // Robot 8 - moving the wheelchair
        Seat    // Robot 1-4 - adjusting seat actuators
    }

    public enum ConfidenceLevel
    {
        High,    // Recently synced or power cycled
        Medium,  // Several commands sent without confirmation
        Low      // Many commands sent - suggest power cycle
    }

    [Header("State")]
    [SerializeField] private WheelchairMode currentMode = WheelchairMode.Drive;
    [SerializeField] private string selectedActuatorId = "";
    [SerializeField] private int commandsSinceSync = 0;

    [Header("Confidence Thresholds")]
    [SerializeField] private int mediumThreshold = 5;
    [SerializeField] private int lowThreshold = 10;

    // Events for UI updates
    public event Action<WheelchairMode> OnModeChanged;
    public event Action<string> OnActuatorSelected;
    public event Action<ConfidenceLevel> OnConfidenceChanged;
    public event Action OnSyncRequested;

    // Properties
    public WheelchairMode CurrentMode => currentMode;
    public string SelectedActuatorId => selectedActuatorId;
    public ConfidenceLevel Confidence => GetConfidenceLevel();

    private ConfidenceLevel _lastConfidence = ConfidenceLevel.High;

    void Awake()
    {
        // Only initialize in DriveChair scene
        if (SceneManager.GetActiveScene().name != DRIVE_CHAIR_SCENE)
        {
            enabled = false;
            return;
        }

        // Power on assumption: wheelchair starts in Drive mode
        currentMode = WheelchairMode.Drive;
        commandsSinceSync = 0;
        selectedActuatorId = "";
    }

    /// <summary>
    /// Called when the user confirms they power cycled the wheelchair.
    /// Resets to known good state (Drive mode).
    /// </summary>
    public void OnPowerCycleConfirmed()
    {
        Debug.Log("[WheelchairState] Power cycle confirmed - resetting to Drive mode");

        currentMode = WheelchairMode.Drive;
        selectedActuatorId = "";
        commandsSinceSync = 0;

        OnModeChanged?.Invoke(currentMode);
        OnActuatorSelected?.Invoke(selectedActuatorId);
        CheckConfidenceChange();
    }

    /// <summary>
    /// Called when user visually confirms the current mode matches the app.
    /// </summary>
    public void OnStateVerified()
    {
        Debug.Log("[WheelchairState] State verified by user");
        commandsSinceSync = 0;
        CheckConfidenceChange();
    }

    /// <summary>
    /// Sets the mode to Drive and tracks the command.
    /// </summary>
    public void SetDriveMode()
    {
        if (currentMode != WheelchairMode.Drive)
        {
            currentMode = WheelchairMode.Drive;
            selectedActuatorId = "";
            OnModeChanged?.Invoke(currentMode);
            OnActuatorSelected?.Invoke(selectedActuatorId);
        }
        IncrementCommandCount();
    }

    /// <summary>
    /// Sets the mode to Seat adjustment and tracks the command.
    /// </summary>
    public void SetSeatMode()
    {
        if (currentMode != WheelchairMode.Seat)
        {
            currentMode = WheelchairMode.Seat;
            OnModeChanged?.Invoke(currentMode);
        }
        IncrementCommandCount();
    }

    /// <summary>
    /// Selects an actuator (only valid in Seat mode).
    /// </summary>
    public void SelectActuator(string actuatorId)
    {
        if (currentMode != WheelchairMode.Seat)
        {
            Debug.LogWarning($"[WheelchairState] Cannot select actuator in Drive mode");
            return;
        }

        if (selectedActuatorId != actuatorId)
        {
            selectedActuatorId = actuatorId;
            OnActuatorSelected?.Invoke(selectedActuatorId);
        }
        IncrementCommandCount();
    }

    /// <summary>
    /// Tracks that a command was sent to the wheelchair.
    /// </summary>
    public void IncrementCommandCount()
    {
        commandsSinceSync++;
        CheckConfidenceChange();
    }

    /// <summary>
    /// Gets the current confidence level based on commands sent.
    /// </summary>
    public ConfidenceLevel GetConfidenceLevel()
    {
        if (commandsSinceSync >= lowThreshold)
            return ConfidenceLevel.Low;
        if (commandsSinceSync >= mediumThreshold)
            return ConfidenceLevel.Medium;
        return ConfidenceLevel.High;
    }

    private void CheckConfidenceChange()
    {
        var newConfidence = GetConfidenceLevel();
        if (newConfidence != _lastConfidence)
        {
            _lastConfidence = newConfidence;
            OnConfidenceChanged?.Invoke(newConfidence);

            if (newConfidence == ConfidenceLevel.Low)
            {
                Debug.Log("[WheelchairState] Confidence is low - suggesting power cycle");
                OnSyncRequested?.Invoke();
            }
        }
    }

    /// <summary>
    /// User confirms the mode shown matches what they see on the wheelchair.
    /// </summary>
    public void ConfirmCurrentMode(WheelchairMode confirmedMode)
    {
        if (currentMode != confirmedMode)
        {
            currentMode = confirmedMode;
            OnModeChanged?.Invoke(currentMode);
        }
        commandsSinceSync = 0;
        CheckConfidenceChange();
    }

    /// <summary>
    /// Gets a user-friendly description of the current state.
    /// </summary>
    public string GetStateDescription()
    {
        string modeStr = currentMode == WheelchairMode.Drive ? "Drive Mode" : "Seat Adjustment";
        string actuatorStr = string.IsNullOrEmpty(selectedActuatorId) ? "" : $" ({selectedActuatorId})";
        string confidenceStr = Confidence switch
        {
            ConfidenceLevel.High => "Synced",
            ConfidenceLevel.Medium => "Likely synced",
            ConfidenceLevel.Low => "May need sync",
            _ => ""
        };
        return $"{modeStr}{actuatorStr} - {confidenceStr}";
    }
}
