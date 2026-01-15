using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using UnityEngine;

/// <summary>
/// Handles macro/sequence execution for steering commands.
/// Loads configuration from JSON and provides methods to execute command sequences.
/// Macro format: "S[speed]D[direction]R[robot],P[pauseMs],..."
/// Example: "S50D31R1,P500,S31D31R1" - speed 50 dir 31 robot 1, wait 500ms, then neutral
/// </summary>
public class MacroController : MonoBehaviour
{
    [Header("Serial Settings")]
    [SerializeField] private string serialPort = "/dev/cu.usbmodem11101";
    [SerializeField] private int baudRate = 115200;
    [Tooltip("If set, MacroController will use JoystickController's serial port instead of opening its own")]
    [SerializeField] private JoystickController sharedSerialSource;

    [Header("Configuration")]
    [SerializeField] private string configFileName = "MacroConfig.json";

    [Header("Debug")]
    [SerializeField] private bool logCommands = true;

    private SerialPort _serialPort;
    private bool _usingSharedSerial = false;
    private MacroConfig _config;
    private Coroutine _currentMacro;
    private bool _isExecuting;

    public bool IsExecuting => _isExecuting;
    public MacroConfig Config => _config;
    public event Action<string, string> OnMacroStarted;  // id, label
    public event Action<string, string> OnMacroCompleted; // id, label
    public event Action<string> OnCommandSent;

    void Awake()
    {
        LoadConfig();
    }

    void Start()
    {
        ConnectSerial();
    }

    /// <summary>
    /// Loads macro configuration from JSON file in StreamingAssets.
    /// </summary>
    public void LoadConfig()
    {
        string path = Path.Combine(Application.streamingAssetsPath, configFileName);
        Debug.Log($"[MacroController] Loading config from: {path}");

        if (!File.Exists(path))
        {
            Debug.LogError($"[MacroController] Config file not found: {path}");
            _config = new MacroConfig();
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            Debug.Log($"[MacroController] JSON content length: {json.Length}");

            _config = JsonUtility.FromJson<MacroConfig>(json);

            if (_config == null)
            {
                Debug.LogError("[MacroController] JsonUtility returned null");
                _config = new MacroConfig();
                return;
            }

            if (_config.actuators == null)
            {
                _config.actuators = new ActuatorConfig[0];
            }

            if (_config.actions == null)
            {
                _config.actions = new ActionConfig[0];
            }

            Debug.Log($"[MacroController] Loaded {_config.actuators.Length} actuators and {_config.actions.Length} actions");

            foreach (var act in _config.actuators)
            {
                Debug.Log($"[MacroController] - Actuator: {act.id} = '{act.label}'");
            }

            foreach (var action in _config.actions)
            {
                Debug.Log($"[MacroController] - Action: {action.id} = '{action.label}' macro='{action.macro}'");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MacroController] Failed to load config: {e.Message}\n{e.StackTrace}");
            _config = new MacroConfig();
        }
    }

