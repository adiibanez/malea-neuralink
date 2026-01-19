using UnityEngine;
using UnityEngine.UI;

namespace Sensocto.SDK
{
    /// <summary>
    /// UI component that displays serial connection status.
    /// Attach to a GameObject with an Image component for visual indicator,
    /// and optionally a Text/TMP_Text component for status text.
    /// </summary>
    public class SerialConnectionIndicator : MonoBehaviour
    {
        [Header("Serial Connection")]
        [Tooltip("Reference to the SerialConnection to monitor. If null, will try to find one.")]
        [SerializeField] private SerialConnectionMonitor connectionMonitor;

        [Header("UI Components")]
        [Tooltip("Image component for the status indicator (optional).")]
        [SerializeField] private Image statusImage;

        [Tooltip("Text component for status text (optional, supports both UI.Text and TMP).")]
        [SerializeField] private Component statusText;

        [Tooltip("Text component for port name (optional).")]
        [SerializeField] private Component portText;

        [Header("Colors")]
        [SerializeField] private Color connectedColor = new Color(0.2f, 0.8f, 0.2f); // Green
        [SerializeField] private Color disconnectedColor = new Color(0.8f, 0.2f, 0.2f); // Red
        [SerializeField] private Color connectingColor = new Color(0.8f, 0.8f, 0.2f); // Yellow
        [SerializeField] private Color reconnectingColor = new Color(0.8f, 0.5f, 0.2f); // Orange
        [SerializeField] private Color errorColor = new Color(0.5f, 0.0f, 0.0f); // Dark Red

        [Header("Blinking")]
        [Tooltip("Enable blinking when reconnecting.")]
        [SerializeField] private bool blinkOnReconnect = true;

        [Tooltip("Blink interval in seconds.")]
        [SerializeField] private float blinkInterval = 0.5f;

        [Header("Text Formats")]
        [SerializeField] private string connectedFormat = "Connected";
        [SerializeField] private string disconnectedFormat = "Disconnected";
        [SerializeField] private string connectingFormat = "Connecting...";
        [SerializeField] private string reconnectingFormat = "Reconnecting ({0}/{1})";
        [SerializeField] private string errorFormat = "Error";

        // State
        private SerialConnectionState _lastState = SerialConnectionState.Disconnected;
        private float _blinkTimer;
        private bool _blinkOn = true;
        private int _reconnectAttempt;
        private int _maxReconnectAttempts;

        void Start()
        {
            // Try to get components if not assigned
            if (statusImage == null)
                statusImage = GetComponent<Image>();

            if (statusText == null)
                statusText = GetComponentInChildren<Text>();

            // Subscribe to connection monitor if available
            if (connectionMonitor != null)
            {
                connectionMonitor.OnStateChanged += HandleStateChanged;
                connectionMonitor.OnReconnecting += HandleReconnecting;

                // Set initial state
                UpdateVisuals(connectionMonitor.State);
            }
        }

        void OnDestroy()
        {
            if (connectionMonitor != null)
            {
                connectionMonitor.OnStateChanged -= HandleStateChanged;
                connectionMonitor.OnReconnecting -= HandleReconnecting;
            }
        }

        void Update()
        {
            // Handle blinking during reconnect
            if (blinkOnReconnect && _lastState == SerialConnectionState.Reconnecting)
            {
                _blinkTimer += Time.deltaTime;
                if (_blinkTimer >= blinkInterval)
                {
                    _blinkTimer = 0f;
                    _blinkOn = !_blinkOn;

                    if (statusImage != null)
                    {
                        statusImage.color = _blinkOn ? reconnectingColor : Color.clear;
                    }
                }
            }
        }

        private void HandleStateChanged(SerialConnectionState state)
        {
            _lastState = state;
            UpdateVisuals(state);
        }

        private void HandleReconnecting(int attempt, int maxAttempts)
        {
            _reconnectAttempt = attempt;
            _maxReconnectAttempts = maxAttempts;
            UpdateStatusText(SerialConnectionState.Reconnecting);
        }

        private void UpdateVisuals(SerialConnectionState state)
        {
            // Update image color
            if (statusImage != null)
            {
                statusImage.color = GetColorForState(state);
                _blinkTimer = 0f;
                _blinkOn = true;
            }

            // Update status text
            UpdateStatusText(state);

            // Update port text
            UpdatePortText();
        }

        private void UpdateStatusText(SerialConnectionState state)
        {
            if (statusText == null) return;

            string text = state switch
            {
                SerialConnectionState.Connected => connectedFormat,
                SerialConnectionState.Disconnected => disconnectedFormat,
                SerialConnectionState.Connecting => connectingFormat,
                SerialConnectionState.Reconnecting => string.Format(reconnectingFormat, _reconnectAttempt, _maxReconnectAttempts),
                SerialConnectionState.Error => errorFormat,
                _ => "Unknown"
            };

            SetText(statusText, text);
        }

        private void UpdatePortText()
        {
            if (portText == null || connectionMonitor == null) return;

            string port = connectionMonitor.PortName;
            SetText(portText, string.IsNullOrEmpty(port) ? "No port" : port);
        }

