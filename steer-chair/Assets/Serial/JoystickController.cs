using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using UnityEngine;
using Sensocto;
using Debug = UnityEngine.Debug;

/// <summary>
/// Abstracts serial communication to send joystick-like commands based on input values.
/// Maps X/Y inputs (e.g., from head pose) to speed/direction values for the joystick.
/// Handles all communication asynchronously in a background thread.
/// Implements IMoveReceiver to integrate with DriveEvents movement broadcasting.
/// </summary>
public class JoystickController : MonoBehaviour, IMoveReceiver
{
    [Header("Serial Settings")]
    [SerializeField] private string serialPort = "/dev/cu.usbmodem11101";
    [SerializeField] private int baudRate = 115200;
    
    [Header("Timing")]
    [SerializeField] private float updateInterval = 0.14f;
    [SerializeField] private float idleTimeout = 0.3f;
    
    [Header("Debug")]
    [SerializeField] private bool logCommands = true;
    [SerializeField] private bool logInput = false;
    
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

    // Serial
    private SerialPort ser;
    private Thread sendThread;
    private volatile bool running;
    
    // Thread-safe input buffer
    private readonly object inputLock = new object();

    /// <summary>
    /// Exposes the serial port for sharing with other controllers (e.g., MacroController).
    /// </summary>
    public SerialPort SharedSerialPort => ser;

    void Awake()
    {
        // Initialize stopwatch early so it's available if Move() is called before Start()
        stopwatch = Stopwatch.StartNew();
        lastInputTime = (float)stopwatch.Elapsed.TotalSeconds;
        lastSendTime = (float)stopwatch.Elapsed.TotalSeconds;

        Debug.Log($"[JoystickController] Awake: stopwatch started, lastInputTime={lastInputTime:F3}, lastSendTime={lastSendTime:F3}");

        // Auto-detect serial port if not manually set or if default doesn't exist
        string detectedPort = SerialPortUtility.GetSerialPort();
        if (!string.IsNullOrEmpty(detectedPort))
        {
            Debug.Log($"[JoystickController] Auto-detected serial port: {detectedPort} (configured: {serialPort})");
            serialPort = detectedPort;
        }

        // Connect serial in Awake so it's available for other controllers (e.g., MacroController) in Start()
        ConnectSerial();
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
    
    /// <summary>
    /// Attempts to establish serial connection.
    /// </summary>
    private void ConnectSerial()
    {
        try
        {
            ser = new SerialPort(serialPort, baudRate)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true
            };
            ser.Open();
            Debug.Log($"[JoystickController] Serial connected on {serialPort}");
        }
        catch (Exception e)
        {
            ser = null;
            Debug.LogError($"[JoystickController] Serial connection failed: {e.Message}");
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
                    if (ser != null && ser.IsOpen)
                    {
                        ser.Write(cmd);
                    }
                    lastSendTime = now;
                    
                    if (logCommands)
                    {
                        // Only log non-neutral commands to reduce noise, or log every 10th neutral command
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
                Debug.LogError($"[JoystickController] Error in send loop: {e.Message}");
                Thread.Sleep(100); // Longer sleep on error
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
        
        if (ser != null && ser.IsOpen)
        {
            ser.Close();
            Debug.Log("[JoystickController] Serial connection closed.");
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