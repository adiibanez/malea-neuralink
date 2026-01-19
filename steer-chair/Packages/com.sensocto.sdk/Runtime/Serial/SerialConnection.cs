using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
using System.IO.Ports;
#endif

namespace Sensocto.SDK
{
    /// <summary>
    /// Connection state for serial port.
    /// </summary>
    public enum SerialConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Error
    }

    /// <summary>
    /// Manages serial port connection with auto-reconnect and exponential backoff.
    /// Thread-safe for use with background I/O threads.
    /// </summary>
    public class SerialConnection : IDisposable
    {
        // Configuration
        private readonly string _configuredPort;
        private readonly int _baudRate;
        private readonly bool _autoReconnect;
        private readonly int _maxReconnectAttempts;

        // Current port (may change on reconnect if auto-detected)
        private string _currentPort;

        // Serial port - platform specific
#if UNITY_EDITOR
        private SerialPort _serialPort;
#else
        private NativeSerialPort _serialPort;
#endif

        // Thread safety
        private readonly object _lock = new object();
        private bool _disposed;

        // Reconnection state
        private int _reconnectAttempts;
        private int _consecutiveErrors;
        private float _lastReconnectAttempt;
        private Stopwatch _stopwatch;

        // Constants
        private const int BASE_RECONNECT_DELAY_MS = 100;
        private const int MAX_RECONNECT_DELAY_MS = 5000;
        private const int ERRORS_BEFORE_RECONNECT = 3;

        // Events
        /// <summary>
        /// Fired when connection state changes.
        /// </summary>
        public event Action<SerialConnectionState> OnStateChanged;

        /// <summary>
        /// Fired when successfully connected.
        /// </summary>
        public event Action<string> OnConnected;

        /// <summary>
        /// Fired when disconnected.
        /// </summary>
        public event Action<string> OnDisconnected;

        /// <summary>
        /// Fired when reconnection attempt starts.
        /// </summary>
        public event Action<int, int> OnReconnecting; // (attempt, maxAttempts)

        /// <summary>
        /// Fired when an error occurs.
        /// </summary>
        public event Action<string> OnError;

        /// <summary>
        /// Fired when data is received (if using read thread).
        /// </summary>
        public event Action<byte[]> OnDataReceived;

        /// <summary>
        /// Fired when a line is received (if using read thread).
        /// </summary>
        public event Action<string> OnLineReceived;

        // Properties
        /// <summary>
        /// Current connection state.
        /// </summary>
        public SerialConnectionState State { get; private set; } = SerialConnectionState.Disconnected;

        /// <summary>
        /// Whether the serial port is currently connected and open.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                lock (_lock)
                {
                    return _serialPort != null && _serialPort.IsOpen;
                }
            }
        }

        /// <summary>
        /// The currently connected port name.
        /// </summary>
        public string PortName => _currentPort;

        /// <summary>
        /// Number of reconnection attempts made.
        /// </summary>
        public int ReconnectAttempts => _reconnectAttempts;

        /// <summary>
        /// Creates a new SerialConnection with the specified settings.
        /// </summary>
        /// <param name="port">Port name (e.g., "COM3" or "/dev/cu.usbmodem123"). If null/empty, auto-detects.</param>
        /// <param name="baudRate">Baud rate (default: 115200).</param>
        /// <param name="autoReconnect">Whether to auto-reconnect on disconnect (default: true).</param>
        /// <param name="maxReconnectAttempts">Maximum reconnection attempts (default: 10).</param>
        public SerialConnection(
            string port = null,
            int baudRate = 115200,
            bool autoReconnect = true,
            int maxReconnectAttempts = 10)
        {
            _configuredPort = port;
            _baudRate = baudRate;
            _autoReconnect = autoReconnect;
            _maxReconnectAttempts = maxReconnectAttempts;
            _stopwatch = Stopwatch.StartNew();
        }

        /// <summary>
        /// Attempts to connect to the serial port.
        /// </summary>
        /// <returns>True if connection succeeded.</returns>
        public bool Connect()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SerialConnection));

            lock (_lock)
            {
                // Clean up existing connection first
                CloseInternal();

                SetState(SerialConnectionState.Connecting);

                try
                {
                    // Determine port to use
                    _currentPort = _configuredPort;

                    // Try config file first
                    if (string.IsNullOrEmpty(_currentPort))
                    {
                        _currentPort = LoadPortFromConfig();
                    }

                    // Auto-detect if still empty
                    if (string.IsNullOrEmpty(_currentPort))
                    {
                        _currentPort = SerialPortUtility.GetSerialPort();
                    }

                    if (string.IsNullOrEmpty(_currentPort))
                    {
                        Debug.LogWarning("[SerialConnection] No serial port available");
                        SetState(SerialConnectionState.Error);
                        OnError?.Invoke("No serial port available");
                        return false;
                    }

#if UNITY_EDITOR
                    _serialPort = new SerialPort(_currentPort, _baudRate)
                    {
                        ReadTimeout = 500,
                        WriteTimeout = 500,
                        DtrEnable = true,
                        RtsEnable = true
                    };
                    _serialPort.Open();
#else
                    _serialPort = new NativeSerialPort(_currentPort, _baudRate);
#endif

                    _reconnectAttempts = 0;
                    _consecutiveErrors = 0;

                    SetState(SerialConnectionState.Connected);
                    Debug.Log($"[SerialConnection] Connected to {_currentPort}");
                    OnConnected?.Invoke(_currentPort);

                    return true;
                }
                catch (Exception e)
                {
                    _serialPort = null;
                    SetState(SerialConnectionState.Error);
                    Debug.LogWarning($"[SerialConnection] Connection failed: {e.Message}");
                    OnError?.Invoke(e.Message);
                    return false;
                }
            }
        }

        /// <summary>
        /// Disconnects from the serial port.
        /// </summary>
        public void Disconnect()
        {
            lock (_lock)
            {
                CloseInternal();
                _reconnectAttempts = 0;
                SetState(SerialConnectionState.Disconnected);
                OnDisconnected?.Invoke(_currentPort);
            }
        }

        /// <summary>
        /// Writes data to the serial port.
        /// </summary>
        /// <param name="data">String data to write.</param>
        /// <returns>True if write succeeded.</returns>
        public bool Write(string data)
        {
            lock (_lock)
            {
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    HandleWriteError("Port not open");
                    return false;
                }

                try
                {
                    _serialPort.Write(data);
                    _consecutiveErrors = 0;
                    return true;
                }
                catch (TimeoutException)
                {
                    HandleWriteError("Write timeout");
                    return false;
                }
                catch (Exception e)
                {
                    HandleWriteError(e.Message);
                    CloseInternal();
                    return false;
                }
            }
        }

        /// <summary>
        /// Writes byte data to the serial port.
        /// </summary>
        /// <param name="data">Byte array to write.</param>
        /// <returns>True if write succeeded.</returns>
        public bool Write(byte[] data)
        {
            lock (_lock)
            {
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    HandleWriteError("Port not open");
                    return false;
                }

                try
                {
#if UNITY_EDITOR
                    _serialPort.Write(data, 0, data.Length);
#else
                    _serialPort.Write(data);
#endif
                    _consecutiveErrors = 0;
                    return true;
                }
                catch (TimeoutException)
                {
                    HandleWriteError("Write timeout");
                    return false;
                }
                catch (Exception e)
                {
                    HandleWriteError(e.Message);
                    CloseInternal();
                    return false;
                }
            }
        }

        /// <summary>
        /// Reads available data from the serial port.
        /// </summary>
        /// <param name="buffer">Buffer to read into.</param>
        /// <returns>Number of bytes read.</returns>
        public int Read(byte[] buffer)
        {
            lock (_lock)
            {
                if (_serialPort == null || !_serialPort.IsOpen)
                    return 0;

                try
                {
#if UNITY_EDITOR
                    return _serialPort.Read(buffer, 0, buffer.Length);
#else
                    return _serialPort.Read(buffer);
#endif
                }
                catch
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// Reads a line from the serial port.
        /// </summary>
        /// <returns>The line read, or null if nothing available.</returns>
        public string ReadLine()
        {
            lock (_lock)
            {
                if (_serialPort == null || !_serialPort.IsOpen)
                    return null;

                try
                {
#if UNITY_EDITOR
                    return _serialPort.ReadLine();
#else
                    return _serialPort.ReadLine();
#endif
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Attempts to reconnect with exponential backoff.
        /// Call this periodically from a background thread or update loop when not connected.
        /// </summary>
        /// <returns>True if reconnection succeeded.</returns>
        public bool TryReconnect()
        {
            if (!_autoReconnect || _disposed)
                return false;

            if (_reconnectAttempts >= _maxReconnectAttempts)
            {
                if (State != SerialConnectionState.Error)
                {
                    SetState(SerialConnectionState.Error);
                    OnError?.Invoke($"Max reconnection attempts ({_maxReconnectAttempts}) reached");
                }
                return false;
            }

            float now = (float)_stopwatch.Elapsed.TotalSeconds;

            // Calculate delay with exponential backoff
            int delayMs = Math.Min(BASE_RECONNECT_DELAY_MS * (1 << _reconnectAttempts), MAX_RECONNECT_DELAY_MS);
            float delaySec = delayMs / 1000f;

            if (now - _lastReconnectAttempt < delaySec)
                return false; // Not enough time has passed

            _lastReconnectAttempt = now;
            _reconnectAttempts++;

            SetState(SerialConnectionState.Reconnecting);
            OnReconnecting?.Invoke(_reconnectAttempts, _maxReconnectAttempts);

            Debug.Log($"[SerialConnection] Reconnect attempt {_reconnectAttempts}/{_maxReconnectAttempts} (delay was {delayMs}ms)");

            lock (_lock)
            {
                CloseInternal();

                try
                {
                    // Re-detect port in case it changed
                    string detectedPort = SerialPortUtility.GetSerialPort();
                    if (!string.IsNullOrEmpty(detectedPort))
                    {
                        _currentPort = detectedPort;
                    }

                    if (string.IsNullOrEmpty(_currentPort))
                    {
                        return false;
                    }

#if UNITY_EDITOR
                    _serialPort = new SerialPort(_currentPort, _baudRate)
                    {
                        ReadTimeout = 500,
                        WriteTimeout = 500,
                        DtrEnable = true,
                        RtsEnable = true
                    };
                    _serialPort.Open();
#else
                    _serialPort = new NativeSerialPort(_currentPort, _baudRate);
#endif

                    _reconnectAttempts = 0;
                    _consecutiveErrors = 0;

                    SetState(SerialConnectionState.Connected);
                    Debug.Log($"[SerialConnection] Reconnected to {_currentPort}");
                    OnConnected?.Invoke(_currentPort);

                    return true;
                }
                catch (Exception e)
                {
                    _serialPort = null;
                    Debug.LogWarning($"[SerialConnection] Reconnect attempt {_reconnectAttempts} failed: {e.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks if reconnection should be attempted based on error count.
        /// </summary>
        public bool ShouldReconnect()
        {
            return !IsConnected || _consecutiveErrors >= ERRORS_BEFORE_RECONNECT;
        }

        /// <summary>
        /// Resets the reconnection attempt counter.
        /// </summary>
        public void ResetReconnectAttempts()
        {
            _reconnectAttempts = 0;
        }

        private void HandleWriteError(string message)
        {
            _consecutiveErrors++;
            if (_consecutiveErrors >= ERRORS_BEFORE_RECONNECT)
            {
                Debug.LogWarning($"[SerialConnection] {_consecutiveErrors} consecutive errors, will attempt reconnect");
            }
        }

        private void CloseInternal()
        {
            if (_serialPort != null)
            {
                try
                {
                    if (_serialPort.IsOpen)
                    {
#if UNITY_EDITOR
                        _serialPort.Close();
#else
                        _serialPort.Close();
#endif
                    }
#if UNITY_EDITOR
                    _serialPort.Dispose();
#else
                    _serialPort.Dispose();
#endif
                }
                catch { }
                _serialPort = null;
            }
        }

        private void SetState(SerialConnectionState newState)
        {
            if (State != newState)
            {
                State = newState;
                OnStateChanged?.Invoke(newState);
            }
        }

        private string LoadPortFromConfig()
        {
            try
            {
                string configPath = Path.Combine(Application.streamingAssetsPath, "serial_port.txt");
                if (File.Exists(configPath))
                {
                    string port = File.ReadAllText(configPath).Trim();
                    if (!string.IsNullOrEmpty(port) && !port.StartsWith("#"))
                    {
                        Debug.Log($"[SerialConnection] Using port from config: {port}");
                        return port;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SerialConnection] Could not read serial_port.txt: {e.Message}");
            }
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_lock)
            {
                CloseInternal();
            }
        }
    }
}
