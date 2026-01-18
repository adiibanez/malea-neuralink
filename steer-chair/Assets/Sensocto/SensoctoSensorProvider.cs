using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Sensocto.SDK;

namespace Sensocto
{
    /// <summary>
    /// Unity event for receiving movement data.
    /// </summary>
    [Serializable]
    public class MovementEvent : UnityEvent<Vector2> { }

    /// <summary>
    /// Unity event for receiving raw measurement data.
    /// </summary>
    [Serializable]
    public class MeasurementEvent : UnityEvent<string, object> { }

    /// <summary>
    /// Unity MonoBehaviour for integrating Sensocto SDK with the wheelchair control system.
    /// Implements IMoveReceiver for receiving movement commands from external sensors.
    /// </summary>
    public class SensoctoSensorProvider : MonoBehaviour, IMoveReceiver
    {
        [Header("Connection Settings")]
        [SerializeField] private string serverUrl = "wss://sensocto.fly.dev/socket";
        [SerializeField] private string bearerToken = "";
        [SerializeField] private bool connectOnStart = true;
        [SerializeField] private bool autoReconnect = true;

        [Header("Inbound Sensor (receives external input)")]
        [SerializeField] private bool enableInbound = true;
        [SerializeField] private string inboundSensorId = "";
        [SerializeField] private string directionAttribute = "direction";
        [SerializeField] private string speedAttribute = "speed";

        [Header("Outbound Telemetry (sends wheelchair state)")]
        [SerializeField] private bool enableOutbound = true;
        [SerializeField] private string outboundSensorId = "";
        [SerializeField] private string outboundSensorName = "Unity Wheelchair";
        [SerializeField] private string[] outboundAttributes = { "position", "velocity", "state" };

        [Header("Movement Settings")]
        [SerializeField] private float movementSensitivity = 1f;
        [SerializeField] private float deadZone = 0.1f;

        [Header("Events")]
        public MovementEvent OnMovementReceived;
        public MeasurementEvent OnMeasurementReceived;
        public UnityEvent OnConnected;
        public UnityEvent OnDisconnected;

        private SensoctoClient _client;
        private SensoctoConfig _config;
        private SensorSubscription _inboundSubscription;
        private SensorStream _outboundStream;
        private Vector2 _currentMovement;
        private Vector2 _lastSentMovement;
        private readonly object _movementLock = new object();

        /// <summary>
        /// Current connection state.
        /// </summary>
        public ConnectionState ConnectionState => _client?.ConnectionState ?? ConnectionState.Disconnected;

        /// <summary>
        /// Whether currently connected.
        /// </summary>
        public bool IsConnected => _client?.IsConnected ?? false;

        /// <summary>
        /// Current movement input from sensors.
        /// </summary>
        public Vector2 CurrentMovement
        {
            get
            {
                lock (_movementLock)
                {
                    return _currentMovement;
                }
            }
        }

        private void Awake()
        {
            _config = SensoctoConfig.CreateRuntime(serverUrl, "Unity Wheelchair");
            _config.BearerToken = bearerToken;
            _config.AutoReconnect = autoReconnect;
            _config.AutoJoinConnector = false;

            _client = new SensoctoClient(_config);
            _client.OnConnectionStateChanged += HandleConnectionStateChange;
            _client.OnError += HandleError;
            _client.OnReconnected += HandleReconnected;
        }

        private void Start()
        {
            if (connectOnStart)
            {
                Connect();
            }
        }

        private void Update()
        {
            // Flush outbound stream if active
            if (_outboundStream != null && _outboundStream.IsActive)
            {
                _ = _outboundStream.FlushBatchAsync();
            }
        }

        private void OnDestroy()
        {
            Disconnect();
            _client?.Dispose();
        }

