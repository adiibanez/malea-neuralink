using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
using System.IO.Ports;
#endif

/// <summary>
/// Abstracts serial communication to send joystick-like commands based on input values.
/// Maps X/Y inputs (e.g., from head pose) to speed/direction values for the joystick.
/// Handles all communication asynchronously in a background thread.
/// Implements IMoveReceiver to integrate with DriveEvents movement broadcasting.
/// </summary>
public class JoystickController : MonoBehaviour, IMoveReceiver
{
    [Header("Serial Settings")]
    [SerializeField] private string serialPort = "/dev/cu.usbmodem21101";
    [SerializeField] private int baudRate = 115200;

    [Header("Timing")]
    [SerializeField] private float updateInterval = 0.12f;
    [SerializeField] private float idleTimeout = 0.3f;

    [Header("Debug")]
    [SerializeField] private bool logCommands = true;
    [SerializeField] private bool logInput = false;

    /// <summary>
    /// Event fired when serial connection state changes.
    /// Parameter is true when connected, false when disconnected.
    /// </summary>
    public event Action<bool> OnSerialConnectionChanged;
    
    // Constants
    private const int NEUTRAL = 31;
    private const int MAX_VAL = 63;
    private const int MIN_VAL = 0;
    
    // State
    private int speed = NEUTRAL;
    private int direction = NEUTRAL;
    private float lastInputTime;
    private float lastSendTime;
    
    // Timing stats
    private float? timeDiffMin = null;
    private float timeDiffMax = 0f;

    // Thread-safe timing (Stopwatch works across threads unlike Time.realtimeSinceStartup)
    private Stopwatch stopwatch;

    // Serial - Use native implementation in builds, managed in Editor
#if UNITY_EDITOR
    private SerialPort ser;
#else
    private NativeSerialPort ser;
#endif
    private Thread sendThread;
    private volatile bool running;

    // Reconnection state
    private readonly object serialLock = new object();
    private int reconnectAttempts = 0;
    private const int MAX_RECONNECT_DELAY_MS = 5000;
    private const int BASE_RECONNECT_DELAY_MS = 100;
    private float lastReconnectAttempt = 0f;
    private int consecutiveErrors = 0;
    private const int ERRORS_BEFORE_RECONNECT = 3;

    // Connection state tracking for events
    private bool _lastKnownConnectionState = false;
    private readonly object _connectionStateLock = new object();

    // Thread-safe input buffer
    private readonly object inputLock = new object();

    /// <summary>
    /// Exposes the serial port for sharing with other controllers (e.g., MacroController).
    /// Note: The port may become null during reconnection. Callers should handle null gracefully.
    /// </summary>
#if UNITY_EDITOR
    public SerialPort SharedSerialPort
    {
        get
        {
            lock (serialLock)
            {
                return ser;
            }
        }
    }
#else
    public NativeSerialPort SharedSerialPort
    {
        get
        {
            lock (serialLock)
            {
                return ser;
            }
        }
    }
#endif

    /// <summary>
    /// Returns true if serial port is currently connected and open.
    /// </summary>
    public bool IsConnected => IsSerialConnected();

    void Awake()
    {
        // Initialize stopwatch early so it's available if Move() is called before Start()
        stopwatch = Stopwatch.StartNew();
        lastInputTime = (float)stopwatch.Elapsed.TotalSeconds;
        lastSendTime = (float)stopwatch.Elapsed.TotalSeconds;

        Debug.Log($"[JoystickController] Awake: isEditor={Application.isEditor}, platform={Application.platform}");

        // Try to load port from config file first (allows manual override)
        string configPort = LoadPortFromConfig();
        if (!string.IsNullOrEmpty(configPort))
        {
            Debug.Log($"[JoystickController] Using port from config file: {configPort}");
            serialPort = configPort;
        }
        else
        {
            // Auto-detect serial port
            string detectedPort = SerialPortUtility.GetSerialPort();
            if (!string.IsNullOrEmpty(detectedPort))
            {
                Debug.Log($"[JoystickController] Auto-detected serial port: {detectedPort}");
                serialPort = detectedPort;
            }
            else
            {
                Debug.LogWarning($"[JoystickController] No serial device auto-detected, using default: {serialPort}");
            }
        }

        // Connect serial in Awake so it's available for other controllers (e.g., MacroController) in Start()
        ConnectSerial();
    }

