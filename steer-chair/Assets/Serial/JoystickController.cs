using System;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

/// <summary>
/// Abstracts serial communication to send joystick-like commands based on input values.
/// Maps X/Y inputs (e.g., from head pose) to speed/direction values for the joystick.
/// Handles all communication asynchronously in a background thread.
/// </summary>
public class JoystickController : MonoBehaviour
{
    [Header("Serial Settings")]
    [SerializeField] private string serialPort = "/dev/cu.usbmodem11101";
    [SerializeField] private int baudRate = 115200;
    
    [Header("Timing")]
    [SerializeField] private float updateInterval = 0.14f;
    [SerializeField] private float idleTimeout = 0.3f;
    
    [Header("Debug")]
    [SerializeField] private bool logCommands = false;
    
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
    
    // Serial
    private SerialPort ser;
    private Thread sendThread;
    private volatile bool running;
    
    // Thread-safe input buffer
    private readonly object inputLock = new object();
    
    void Start()
    {
        lastInputTime = Time.realtimeSinceStartup;
        lastSendTime = Time.realtimeSinceStartup;
        
        ConnectSerial();
        
        running = true;
        sendThread = new Thread(SendLoop) { IsBackground = true };
        sendThread.Start();
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
                float now = Time.realtimeSinceStartup;
                
                int currentSpeed, currentDirection;
                float currentLastInputTime;
                
                lock (inputLock)
                {
                    currentSpeed = speed;
                    currentDirection = direction;
                    currentLastInputTime = lastInputTime;
                    
                    // Check if idle timeout has been exceeded
                    if (now - currentLastInputTime > idleTimeout)
                    {
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
                        Debug.Log($"[JoystickController] Sent: {timeDiff * 1000:F2}ms " +
                                  $"{timeDiffMin.Value * 1000:F2}ms {timeDiffMax * 1000:F2}ms {cmd}");
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
    /// Updates the internal speed and direction based on input values.
    /// </summary>
    /// <param name="directionInput">The direction value (e.g., head rotation left/right)</param>
    /// <param name="speedInput">The speed value (e.g., head tilt up/down)</param>
    /// <param name="dirMin">Minimum direction input range</param>
    /// <param name="dirMax">Maximum direction input range</param>
    /// <param name="speedMin">Minimum speed input range</param>
    /// <param name="speedMax">Maximum speed input range</param>
    public void UpdateInput(
        float directionInput, 
        float speedInput,
        float dirMin = -30f, 
        float dirMax = 30f,
        float speedMin = -20f, 
        float speedMax = 20f)
    {
        lock (inputLock)
        {
            lastInputTime = Time.realtimeSinceStartup;
            
            // Normalize and map direction (X-axis)
            float normalizedDirection = (directionInput - dirMin) / (dirMax - dirMin);
            direction = (int)(MIN_VAL + normalizedDirection * (MAX_VAL - MIN_VAL));
            direction = Mathf.Clamp(direction, MIN_VAL, MAX_VAL);
            
            // Normalize and invert mapping: higher input -> lower output
            float normalizedSpeed = (speedInput - speedMin) / (speedMax - speedMin);
            float invertedSpeed = 1f - normalizedSpeed;
            
            speed = (int)(MIN_VAL + invertedSpeed * (MAX_VAL - MIN_VAL));
            speed = Mathf.Clamp(speed, MIN_VAL, MAX_VAL);
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