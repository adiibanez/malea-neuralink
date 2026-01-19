using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Controls wheelchair mode switching and actuator selection via joystick macros.
/// Actuators are selected by executing macros that mirror the physical joystick sequence.
/// Designed for Eoin's wheelchair control system.
/// Only active in DriveChair scene.
/// </summary>
public class ModeController : MonoBehaviour
{
    private const string DRIVE_CHAIR_SCENE = "DriveChair";
    [Header("References")]
    [SerializeField] private MacroController macroController;
    [SerializeField] private WheelchairState wheelchairState;

    [Header("Configuration")]
    [SerializeField] private string configFileName = "WheelchairProfile.json";

    private WheelchairProfile _profile;
    private Dictionary<string, SeatActuatorConfig> _actuatorsByDirection;

    public WheelchairProfile Profile => _profile;
    public event Action OnProfileLoaded;
    public event Action<SeatActuatorConfig> OnActuatorSelected;

    void Awake()
    {
        // Only initialize in DriveChair scene
        if (SceneManager.GetActiveScene().name != DRIVE_CHAIR_SCENE)
        {
            enabled = false;
            return;
        }

        if (macroController == null)
            macroController = FindFirstObjectByType<MacroController>();

        if (wheelchairState == null)
            wheelchairState = GetComponent<WheelchairState>();

        if (wheelchairState == null)
            wheelchairState = gameObject.AddComponent<WheelchairState>();

        LoadProfile();
    }

    /// <summary>
    /// Loads the wheelchair profile from JSON configuration.
    /// </summary>
    public void LoadProfile()
    {
        string path = Path.Combine(Application.streamingAssetsPath, configFileName);
        Debug.Log($"[ModeController] Loading profile from: {path}");

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[ModeController] Profile not found, creating default: {path}");
            _profile = CreateDefaultProfile();
            SaveProfile();
        }
        else
        {
            try
            {
                string json = File.ReadAllText(path);
                _profile = JsonUtility.FromJson<WheelchairProfile>(json);

                if (_profile == null)
                {
                    Debug.LogError("[ModeController] Failed to parse profile, using default");
                    _profile = CreateDefaultProfile();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ModeController] Error loading profile: {e.Message}");
                _profile = CreateDefaultProfile();
            }
        }

        BuildActuatorLookup();
        OnProfileLoaded?.Invoke();