        private Color GetColorForState(SerialConnectionState state)
        {
            return state switch
            {
                SerialConnectionState.Connected => connectedColor,
                SerialConnectionState.Disconnected => disconnectedColor,
                SerialConnectionState.Connecting => connectingColor,
                SerialConnectionState.Reconnecting => reconnectingColor,
                SerialConnectionState.Error => errorColor,
                _ => disconnectedColor
            };
        }

        private void SetText(Component textComponent, string value)
        {
            if (textComponent is Text unityText)
            {
                unityText.text = value;
            }
            else
            {
                // Try TextMeshPro via reflection to avoid hard dependency
                var textProperty = textComponent.GetType().GetProperty("text");
                textProperty?.SetValue(textComponent, value);
            }
        }

        /// <summary>
        /// Manually set the connection to monitor.
        /// </summary>
        public void SetConnectionMonitor(SerialConnectionMonitor monitor)
        {
            // Unsubscribe from old
            if (connectionMonitor != null)
            {
                connectionMonitor.OnStateChanged -= HandleStateChanged;
                connectionMonitor.OnReconnecting -= HandleReconnecting;
            }

            connectionMonitor = monitor;

            // Subscribe to new
            if (connectionMonitor != null)
            {
                connectionMonitor.OnStateChanged += HandleStateChanged;
                connectionMonitor.OnReconnecting += HandleReconnecting;
                UpdateVisuals(connectionMonitor.State);
            }
        }

        /// <summary>
        /// Manually update the display with a state (for testing or standalone use).
        /// </summary>
        public void SetState(SerialConnectionState state, string portName = null)
        {
            _lastState = state;
            UpdateVisuals(state);

            if (portText != null && portName != null)
            {
                SetText(portText, portName);
            }
        }
    }

    /// <summary>
    /// MonoBehaviour wrapper for SerialConnection that handles Unity lifecycle
    /// and provides events for UI binding.
    /// </summary>
    public class SerialConnectionMonitor : MonoBehaviour
    {
        [Header("Serial Settings")]
        [Tooltip("Serial port name. Leave empty for auto-detection.")]
        [SerializeField] private string portName;

        [Tooltip("Baud rate for serial communication.")]
        [SerializeField] private int baudRate = 115200;

        [Header("Reconnection")]
        [Tooltip("Automatically reconnect on disconnect.")]
        [SerializeField] private bool autoReconnect = true;

        [Tooltip("Maximum reconnection attempts.")]
        [SerializeField] private int maxReconnectAttempts = 10;

        [Tooltip("Check connection status every N seconds.")]
        [SerializeField] private float checkInterval = 1f;

        [Header("Auto Connect")]
        [Tooltip("Connect automatically on Start.")]
        [SerializeField] private bool connectOnStart = true;

        // The underlying connection
        private SerialConnection _connection;
        private float _checkTimer;

        // Events (mirror SerialConnection events for easier UI binding)
        public event System.Action<SerialConnectionState> OnStateChanged;
        public event System.Action<string> OnConnected;
        public event System.Action<string> OnDisconnected;
        public event System.Action<int, int> OnReconnecting;
        public event System.Action<string> OnError;

        // Properties
        public SerialConnectionState State => _connection?.State ?? SerialConnectionState.Disconnected;
        public bool IsConnected => _connection?.IsConnected ?? false;
        public string PortName => _connection?.PortName;
        public SerialConnection Connection => _connection;

        void Awake()
        {
            _connection = new SerialConnection(
                portName,
                baudRate,
                autoReconnect,
                maxReconnectAttempts
            );

            // Forward events
            _connection.OnStateChanged += state => OnStateChanged?.Invoke(state);
            _connection.OnConnected += port => OnConnected?.Invoke(port);
            _connection.OnDisconnected += port => OnDisconnected?.Invoke(port);
            _connection.OnReconnecting += (attempt, max) => OnReconnecting?.Invoke(attempt, max);
            _connection.OnError += error => OnError?.Invoke(error);
        }

        void Start()
        {
            if (connectOnStart)
            {
                Connect();
            }
        }

        void Update()
        {
            if (!autoReconnect || _connection == null) return;

            _checkTimer += Time.deltaTime;
            if (_checkTimer >= checkInterval)
            {
                _checkTimer = 0f;

                if (_connection.ShouldReconnect())
                {
                    _connection.TryReconnect();
                }
            }
        }

        void OnDestroy()
        {
            _connection?.Dispose();
        }

        void OnApplicationQuit()
        {
            _connection?.Dispose();
        }

        /// <summary>
        /// Connect to the serial port.
        /// </summary>
        public bool Connect()
        {
            return _connection?.Connect() ?? false;
        }

        /// <summary>
        /// Disconnect from the serial port.
        /// </summary>
        public void Disconnect()
        {
            _connection?.Disconnect();
        }

        /// <summary>
        /// Write data to the serial port.
        /// </summary>
        public bool Write(string data)
        {
            return _connection?.Write(data) ?? false;
        }

        /// <summary>
        /// Write bytes to the serial port.
        /// </summary>
        public bool Write(byte[] data)
        {
            return _connection?.Write(data) ?? false;
        }

        /// <summary>
        /// Read available data from the serial port.
        /// </summary>
        public int Read(byte[] buffer)
        {
            return _connection?.Read(buffer) ?? 0;
        }

        /// <summary>
        /// Read a line from the serial port.
        /// </summary>
        public string ReadLine()
        {
            return _connection?.ReadLine();
        }
    }
}
