using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Sensocto.SDK;

namespace Sensocto
{
    /// <summary>
    /// Emits joystick X/Y values as a sensor to the Sensocto platform.
    /// Implements IMoveReceiver to receive movement from DriveEvents or other sources.
    /// Add this component alongside other IMoveReceiver components to have the joystick
    /// input automatically broadcast as sensor measurements.
    /// </summary>
    public class JoystickSensorEmitter : MonoBehaviour, IMoveReceiver
    {
        [Header("Connection Settings")]
        [SerializeField] private string serverUrl = "wss://sensocto.fly.dev/socket";
        [SerializeField] private string bearerToken = "";
        [SerializeField] private bool connectOnStart = true;
        [Tooltip("Use token from AuthManager (set via deep link) instead of bearerToken field")]
        [SerializeField] private bool useAuthManager = true;

        [Header("Sensor Configuration")]
        [SerializeField] private string sensorName = "Unity Joystick";
        [SerializeField] private string sensorType = "joystick";
        [SerializeField] private int samplingRateHz = 30;
        [SerializeField] private int batchSize = 5;

        [Header("Data Transmission")]
        [Tooltip("Send individual X and Y attributes separately")]
        [SerializeField] private bool sendSeparateAxes = true;
        [Tooltip("Also send combined XY as single measurement")]
        [SerializeField] private bool sendCombinedXY = true;
        [Tooltip("Minimum change threshold to send update (0-1)")]
        [SerializeField] private float changeThreshold = 0.01f;

        [Header("Events")]
        public UnityEvent OnConnected;
        public UnityEvent OnDisconnected;
        public UnityEvent<string> OnSensorRegistered;

        private SensoctoClient _client;
        private SensoctoConfig _config;
        private SensorStream _stream;
        private Vector2 _lastSentPosition;
        private bool _isRegistered;

        /// <summary>
        /// The registered sensor ID. Available after successful registration.
        /// </summary>
        public string SensorId => _stream?.SensorId;

        /// <summary>
        /// Whether the sensor is currently active and sending data.
        /// </summary>
        public bool IsActive => _stream?.IsActive ?? false;

        /// <summary>
        /// Current connection state.
        /// </summary>
        public ConnectionState ConnectionState => _client?.ConnectionState ?? ConnectionState.Disconnected;

        private void Awake()
        {
            InitializeClient();

            // Re-initialize if auth changes (e.g., deep link received while running)
            if (useAuthManager)
            {
                AuthManager.OnAuthChanged += HandleAuthChanged;
            }
        }

        private void Start()
        {
            if (connectOnStart)
            {
                // If using AuthManager, wait for valid token or connect anyway
                if (useAuthManager && !AuthManager.HasValidToken())
                {
                    Debug.Log("[JoystickSensorEmitter] Waiting for authentication via deep link...");
                    return;
                }
                Connect();
            }
        }

        private void Update()
        {
            // Flush stream if active
            if (_stream != null && _stream.IsActive)
            {
                _ = _stream.FlushBatchAsync();
            }
        }

        private void InitializeClient()
        {
            // Determine which token and server to use
            var effectiveToken = GetEffectiveToken();
            var effectiveServer = GetEffectiveServerUrl();

            _config = SensoctoConfig.CreateRuntime(effectiveServer, "Unity DriveChair");
            _config.BearerToken = effectiveToken;
            _config.AutoJoinConnector = false;

            _client = new SensoctoClient(_config);
            _client.OnConnectionStateChanged += HandleConnectionStateChange;
            _client.OnError += HandleError;

            Debug.Log($"[JoystickSensorEmitter] Initialized with server: {effectiveServer}, token: {(string.IsNullOrEmpty(effectiveToken) ? "NONE" : "SET")}");
        }

        private string GetEffectiveToken()
        {
            if (useAuthManager && AuthManager.HasValidToken())
            {
                return AuthManager.GetToken();
            }
            return bearerToken;
        }

        private string GetEffectiveServerUrl()
        {
            if (useAuthManager)
            {
                var authServer = AuthManager.GetServerUrl();
                if (!string.IsNullOrEmpty(authServer))
                {
                    return authServer;
                }
            }
            return serverUrl;
        }

        private void HandleAuthChanged()
        {
            Debug.Log("[JoystickSensorEmitter] Auth changed, reconnecting...");

            // Disconnect existing connection
            Disconnect();

            // Dispose old client
            _client?.Dispose();

            // Re-initialize with new credentials
            InitializeClient();

            // Connect if we have valid auth
            if (AuthManager.HasValidToken())
            {
                Connect();
            }
        }

