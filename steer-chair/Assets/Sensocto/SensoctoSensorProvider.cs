using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Sensocto.Models;

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
    /// Unity MonoBehaviour for integrating Sensocto with the wheelchair control system.
    /// Implements IMoveReceiver for receiving movement commands from external sensors.
    /// </summary>
    public class SensoctoSensorProvider : MonoBehaviour, IMoveReceiver
    {
        [Header("Connection Settings")]
        [SerializeField] private string serverUrl = "ws://localhost:4000/socket";
        [SerializeField] private string bearerToken = "";
        [SerializeField] private bool connectOnStart = true;
        [SerializeField] private bool autoReconnect = true;

        [Header("Inbound Sensor (receives external input)")]
        [SerializeField] private bool enableInbound = true;
        [SerializeField] private string inboundSensorId = "";
        [SerializeField] private string inboundConnectorId = "";
        [SerializeField] private string inboundSensorName = "External Sensor";
        [SerializeField] private string[] inboundAttributes = { "direction", "speed" };
        [SerializeField] private string directionAttribute = "direction";
        [SerializeField] private string speedAttribute = "speed";

        [Header("Outbound Telemetry (sends wheelchair state)")]
        [SerializeField] private bool enableOutbound = true;
        [SerializeField] private string outboundSensorId = "";
        [SerializeField] private string outboundConnectorId = "";
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
        private Vector2 _currentMovement;
        private Vector2 _lastSentMovement;
        private readonly object _movementLock = new object();

        /// <summary>
        /// Current connection state.
        /// </summary>
        public ConnectionState ConnectionState => _client?.State ?? ConnectionState.Disconnected;

        /// <summary>
        /// Whether currently connected.
        /// </summary>
        public bool IsConnected => ConnectionState == ConnectionState.Connected;

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
            _client = new SensoctoClient(serverUrl, bearerToken);

            _client.OnConnectionStateChange += HandleConnectionStateChange;
            _client.OnMeasurement += HandleMeasurement;
            _client.OnBackpressureConfig += HandleBackpressureConfig;
            _client.OnError += HandleError;
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
            if (_client == null || !IsConnected)
                return;

            // Flush buffered measurements for outbound telemetry
            if (enableOutbound && !string.IsNullOrEmpty(outboundSensorId))
            {
                _client.FlushBufferedMeasurements(outboundSensorId);
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
            await _client.ConnectAsync();

            if (_client.State == ConnectionState.Connected)
            {
                await JoinChannels();
            }
        }

        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        public void Disconnect()
        {
            _client?.Disconnect();
        }

        private async System.Threading.Tasks.Task JoinChannels()
        {
            // Join inbound sensor channel
            if (enableInbound && !string.IsNullOrEmpty(inboundSensorId))
            {
                var inboundParams = new SensorJoinParams
                {
                    SensorId = inboundSensorId,
                    ConnectorId = inboundConnectorId,
                    ConnectorName = "Unity Receiver",
                    SensorName = inboundSensorName,
                    Attributes = inboundAttributes,
                    SensorType = "receiver",
                    SamplingRate = 50,
                    BatchSize = 10,
                    BearerToken = bearerToken
                };

                var joined = await _client.JoinSensorAsync(inboundParams);
                if (joined)
                {
                    Debug.Log($"[SensoctoSensorProvider] Joined inbound sensor: {inboundSensorId}");
                }
            }

            // Join outbound sensor channel
            if (enableOutbound && !string.IsNullOrEmpty(outboundSensorId))
            {
                var outboundParams = new SensorJoinParams
                {
                    SensorId = outboundSensorId,
                    ConnectorId = outboundConnectorId,
                    ConnectorName = "Unity Transmitter",
                    SensorName = outboundSensorName,
                    Attributes = outboundAttributes,
                    SensorType = "controller",
                    SamplingRate = 50,
                    BatchSize = 10,
                    BearerToken = bearerToken
                };

                var joined = await _client.JoinSensorAsync(outboundParams);
                if (joined)
                {
                    Debug.Log($"[SensoctoSensorProvider] Joined outbound sensor: {outboundSensorId}");
                }
            }
        }

        private void HandleConnectionStateChange(ConnectionState state)
        {
            Debug.Log($"[SensoctoSensorProvider] Connection state: {state}");

            if (state == ConnectionState.Connected)
            {
                OnConnected?.Invoke();
            }
            else if (state == ConnectionState.Disconnected)
            {
                OnDisconnected?.Invoke();
            }
        }

        private void HandleMeasurement(string sensorId, Measurement measurement)
        {
            // Only process measurements from inbound sensor
            if (sensorId != inboundSensorId)
                return;

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

            // Invoke on main thread
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

        private void HandleBackpressureConfig(string sensorId, BackpressureConfig config)
        {
            Debug.Log($"[SensoctoSensorProvider] Backpressure for {sensorId}: {config.AttentionLevel}, " +
                      $"window: {config.RecommendedBatchWindowMs}ms, batch: {config.RecommendedBatchSize}");
        }

        private void HandleError(string error)
        {
            Debug.LogError($"[SensoctoSensorProvider] Error: {error}");
        }

        #region IMoveReceiver Implementation

        /// <summary>
        /// IMoveReceiver.Move - Called when receiving movement from external sources.
        /// This implementation sends the movement to the outbound sensor channel.
        /// </summary>
        public void Move(Vector2 direction)
        {
            if (!enableOutbound || string.IsNullOrEmpty(outboundSensorId) || !IsConnected)
                return;

            // Only send if movement changed significantly
            if (Vector2.Distance(direction, _lastSentMovement) < 0.01f)
                return;

            _lastSentMovement = direction;

            // Buffer measurements for batch sending
            _client.BufferMeasurement(outboundSensorId, "velocity", new Dictionary<string, object>
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
            if (!enableOutbound || string.IsNullOrEmpty(outboundSensorId) || !IsConnected)
                return;

            _client.BufferMeasurement(outboundSensorId, attributeId, value);
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
        /// Get the backpressure manager for the outbound sensor.
        /// </summary>
        public BackpressureManager GetOutboundBackpressure()
        {
            return _client?.GetBackpressureManager(outboundSensorId);
        }

        /// <summary>
        /// Get the backpressure manager for the inbound sensor.
        /// </summary>
        public BackpressureManager GetInboundBackpressure()
        {
            return _client?.GetBackpressureManager(inboundSensorId);
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