        /// <summary>
        /// Connect to the Sensocto server.
        /// </summary>
        public async void Connect()
        {
            if (_client == null)
                return;

            Debug.Log($"[SensoctoSensorProvider] Connecting to {serverUrl}...");

            try
            {
                var connected = await _client.ConnectAsync(bearerToken);

                if (connected)
                {
                    await SetupChannels();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SensoctoSensorProvider] Connection failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        public async void Disconnect()
        {
            try
            {
                if (_inboundSubscription != null)
                {
                    await _inboundSubscription.UnsubscribeAsync();
                    _inboundSubscription = null;
                }

                if (_outboundStream != null)
                {
                    await _outboundStream.CloseAsync();
                    _outboundStream = null;
                }

                if (_client != null)
                {
                    await _client.DisconnectAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SensoctoSensorProvider] Error during disconnect: {ex.Message}");
            }
        }

        private async Task SetupChannels()
        {
            // Subscribe to inbound sensor
            if (enableInbound && !string.IsNullOrEmpty(inboundSensorId))
            {
                try
                {
                    _inboundSubscription = await _client.SubscribeToSensorAsync(inboundSensorId, "Unity Receiver");
                    _inboundSubscription.OnMeasurement += HandleMeasurement;
                    _inboundSubscription.OnBackpressureConfig += HandleBackpressureConfig;
                    Debug.Log($"[SensoctoSensorProvider] Subscribed to inbound sensor: {inboundSensorId}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SensoctoSensorProvider] Failed to subscribe to inbound sensor: {ex.Message}");
                }
            }

            // Register outbound sensor
            if (enableOutbound && !string.IsNullOrEmpty(outboundSensorId))
            {
                try
                {
                    var sensorConfig = new SensorConfig
                    {
                        SensorId = outboundSensorId,
                        SensorName = outboundSensorName,
                        SensorType = "controller",
                        Attributes = new List<string>(outboundAttributes),
                        SamplingRateHz = 50,
                        BatchSize = 10
                    };

                    _outboundStream = await _client.RegisterSensorAsync(sensorConfig);
                    Debug.Log($"[SensoctoSensorProvider] Registered outbound sensor: {outboundSensorId}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SensoctoSensorProvider] Failed to register outbound sensor: {ex.Message}");
                }
            }
        }

        private void HandleConnectionStateChange(ConnectionState state)
        {
            Debug.Log($"[SensoctoSensorProvider] Connection state: {state}");

            // Invoke events on main thread
            if (state == ConnectionState.Connected)
            {
                OnConnected?.Invoke();
            }
            else if (state == ConnectionState.Disconnected)
            {
                OnDisconnected?.Invoke();
            }
        }

        private async void HandleReconnected()
        {
            Debug.Log("[SensoctoSensorProvider] Reconnected, re-establishing channels...");
            await SetupChannels();
        }

        private void HandleMeasurement(Measurement measurement)
        {
            OnMeasurementReceived?.Invoke(measurement.AttributeId, measurement.Payload);

            // Parse direction/speed into movement vector
            if (measurement.AttributeId == directionAttribute || measurement.AttributeId == speedAttribute)
            {
                UpdateMovement(measurement);
            }
        }

        private void UpdateMovement(Measurement measurement)
        {
            lock (_movementLock)
            {
                if (measurement.AttributeId == directionAttribute)
                {
                    _currentMovement.x = ParseFloat(measurement.Payload) * movementSensitivity;
                }
                else if (measurement.AttributeId == speedAttribute)
                {
                    _currentMovement.y = ParseFloat(measurement.Payload) * movementSensitivity;
                }

                // Apply dead zone
                if (Mathf.Abs(_currentMovement.x) < deadZone) _currentMovement.x = 0;
                if (Mathf.Abs(_currentMovement.y) < deadZone) _currentMovement.y = 0;

                // Clamp to -1 to 1
                _currentMovement.x = Mathf.Clamp(_currentMovement.x, -1f, 1f);
                _currentMovement.y = Mathf.Clamp(_currentMovement.y, -1f, 1f);
            }

            OnMovementReceived?.Invoke(_currentMovement);
        }

        private float ParseFloat(object value)
        {
            if (value == null) return 0;
            if (value is float f) return f;
            if (value is double d) return (float)d;
            if (value is int i) return i;
            if (value is long l) return l;

            // Try to parse from dictionary (Vector2-like payload)
            if (value is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue("x", out var x))
                    return ParseFloat(x);
                if (dict.TryGetValue("value", out var v))
                    return ParseFloat(v);
            }

            if (float.TryParse(value.ToString(), out var result))
                return result;

            return 0;
        }

        private void HandleBackpressureConfig(BackpressureConfig config)
        {
            Debug.Log($"[SensoctoSensorProvider] Backpressure: {config.AttentionLevel}, " +
                      $"window: {config.RecommendedBatchWindow}ms, batch: {config.RecommendedBatchSize}");
        }

        private void HandleError(SensoctoError error)
        {
            Debug.LogError($"[SensoctoSensorProvider] Error: {error.Message}");
        }

        #region IMoveReceiver Implementation

        /// <summary>
        /// IMoveReceiver.Move - Called when receiving movement from external sources.
        /// This implementation sends the movement to the outbound sensor channel.
        /// </summary>
        public void Move(Vector2 direction)
        {
            if (!enableOutbound || _outboundStream == null || !_outboundStream.IsActive)
                return;

            // Only send if movement changed significantly
            if (Vector2.Distance(direction, _lastSentMovement) < 0.01f)
                return;

            _lastSentMovement = direction;

            // Add to batch for sending
            _outboundStream.AddToBatch("velocity", new Dictionary<string, object>
            {
                ["x"] = direction.x,
                ["y"] = direction.y
            });
        }

        #endregion

        #region Public API for sending telemetry

        /// <summary>
        /// Send a telemetry measurement to the outbound sensor.
        /// </summary>
        public void SendTelemetry(string attributeId, object value)
        {
            if (!enableOutbound || _outboundStream == null || !_outboundStream.IsActive)
                return;

            _outboundStream.AddToBatch(attributeId, value);
        }

        /// <summary>
        /// Send position telemetry.
        /// </summary>
        public void SendPosition(Vector3 position)
        {
            SendTelemetry("position", new Dictionary<string, object>
            {
                ["x"] = position.x,
                ["y"] = position.y,
                ["z"] = position.z
            });
        }

        /// <summary>
        /// Send state telemetry.
        /// </summary>
        public void SendState(string state)
        {
            SendTelemetry("state", state);
        }

        /// <summary>
        /// Get the backpressure manager for the inbound sensor.
        /// </summary>
        public BackpressureManager GetInboundBackpressure()
        {
            return _inboundSubscription?.Backpressure;
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

        [ContextMenu("Test Disconnect")]
        private void TestDisconnect()
        {
            if (Application.isPlaying)
            {
                Disconnect();
            }
        }

        [ContextMenu("Send Test Telemetry")]
        private void SendTestTelemetry()
        {
            if (Application.isPlaying && IsConnected)
            {
                SendTelemetry("test", new Dictionary<string, object>
                {
                    ["message"] = "Hello from Unity",
                    ["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
        }

        #endregion
    }
}