    /// <summary>
    /// Attempts to load serial port from a config file in StreamingAssets.
    /// This allows manual override when auto-detection fails in builds.
    /// Config file: StreamingAssets/serial_port.txt containing just the port path.
    /// </summary>
    private string LoadPortFromConfig()
    {
        try
        {
            string configPath = System.IO.Path.Combine(Application.streamingAssetsPath, "serial_port.txt");
            if (System.IO.File.Exists(configPath))
            {
                string port = System.IO.File.ReadAllText(configPath).Trim();
                if (!string.IsNullOrEmpty(port) && !port.StartsWith("#"))
                {
                    return port;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[JoystickController] Could not read serial_port.txt: {e.Message}");
        }
        return null;
    }

    void Start()
    {
        logCommands = true; // Force enable logging
        logInput = true;    // Force enable input logging for debugging

        float currentTime = (float)stopwatch.Elapsed.TotalSeconds;
        Debug.Log($"[JoystickController] Start: currentTime={currentTime:F3}, lastInputTime={lastInputTime:F3}, timeSinceAwake={(currentTime - lastInputTime):F3}s");

        running = true;
        sendThread = new Thread(SendLoop) { IsBackground = true };
        sendThread.Start();

        Debug.Log("[JoystickController] Start: SendLoop thread started");
    }

    void Update()
    {
        // Process pending connection state notifications on main thread
        if (_pendingConnectionNotification)
        {
            _pendingConnectionNotification = false;
            NotifyConnectionStateIfChanged(_pendingConnectionState);
        }
    }

    /// <summary>
    /// Attempts to establish serial connection. Thread-safe.
    /// </summary>
    /// <returns>True if connection succeeded, false otherwise.</returns>
    private bool ConnectSerial()
    {
        lock (serialLock)
        {
            // Clean up existing connection first
            if (ser != null)
            {
                try
                {
                    if (ser.IsOpen)
                        ser.Close();
                    ser.Dispose();
                }
                catch { }
                ser = null;
            }

            try
            {
                // Re-detect port in case it changed (device reconnected)
                string detectedPort = SerialPortUtility.GetSerialPort();
                if (!string.IsNullOrEmpty(detectedPort))
                {
                    serialPort = detectedPort;
                }

#if UNITY_EDITOR
                ser = new SerialPort(serialPort, baudRate)
                {
                    ReadTimeout = 500,
                    WriteTimeout = 500,
                    DtrEnable = true,
                    RtsEnable = true
                };
                ser.Open();
#else
                ser = new NativeSerialPort(serialPort, baudRate);
#endif

                reconnectAttempts = 0;
                consecutiveErrors = 0;
                Debug.Log($"[JoystickController] Serial connected on {serialPort}");
                QueueConnectionNotification(true);
                return true;
            }
            catch (Exception e)
            {
                ser = null;
                Debug.LogWarning($"[JoystickController] Serial connection failed: {e.GetType().Name}: {e.Message}");
                if (e.InnerException != null)
                {
                    Debug.LogWarning($"[JoystickController] Inner exception: {e.InnerException.GetType().Name}: {e.InnerException.Message}");
                }
                Debug.LogWarning($"[JoystickController] Stack trace: {e.StackTrace}");
                QueueConnectionNotification(false);
                return false;
            }
        }
    }

    /// <summary>
    /// Attempts to reconnect with exponential backoff. Called from SendLoop on errors.
    /// </summary>
    private void TryReconnect()
    {
        float now = (float)stopwatch.Elapsed.TotalSeconds;

        // Calculate delay with exponential backoff
        int delayMs = Math.Min(BASE_RECONNECT_DELAY_MS * (1 << reconnectAttempts), MAX_RECONNECT_DELAY_MS);
        float delaySec = delayMs / 1000f;

        if (now - lastReconnectAttempt < delaySec)
            return; // Not enough time has passed

        lastReconnectAttempt = now;
        reconnectAttempts++;

        Debug.Log($"[JoystickController] Attempting reconnect #{reconnectAttempts} (delay was {delayMs}ms)");

        if (ConnectSerial())
        {
            Debug.Log("[JoystickController] Reconnection successful!");
        }
    }

    /// <summary>
    /// Checks if serial port is connected and usable.
    /// </summary>
    private bool IsSerialConnected()
    {
        lock (serialLock)
        {
            return ser != null && ser.IsOpen;
        }
    }

    /// <summary>
    /// Notifies listeners if connection state has changed.
    /// Must be called from main thread for Unity event safety.
    /// </summary>
    private void NotifyConnectionStateIfChanged(bool currentState)
    {
        bool shouldNotify = false;
        lock (_connectionStateLock)
        {
            if (_lastKnownConnectionState != currentState)
            {
                _lastKnownConnectionState = currentState;
                shouldNotify = true;
            }
        }

        if (shouldNotify)
        {
            Debug.Log($"[JoystickController] Connection state changed: {(currentState ? "Connected" : "Disconnected")}");
            OnSerialConnectionChanged?.Invoke(currentState);
        }
    }

    /// <summary>
    /// Queues a connection state notification to be processed on the main thread.
    /// </summary>
    private volatile bool _pendingConnectionNotification = false;
    private volatile bool _pendingConnectionState = false;

    private void QueueConnectionNotification(bool connected)
    {
        _pendingConnectionState = connected;
        _pendingConnectionNotification = true;
    }

    /// <summary>
    /// Safely writes to serial port with error tracking.
    /// </summary>
    /// <returns>True if write succeeded, false otherwise.</returns>
    private bool SafeSerialWrite(string data)
    {
        lock (serialLock)
        {
            if (ser == null || !ser.IsOpen)
                return false;

            try
            {
                ser.Write(data);
                consecutiveErrors = 0;
                return true;
            }
            catch (TimeoutException)
            {
                consecutiveErrors++;
                if (consecutiveErrors >= ERRORS_BEFORE_RECONNECT)
                {
                    Debug.LogWarning($"[JoystickController] {consecutiveErrors} consecutive timeouts, will attempt reconnect");
                }
                return false;
            }
            catch (Exception e)
            {
                consecutiveErrors++;
                Debug.LogWarning($"[JoystickController] Serial write error: {e.Message}");

                // Connection is likely broken, close it so reconnect can happen
                try
                {
                    ser.Close();
                    ser.Dispose();
                }
                catch { }
                ser = null;

                QueueConnectionNotification(false);
                return false;
            }
        }
    }
    
    /// <summary>
    /// Background thread that continuously sends commands at the specified interval.
    /// </summary>
    private void SendLoop()
    {
        while (running)
        {
            try
            {
                float now = (float)stopwatch.Elapsed.TotalSeconds;

                // Check connection state and attempt reconnect if needed
                if (!IsSerialConnected() || consecutiveErrors >= ERRORS_BEFORE_RECONNECT)
                {
                    TryReconnect();

                    // If still not connected, sleep and continue
                    if (!IsSerialConnected())
                    {
                        Thread.Sleep(50);
                        continue;
                    }
                }

                int currentSpeed, currentDirection;
                float currentLastInputTime;

                lock (inputLock)
                {
                    currentSpeed = speed;
                    currentDirection = direction;
                    currentLastInputTime = lastInputTime;

                    // Check if idle timeout has been exceeded
                    float timeSinceInput = now - currentLastInputTime;
                    bool isIdle = timeSinceInput > idleTimeout;

                    // Log when we have non-neutral values (before potential reset)
                    if (currentSpeed != NEUTRAL || currentDirection != NEUTRAL)
                    {
                        Debug.Log($"[JoystickController] SendLoop READ: speed={currentSpeed} dir={currentDirection} " +
                                  $"now={now:F3} lastInput={currentLastInputTime:F3} timeSinceInput={timeSinceInput:F3}s isIdle={isIdle}");
                    }

                    if (isIdle)
                    {
                        if (currentSpeed != NEUTRAL || currentDirection != NEUTRAL)
                        {
                            Debug.Log($"[JoystickController] IDLE RESET: timeSinceInput={timeSinceInput:F3}s > {idleTimeout}s, resetting from S{currentSpeed}D{currentDirection} to neutral");
                        }
                        speed = NEUTRAL;
                        direction = NEUTRAL;
                        currentSpeed = NEUTRAL;
                        currentDirection = NEUTRAL;
                    }
                }

                string cmd = $"S{currentSpeed:D2}D{currentDirection:D2}R8";

                float timeDiff = now - lastSendTime;

                // Track timing stats
                if (timeDiffMin == null)
                    timeDiffMin = timeDiff;
                else
                    timeDiffMin = Mathf.Min(timeDiffMin.Value, timeDiff);

                timeDiffMax = Mathf.Max(timeDiffMax, timeDiff);

                if (timeDiff >= updateInterval)
                {
                    bool writeSuccess = SafeSerialWrite(cmd);
                    lastSendTime = now;

                    if (logCommands && writeSuccess)
                    {
                        bool isNonNeutral = currentSpeed != NEUTRAL || currentDirection != NEUTRAL;
                        if (isNonNeutral)
                        {
                            Debug.Log($"[JoystickController] SENT NON-NEUTRAL: {cmd} (now={now:F3} lastInput={currentLastInputTime:F3})");
                        }
                    }
                }

                Thread.Sleep(1); // 1ms sleep to prevent busy waiting
            }
            catch (Exception e)
            {
                // This catch is for unexpected errors not handled by SafeSerialWrite
                Debug.LogWarning($"[JoystickController] Unexpected error in send loop: {e.Message}");
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>
    /// IMoveReceiver implementation - receives movement commands from DriveEvents.
    /// </summary>
    public void Move(Vector2 direction)
    {
        Vector2 clampedDirection = new Vector2(
            Mathf.Clamp(direction.x, -1, 1),
            Mathf.Clamp(direction.y, -1, 1));

        Vector2 scaled = Vector2.Scale(new Vector2(30, 20), clampedDirection);

        UpdateInput(scaled.x, scaled.y * -1);

        if (logInput)
        {
            var (s, d) = GetCurrentValues();
            Debug.Log($"[JoystickController] Move: input=({direction.x:F2}, {direction.y:F2}) -> speed={s} dir={d} -> S{s:D2}D{d:D2}R8");
        }
    }
    
    /// <summary>
    /// Updates the internal speed and direction based on input values.
    /// </summary>
    /// <param name="directionInput">The direction value (e.g., head rotation left/right)</param>
    /// <param name="speedInput">The speed value (e.g., head tilt up/down)</param>
    /// <param name="dirMin">Minimum direction input range</param>
    /// <param name="dirMax">Maximum direction input range</param>
    /// <param name="speedMin">Minimum speed input range</param>
    /// <param name="speedMax">Maximum speed input range</param>
    private void UpdateInput(
        float directionInput,
        float speedInput,
        float dirMin = -30f,
        float dirMax = 30f,
        float speedMin = -20f,
        float speedMax = 20f)
    {
        lock (inputLock)
        {
            float newInputTime = (float)stopwatch.Elapsed.TotalSeconds;
            float oldInputTime = lastInputTime;
            lastInputTime = newInputTime;

            // Normalize and map direction (X-axis)
            float normalizedDirection = (directionInput - dirMin) / (dirMax - dirMin);
            int newDirection = (int)(MIN_VAL + normalizedDirection * (MAX_VAL - MIN_VAL));
            newDirection = Mathf.Clamp(newDirection, MIN_VAL, MAX_VAL);

            // Normalize and invert mapping: higher input -> lower output
            float normalizedSpeed = (speedInput - speedMin) / (speedMax - speedMin);
            float invertedSpeed = 1f - normalizedSpeed;
            int newSpeed = (int)(MIN_VAL + invertedSpeed * (MAX_VAL - MIN_VAL));
            newSpeed = Mathf.Clamp(newSpeed, MIN_VAL, MAX_VAL);

            int oldSpeed = speed;
            int oldDirection = direction;
            speed = newSpeed;
            direction = newDirection;

            Debug.Log($"[JoystickController] UpdateInput WRITE: speed {oldSpeed}->{newSpeed} dir {oldDirection}->{newDirection} " +
                      $"lastInputTime {oldInputTime:F3}->{newInputTime:F3}");
        }
    }
    
    /// <summary>
    /// Returns the current speed and direction values.
    /// </summary>
    public (int speed, int direction) GetCurrentValues()
    {
        lock (inputLock)
        {
            return (speed, direction);
        }
    }
    
    /// <summary>
    /// Closes the serial connection and stops the background thread.
    /// </summary>
    public void Close()
    {
        running = false;

        if (sendThread != null && sendThread.IsAlive)
        {
            sendThread.Join(1000); // Wait up to 1 second
        }

        lock (serialLock)
        {
            if (ser != null)
            {
                try
                {
                    if (ser.IsOpen)
                        ser.Close();
                    ser.Dispose();
                }
                catch { }
                ser = null;
                Debug.Log("[JoystickController] Serial connection closed.");
            }
        }
    }
    
    void OnDestroy()
    {
        Close();
    }
    
    void OnApplicationQuit()
    {
        Close();
    }
}