        private void OnDestroy()
        {
            if (useAuthManager)
            {
                AuthManager.OnAuthChanged -= HandleAuthChanged;
            }

            Disconnect();
            _stream?.Dispose();
            _client?.Dispose();
        }

        /// <summary>
        /// Connect to the server and register as a joystick sensor.
        /// </summary>
        public async void Connect()
        {
            if (_client == null) return;

            Debug.Log($"[JoystickSensorEmitter] Connecting to {GetEffectiveServerUrl()}...");

            try
            {
                var connected = await _client.ConnectAsync(GetEffectiveToken());

                if (connected)
                {
                    await RegisterSensor();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JoystickSensorEmitter] Connection failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        public async void Disconnect()
        {
            try
            {
                if (_stream != null)
                {
                    await _stream.CloseAsync();
                    _stream = null;
                }

                _isRegistered = false;

                if (_client != null)
                {
                    await _client.DisconnectAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JoystickSensorEmitter] Error during disconnect: {ex.Message}");
            }
        }

        private async Task RegisterSensor()
        {
            var attributes = new List<string>();

            if (sendSeparateAxes)
            {
                attributes.Add("x");
                attributes.Add("y");
            }

            if (sendCombinedXY)
            {
                attributes.Add("xy");
            }

            var sensorConfig = new SensorConfig
            {
                SensorName = sensorName,
                SensorType = sensorType,
                Attributes = attributes,
                SamplingRateHz = samplingRateHz,
                BatchSize = batchSize
            };

            try
            {
                _stream = await _client.RegisterSensorAsync(sensorConfig);

                if (_stream != null)
                {
                    _isRegistered = true;
                    Debug.Log($"[JoystickSensorEmitter] Registered as sensor: {_stream.SensorId}");
                    OnSensorRegistered?.Invoke(_stream.SensorId);
                }
                else
                {
                    Debug.LogError("[JoystickSensorEmitter] Failed to register sensor");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JoystickSensorEmitter] Failed to register sensor: {ex.Message}");
            }
        }

        private void HandleConnectionStateChange(ConnectionState state)
        {
            Debug.Log($"[JoystickSensorEmitter] Connection state: {state}");

            if (state == ConnectionState.Connected)
            {
                OnConnected?.Invoke();
            }
            else if (state == ConnectionState.Disconnected)
            {
                _isRegistered = false;
                OnDisconnected?.Invoke();
            }
        }

        private void HandleError(SensoctoError error)
        {
            Debug.LogError($"[JoystickSensorEmitter] Error: {error.Message}");
        }

        #region IMoveReceiver Implementation

        /// <summary>
        /// Receives movement from DriveEvents and emits it as sensor measurements.
        /// </summary>
        public void Move(Vector2 direction)
        {
            if (!_isRegistered || _stream == null || !_stream.IsActive)
                return;

            // Only send if movement changed significantly
            if (Vector2.Distance(direction, _lastSentPosition) < changeThreshold)
                return;

            _lastSentPosition = direction;

            // Send separate X and Y attributes
            if (sendSeparateAxes)
            {
                _stream.AddToBatch("x", direction.x);
                _stream.AddToBatch("y", direction.y);
            }

            // Send combined XY measurement
            if (sendCombinedXY)
            {
                _stream.AddToBatch("xy", new Dictionary<string, object>
                {
                    ["x"] = direction.x,
                    ["y"] = direction.y
                });
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Manually send a joystick position (useful for testing or external input).
        /// </summary>
        public void SendPosition(float x, float y)
        {
            Move(new Vector2(x, y));
        }

        /// <summary>
        /// Force flush any buffered measurements.
        /// </summary>
        public async void Flush()
        {
            if (_stream != null)
            {
                await _stream.FlushBatchAsync();
            }
        }

        #endregion

        #region Editor Helpers

        [ContextMenu("Test Connection")]
        private void TestConnection()
        {
            if (Application.isPlaying)
            {
                Connect();
            }
        }

        [ContextMenu("Test Send Center")]
        private void TestSendCenter()
        {
            if (Application.isPlaying && IsActive)
            {
                Move(Vector2.zero);
                Flush();
            }
        }

        [ContextMenu("Test Send Forward")]
        private void TestSendForward()
        {
            if (Application.isPlaying && IsActive)
            {
                Move(new Vector2(0, 1));
                Flush();
            }
        }

        #endregion
    }
}