        Debug.Log($"[ModeController] Profile loaded: {_profile.profileName}, {_profile.seatMode.actuators.Length} actuators");
    }

    /// <summary>
    /// Saves the current profile to JSON.
    /// </summary>
    public void SaveProfile()
    {
        string path = Path.Combine(Application.streamingAssetsPath, configFileName);
        try
        {
            string json = JsonUtility.ToJson(_profile, true);
            File.WriteAllText(path, json);
            Debug.Log($"[ModeController] Profile saved to: {path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ModeController] Failed to save profile: {e.Message}");
        }
    }

    private void BuildActuatorLookup()
    {
        _actuatorsByDirection = new Dictionary<string, SeatActuatorConfig>();

        if (_profile?.seatMode?.actuators == null) return;

        foreach (var actuator in _profile.seatMode.actuators)
        {
            _actuatorsByDirection[actuator.selectionDirection.ToLower()] = actuator;
        }
    }

    /// <summary>
    /// Switches to Drive mode by executing the mode switch macro.
    /// </summary>
    public void SwitchToDriveMode()
    {
        if (wheelchairState.CurrentMode == WheelchairState.WheelchairMode.Drive)
        {
            Debug.Log("[ModeController] Already in Drive mode");
            return;
        }

        string macro = _profile?.driveMode?.enterMacro;
        if (string.IsNullOrEmpty(macro))
        {
            // Default: send neutral to robot 8
            macro = "S31D31R8";
        }

        Debug.Log($"[ModeController] Switching to Drive mode with macro: {macro}");
        macroController?.ExecuteMacroString(macro, "Enter Drive Mode");
        wheelchairState.SetDriveMode();
    }

    /// <summary>
    /// Switches to Seat mode by executing the mode switch macro.
    /// </summary>
    public void SwitchToSeatMode()
    {
        if (wheelchairState.CurrentMode == WheelchairState.WheelchairMode.Seat)
        {
            Debug.Log("[ModeController] Already in Seat mode");
            return;
        }

        string macro = _profile?.seatMode?.enterMacro;
        if (string.IsNullOrEmpty(macro))
        {
            Debug.LogWarning("[ModeController] No seat mode enter macro configured");
            return;
        }

        Debug.Log($"[ModeController] Switching to Seat mode with macro: {macro}");
        macroController?.ExecuteMacroString(macro, "Enter Seat Mode");
        wheelchairState.SetSeatMode();
    }

    /// <summary>
    /// Selects an actuator by direction (mirrors joystick movement).
    /// </summary>
    public void SelectActuatorByDirection(string direction)
    {
        if (wheelchairState.CurrentMode != WheelchairState.WheelchairMode.Seat)
        {
            Debug.LogWarning("[ModeController] Cannot select actuator - not in Seat mode");
            return;
        }

        direction = direction.ToLower();
        if (!_actuatorsByDirection.TryGetValue(direction, out var actuator))
        {
            Debug.LogWarning($"[ModeController] No actuator mapped to direction: {direction}");
            return;
        }

        SelectActuator(actuator);
    }

    /// <summary>
    /// Selects a specific actuator by executing its selection macro.
    /// </summary>
    public void SelectActuator(SeatActuatorConfig actuator)
    {
        if (string.IsNullOrEmpty(actuator.selectionMacro))
        {
            Debug.LogWarning($"[ModeController] No selection macro for actuator: {actuator.label}");
            return;
        }

        Debug.Log($"[ModeController] Selecting actuator '{actuator.label}' with macro: {actuator.selectionMacro}");
        macroController?.ExecuteMacroString(actuator.selectionMacro, $"Select {actuator.label}");
        wheelchairState.SelectActuator(actuator.id);
        OnActuatorSelected?.Invoke(actuator);
    }

    /// <summary>
    /// Selects an actuator by its ID.
    /// </summary>
    public void SelectActuatorById(string actuatorId)
    {
        var actuator = GetActuatorById(actuatorId);
        if (actuator != null)
        {
            SelectActuator(actuator);
        }
    }

    /// <summary>
    /// Increases the currently selected actuator.
    /// </summary>
    public void IncreaseSelectedActuator()
    {
        var actuator = GetSelectedActuator();
        if (actuator == null)
        {
            Debug.LogWarning("[ModeController] No actuator selected");
            return;
        }

        if (string.IsNullOrEmpty(actuator.increaseMacro))
        {
            Debug.LogWarning($"[ModeController] No increase macro for actuator: {actuator.label}");
            return;
        }

        Debug.Log($"[ModeController] Increasing {actuator.label}");
        macroController?.ExecuteMacroString(actuator.increaseMacro, $"{actuator.label} +");
        wheelchairState.IncrementCommandCount();
    }

    /// <summary>
    /// Decreases the currently selected actuator.
    /// </summary>
    public void DecreaseSelectedActuator()
    {
        var actuator = GetSelectedActuator();
        if (actuator == null)
        {
            Debug.LogWarning("[ModeController] No actuator selected");
            return;
        }

        if (string.IsNullOrEmpty(actuator.decreaseMacro))
        {
            Debug.LogWarning($"[ModeController] No decrease macro for actuator: {actuator.label}");
            return;
        }

        Debug.Log($"[ModeController] Decreasing {actuator.label}");
        macroController?.ExecuteMacroString(actuator.decreaseMacro, $"{actuator.label} -");
        wheelchairState.IncrementCommandCount();
    }

    /// <summary>
    /// Gets the currently selected actuator configuration.
    /// </summary>
    public SeatActuatorConfig GetSelectedActuator()
    {
        string selectedId = wheelchairState.SelectedActuatorId;
        if (string.IsNullOrEmpty(selectedId)) return null;

        return GetActuatorById(selectedId);
    }

    /// <summary>
    /// Gets an actuator by its ID.
    /// </summary>
    public SeatActuatorConfig GetActuatorById(string id)
    {
        if (_profile?.seatMode?.actuators == null) return null;

        foreach (var actuator in _profile.seatMode.actuators)
        {
            if (actuator.id == id)
                return actuator;
        }
        return null;
    }

    /// <summary>
    /// Gets all configured seat actuators.
    /// </summary>
    public SeatActuatorConfig[] GetAllActuators()
    {
        return _profile?.seatMode?.actuators ?? new SeatActuatorConfig[0];
    }

    private WheelchairProfile CreateDefaultProfile()
    {
        return new WheelchairProfile
        {
            profileName = "Eoin's Chair",
            powerOnMode = "drive",

            driveMode = new DriveModeConfig
            {
                robot = 8,
                enterMacro = "S31D31R8",
                description = "Wheelchair movement control"
            },

            seatMode = new SeatModeConfig
            {
                enterMacro = "S31D63R8,P300,S31D31R8",
                description = "Seat adjustment mode",
                actuators = new SeatActuatorConfig[]
                {
                    new SeatActuatorConfig
                    {
                        id = "back_rest",
                        label = "Back Rest",
                        robot = 3,
                        selectionDirection = "up",
                        selectionMacro = "S63D31R8,P200,S31D31R8",
                        increaseMacro = "S63D31R3,P200,S31D31R3",
                        decreaseMacro = "S00D31R3,P200,S31D31R3",
                        icon = "arrow_up"
                    },
                    new SeatActuatorConfig
                    {
                        id = "seat_lift",
                        label = "Seat Lift",
                        robot = 2,
                        selectionDirection = "down",
                        selectionMacro = "S00D31R8,P200,S31D31R8",
                        increaseMacro = "S63D31R2,P200,S31D31R2",
                        decreaseMacro = "S00D31R2,P200,S31D31R2",
                        icon = "arrow_down"
                    },
                    new SeatActuatorConfig
                    {
                        id = "seat_tilt",
                        label = "Seat Tilt",
                        robot = 1,
                        selectionDirection = "left",
                        selectionMacro = "S31D00R8,P200,S31D31R8",
                        increaseMacro = "S63D31R1,P200,S31D31R1",
                        decreaseMacro = "S00D31R1,P200,S31D31R1",
                        icon = "arrow_left"
                    },
                    new SeatActuatorConfig
                    {
                        id = "leg_rest",
                        label = "Leg Rest",
                        robot = 4,
                        selectionDirection = "right",
                        selectionMacro = "S31D63R8,P200,S31D31R8",
                        increaseMacro = "S63D31R4,P200,S31D31R4",
                        decreaseMacro = "S00D31R4,P200,S31D31R4",
                        icon = "arrow_right"
                    }
                }
            },

            recovery = new RecoveryConfig
            {
                powerCycleResetsToMode = "drive",
                lowConfidenceThreshold = 10,
                mediumConfidenceThreshold = 5
            }
        };
    }
}

// ============================================
// Configuration Data Classes
// ============================================

[Serializable]
public class WheelchairProfile
{
    public string profileName;
    public string powerOnMode;  // "drive" or "seat"
    public DriveModeConfig driveMode;
    public SeatModeConfig seatMode;
    public RecoveryConfig recovery;
}

[Serializable]
public class DriveModeConfig
{
    public int robot;
    public string enterMacro;
    public string description;
}

[Serializable]
public class SeatModeConfig
{
    public string enterMacro;
    public string description;
    public SeatActuatorConfig[] actuators;
}

[Serializable]
public class SeatActuatorConfig
{
    public string id;
    public string label;
    public int robot;
    public string selectionDirection;  // up, down, left, right
    public string selectionMacro;      // Macro to select this actuator
    public string increaseMacro;       // Macro to increase/extend
    public string decreaseMacro;       // Macro to decrease/retract
    public string icon;                // Icon hint for UI
}

[Serializable]
public class RecoveryConfig
{
    public string powerCycleResetsToMode;
    public int lowConfidenceThreshold;
    public int mediumConfidenceThreshold;
}