    /// <summary>
    /// Connects to the serial port for sending commands.
    /// Uses shared serial from JoystickController if available to avoid port conflicts.
    /// </summary>
    private void ConnectSerial()
    {
        // Try to use shared serial port from JoystickController first
        if (sharedSerialSource == null)
        {
            sharedSerialSource = FindFirstObjectByType<JoystickController>();
        }

        if (sharedSerialSource != null && sharedSerialSource.SharedSerialPort != null)
        {
            _serialPort = sharedSerialSource.SharedSerialPort;
            _usingSharedSerial = true;
            Debug.Log("[MacroController] Using shared serial port from JoystickController");
            return;
        }

        // Fall back to opening our own connection
        // Auto-detect serial port
        string detectedPort = SerialPortUtility.GetSerialPort();
        if (!string.IsNullOrEmpty(detectedPort))
        {
            Debug.Log($"[MacroController] Auto-detected serial port: {detectedPort}");
            serialPort = detectedPort;
        }

        try
        {
            _serialPort = new SerialPort(serialPort, baudRate)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true
            };
            _serialPort.Open();
            _usingSharedSerial = false;
            Debug.Log($"[MacroController] Serial connected on {serialPort}");
        }
        catch (Exception e)
        {
            _serialPort = null;
            Debug.LogWarning($"[MacroController] Serial connection failed: {e.Message}");
        }
    }

    /// <summary>
    /// Executes a macro string with an identifier.
    /// </summary>
    public void ExecuteMacro(string id, string label, string macro)
    {
        if (_isExecuting)
        {
            Debug.LogWarning("[MacroController] Macro already executing, stopping current...");
            StopCurrentMacro();
        }

        _currentMacro = StartCoroutine(ExecuteMacroCoroutine(id, label, macro));
    }

    /// <summary>
    /// Executes an actuator's increase macro.
    /// </summary>
    public void ExecuteActuatorIncrease(ActuatorConfig actuator)
    {
        ExecuteMacro(actuator.id + "_increase", actuator.label + " +", actuator.increaseMacro);
    }

    /// <summary>
    /// Executes an actuator's decrease macro.
    /// </summary>
    public void ExecuteActuatorDecrease(ActuatorConfig actuator)
    {
        ExecuteMacro(actuator.id + "_decrease", actuator.label + " -", actuator.decreaseMacro);
    }

    /// <summary>
    /// Executes an action's macro.
    /// </summary>
    public void ExecuteAction(ActionConfig action)
    {
        ExecuteMacro(action.id, action.label, action.macro);
    }

    /// <summary>
    /// Executes a raw macro string directly.
    /// </summary>
    public void ExecuteMacroString(string macro, string label = "Direct")
    {
        ExecuteMacro("direct", label, macro);
    }

    /// <summary>
    /// Stops the currently executing macro and sends neutral command.
    /// </summary>
    public void StopCurrentMacro()
    {
        if (_currentMacro != null)
        {
            StopCoroutine(_currentMacro);
            _currentMacro = null;
        }
        _isExecuting = false;

        // Send neutral command
        SendNeutral();
    }

    /// <summary>
    /// Sends the neutral (stop) command.
    /// </summary>
    public void SendNeutral()
    {
        int neutral = _config?.settings?.neutralSpeed ?? 31;
        int robot = _config?.settings?.defaultRobot ?? 8;
        SendCommand(neutral, neutral, robot);
    }

    /// <summary>
    /// Sends a single steering command.
    /// </summary>
    public void SendCommand(int speed, int direction, int robot = 8)
    {
        speed = Mathf.Clamp(speed, 0, 63);
        direction = Mathf.Clamp(direction, 0, 63);
        robot = Mathf.Clamp(robot, 1, 8);

        string cmd = $"S{speed:D2}D{direction:D2}R{robot}";
        SendRawCommand(cmd);
    }

    /// <summary>
    /// Sends a raw command string to serial.
    /// </summary>
    public void SendRawCommand(string cmd)
    {
        if (_serialPort != null && _serialPort.IsOpen)
        {
            try
            {
                _serialPort.Write(cmd);
                if (logCommands)
                {
                    Debug.Log($"[MacroController] Sent: {cmd}");
                }
                OnCommandSent?.Invoke(cmd);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MacroController] Send failed: {e.Message}");
            }
        }
        else if (logCommands)
        {
            Debug.Log($"[MacroController] Would send (no serial): {cmd}");
            OnCommandSent?.Invoke(cmd);
        }
    }

    /// <summary>
    /// Coroutine that executes a macro sequence.
    /// </summary>
    private IEnumerator ExecuteMacroCoroutine(string id, string label, string macro)
    {
        _isExecuting = true;
        OnMacroStarted?.Invoke(id, label);

        if (logCommands)
        {
            Debug.Log($"[MacroController] Starting macro '{label}': {macro}");
        }

        var commands = ParseMacro(macro);
        int delayMs = _config?.settings?.commandDelayMs ?? 50;

        foreach (var cmd in commands)
        {
            if (!_isExecuting) break;

            if (cmd.isPause)
            {
                if (logCommands)
                {
                    Debug.Log($"[MacroController] Pausing {cmd.pauseMs}ms");
                }
                yield return new WaitForSeconds(cmd.pauseMs / 1000f);
            }
            else
            {
                SendCommand(cmd.speed, cmd.direction, cmd.robot);
                yield return new WaitForSeconds(delayMs / 1000f);
            }
        }

        _isExecuting = false;
        OnMacroCompleted?.Invoke(id, label);

        if (logCommands)
        {
            Debug.Log($"[MacroController] Completed macro '{label}'");
        }
    }

    /// <summary>
    /// Parses a macro string into a list of commands.
    /// Format: "S[speed]D[direction]R[robot],P[pauseMs],..."
    /// </summary>
    private List<MacroCommand> ParseMacro(string macro)
    {
        var commands = new List<MacroCommand>();

        if (string.IsNullOrEmpty(macro))
            return commands;

        string[] parts = macro.Split(',');

        foreach (string part in parts)
        {
            string trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var cmd = new MacroCommand();

            // Check for pause command
            if (trimmed.StartsWith("P", StringComparison.OrdinalIgnoreCase))
            {
                cmd.isPause = true;
                if (int.TryParse(trimmed.Substring(1), out int pauseMs))
                {
                    cmd.pauseMs = pauseMs;
                }
                commands.Add(cmd);
                continue;
            }

            // Parse steering command: S##D##R#
            try
            {
                int sIndex = trimmed.IndexOf('S', StringComparison.OrdinalIgnoreCase);
                int dIndex = trimmed.IndexOf('D', StringComparison.OrdinalIgnoreCase);
                int rIndex = trimmed.IndexOf('R', StringComparison.OrdinalIgnoreCase);

                if (sIndex >= 0 && dIndex > sIndex)
                {
                    string speedStr = trimmed.Substring(sIndex + 1, dIndex - sIndex - 1);
                    cmd.speed = int.Parse(speedStr);
                }

                if (dIndex >= 0)
                {
                    int endIndex = rIndex > dIndex ? rIndex : trimmed.Length;
                    string dirStr = trimmed.Substring(dIndex + 1, endIndex - dIndex - 1);
                    cmd.direction = int.Parse(dirStr);
                }

                if (rIndex >= 0)
                {
                    string robotStr = trimmed.Substring(rIndex + 1);
                    cmd.robot = int.Parse(robotStr);
                }
                else
                {
                    cmd.robot = _config?.settings?.defaultRobot ?? 8;
                }

                cmd.isPause = false;
                commands.Add(cmd);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MacroController] Failed to parse command '{trimmed}': {e.Message}");
            }
        }

        return commands;
    }

    /// <summary>
    /// Gets all configured actuators.
    /// </summary>
    public ActuatorConfig[] GetActuators()
    {
        return _config?.actuators ?? new ActuatorConfig[0];
    }

    /// <summary>
    /// Gets all configured action buttons.
    /// </summary>
    public ActionConfig[] GetActions()
    {
        return _config?.actions ?? new ActionConfig[0];
    }

    /// <summary>
    /// Finds an actuator by ID.
    /// </summary>
    public ActuatorConfig FindActuatorById(string id)
    {
        if (_config?.actuators == null) return null;

        foreach (var actuator in _config.actuators)
        {
            if (actuator.id == id)
                return actuator;
        }
        return null;
    }

    /// <summary>
    /// Finds an action by ID.
    /// </summary>
    public ActionConfig FindActionById(string id)
    {
        if (_config?.actions == null) return null;

        foreach (var action in _config.actions)
        {
            if (action.id == id)
                return action;
        }
        return null;
    }

    void OnDestroy()
    {
        StopCurrentMacro();

        // Only close the port if we own it (not shared)
        if (!_usingSharedSerial && _serialPort != null && _serialPort.IsOpen)
        {
            _serialPort.Close();
            Debug.Log("[MacroController] Serial connection closed.");
        }
    }

    void OnApplicationQuit()
    {
        StopCurrentMacro();

        // Only close the port if we own it (not shared)
        if (!_usingSharedSerial && _serialPort != null && _serialPort.IsOpen)
        {
            _serialPort.Close();
        }
    }
}

/// <summary>
/// Represents a single command in a macro sequence.
/// </summary>
[Serializable]
public struct MacroCommand
{
    public bool isPause;
    public int pauseMs;
    public int speed;
    public int direction;
    public int robot;
}

/// <summary>
/// Root configuration object loaded from JSON.
/// </summary>
[Serializable]
public class MacroConfig
{
    public ActuatorConfig[] actuators = new ActuatorConfig[0];
    public ActionConfig[] actions = new ActionConfig[0];
    public MacroSettings settings = new MacroSettings();
}

/// <summary>
/// Configuration for an actuator with +/- controls.
/// </summary>
[Serializable]
public class ActuatorConfig
{
    public string id;
    public string label;
    public string increaseLabel = "+";
    public string decreaseLabel = "-";
    public string increaseMacro;
    public string decreaseMacro;
}

/// <summary>
/// Configuration for a single action button.
/// </summary>
[Serializable]
public class ActionConfig
{
    public string id;
    public string label;
    public string macro;
    public string color;
}

/// <summary>
/// Global macro settings.
/// </summary>
[Serializable]
public class MacroSettings
{
    public int defaultRobot = 8;
    public int neutralSpeed = 31;
    public int neutralDirection = 31;
    public int commandDelayMs = 50;
}
