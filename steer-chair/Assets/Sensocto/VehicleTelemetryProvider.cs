using System;
using System.Collections.Generic;
using UnityEngine;
using Sensocto.Models;

namespace Sensocto
{
    /// <summary>
    /// Provides vehicle telemetry data (direction, speed, battery) with backpressure support.
    /// Attach to any vehicle GameObject. Will auto-detect IVehicleTelemetry or use input from IMoveReceiver.
    /// </summary>
    public class VehicleTelemetryProvider : MonoBehaviour, IMoveReceiver
    {
        [Header("Sensor Configuration")]
        [SerializeField] private string sensorId = "";
        [SerializeField] private string vehicleName = "Vehicle";

        [Header("Telemetry Settings")]
        [SerializeField] private float baseSamplingRateHz = 10f;
        [SerializeField] private bool trackPosition = true;
        [SerializeField] private bool trackVelocity = true;
        [SerializeField] private bool trackBattery = true;

        [Header("Battery Simulation")]
        [SerializeField] private float initialBatteryLevel = 1f;
        [SerializeField] private float batteryDrainPerSecond = 0.001f;
        [SerializeField] private bool simulateBattery = true;

        [Header("Sensocto Integration")]
        [SerializeField] private SensoctoSensorProvider sensoctoProvider;
        [SerializeField] private bool useExternalSensocto = false;

        [Header("Debug")]
        [SerializeField] private bool logTelemetry = true;
        [SerializeField] private bool logBackpressure = false;

        // Internal state
        private Vector2 _currentInput;
        private float _currentSpeed;
        private float _currentDirection;
        private float _batteryLevel;
        private Vector3 _lastPosition;
        private Vector3 _velocity;

        // Backpressure management
        private BackpressureManager _backpressure;
        private float _lastSampleTime;
        private float _effectiveSamplingInterval;

        // External telemetry source
        private IVehicleTelemetry _telemetrySource;

        /// <summary>
        /// Current telemetry snapshot.
        /// </summary>
        public VehicleTelemetrySnapshot CurrentTelemetry => GetTelemetrySnapshot();

        /// <summary>
        /// Current backpressure attention level.
        /// </summary>
        public AttentionLevel AttentionLevel => _backpressure?.CurrentLevel ?? AttentionLevel.None;

        /// <summary>
        /// Event fired when telemetry is sampled.
        /// </summary>
        public event Action<VehicleTelemetrySnapshot> OnTelemetrySampled;

        private void Awake()
        {
            _backpressure = new BackpressureManager();
            _backpressure.OnConfigUpdated += HandleBackpressureUpdate;

            _batteryLevel = initialBatteryLevel;
            _lastPosition = transform.position;
            _effectiveSamplingInterval = 1f / baseSamplingRateHz;

            // Try to find telemetry source
            _telemetrySource = GetComponent<IVehicleTelemetry>();

            // Generate sensor ID if not set
            if (string.IsNullOrEmpty(sensorId))
            {
                sensorId = $"vehicle_{gameObject.GetInstanceID()}";
            }
        }

        private void Start()
        {
            // Find Sensocto provider if not assigned
            if (sensoctoProvider == null && useExternalSensocto)
            {
                sensoctoProvider = FindObjectOfType<SensoctoSensorProvider>();
            }
        }

        private void Update()
        {
            UpdateVelocity();
            UpdateBattery();
            SampleTelemetryIfReady();
            FlushIfReady();
        }

        private void UpdateVelocity()
        {
            if (!trackVelocity) return;

            Vector3 currentPos = transform.position;
            _velocity = (currentPos - _lastPosition) / Time.deltaTime;
            _lastPosition = currentPos;
        }

        private void UpdateBattery()
        {
            if (!simulateBattery) return;

            // Drain battery based on movement
            float drainMultiplier = 1f + Mathf.Abs(_currentSpeed) * 2f;
            _batteryLevel -= batteryDrainPerSecond * drainMultiplier * Time.deltaTime;
            _batteryLevel = Mathf.Clamp01(_batteryLevel);
        }

        private void SampleTelemetryIfReady()
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastSampleTime < _effectiveSamplingInterval)
                return;

            _lastSampleTime = now;

            var snapshot = GetTelemetrySnapshot();

            // Buffer measurements with backpressure
            BufferTelemetry(snapshot);

            // Fire event
            OnTelemetrySampled?.Invoke(snapshot);

            if (logTelemetry)
            {
                Debug.Log($"[VehicleTelemetry:{vehicleName}] dir={snapshot.Direction:F2} spd={snapshot.Speed:F2} " +
                          $"bat={snapshot.BatteryLevel:P0} lvl={AttentionLevel}");
            }
        }

        private void BufferTelemetry(VehicleTelemetrySnapshot snapshot)
        {
            // Buffer direction
            _backpressure.AddMeasurement("direction", snapshot.Direction);

            // Buffer speed
            _backpressure.AddMeasurement("speed", snapshot.Speed);

            // Buffer battery if tracking
            if (trackBattery)
            {
                _backpressure.AddMeasurement("battery", snapshot.BatteryLevel);
            }

            // Buffer position if tracking
            if (trackPosition)
            {
                _backpressure.AddMeasurement("position", new Dictionary<string, object>
                {
                    ["x"] = snapshot.Position.x,
                    ["y"] = snapshot.Position.y,
                    ["z"] = snapshot.Position.z
                });
            }

            // Buffer velocity if tracking
            if (trackVelocity)
            {
                _backpressure.AddMeasurement("velocity", new Dictionary<string, object>
                {
                    ["x"] = snapshot.Velocity.x,
                    ["y"] = snapshot.Velocity.y,
                    ["z"] = snapshot.Velocity.z
                });
            }
        }

        private void FlushIfReady()
        {
            var batch = _backpressure.FlushIfReady();
            if (batch == null || batch.Count == 0)
                return;

            // Send via Sensocto if available
            if (useExternalSensocto && sensoctoProvider != null && sensoctoProvider.IsConnected)
            {
                foreach (var measurement in batch)
                {
                    sensoctoProvider.SendTelemetry(measurement.AttributeId, measurement.Payload);
                }
            }

            if (logTelemetry)
            {
                Debug.Log($"[VehicleTelemetry:{vehicleName}] Flushed {batch.Count} measurements (window={_backpressure.BatchWindowMs}ms, size={_backpressure.BatchSize})");
            }
        }

        private VehicleTelemetrySnapshot GetTelemetrySnapshot()
        {
            // Use external telemetry source if available
            if (_telemetrySource != null)
            {
                return new VehicleTelemetrySnapshot
                {
                    SensorId = sensorId,
                    VehicleName = vehicleName,
                    Direction = _telemetrySource.Direction,
                    Speed = _telemetrySource.Speed,
                    BatteryLevel = _telemetrySource.BatteryLevel >= 0 ? _telemetrySource.BatteryLevel : _batteryLevel,
                    Position = _telemetrySource.Position,
                    Velocity = _telemetrySource.Velocity,
                    Rotation = _telemetrySource.Rotation,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
            }

            // Use internal state
            return new VehicleTelemetrySnapshot
            {
                SensorId = sensorId,
                VehicleName = vehicleName,
                Direction = _currentDirection,
                Speed = _currentSpeed,
                BatteryLevel = _batteryLevel,
                Position = transform.position,
                Velocity = _velocity,
                Rotation = transform.rotation,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private void HandleBackpressureUpdate(BackpressureConfig config)
        {
            // Adjust sampling rate based on backpressure
            float optimalRate = _backpressure.GetOptimalSamplingRate();
            _effectiveSamplingInterval = 1f / Mathf.Min(optimalRate, baseSamplingRateHz);

            if (logBackpressure)
            {
                Debug.Log($"[VehicleTelemetry:{vehicleName}] Backpressure update: {config.AttentionLevel}, " +
                          $"sampling interval now {_effectiveSamplingInterval * 1000:F0}ms");
            }
        }

        #region IMoveReceiver Implementation

        /// <summary>
        /// Receives movement input and updates internal telemetry state.
        /// </summary>
        public void Move(Vector2 direction)
        {
            _currentInput = direction;
            _currentDirection = direction.x;
            _currentSpeed = direction.y;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Manually update the backpressure configuration.
        /// </summary>
        public void UpdateBackpressure(BackpressureConfig config)
        {
            _backpressure.UpdateConfig(config);
        }

        /// <summary>
        /// Set the attention level directly (for testing or local control).
        /// </summary>
        public void SetAttentionLevel(AttentionLevel level)
        {
            _backpressure.UpdateConfig(BackpressureConfig.GetDefault(level));
        }

        /// <summary>
        /// Set battery level directly (0-1).
        /// </summary>
        public void SetBatteryLevel(float level)
        {
            _batteryLevel = Mathf.Clamp01(level);
        }

        /// <summary>
        /// Get the backpressure manager for external access.
        /// </summary>
        public BackpressureManager GetBackpressureManager()
        {
            return _backpressure;
        }

        /// <summary>
        /// Force flush all buffered telemetry immediately.
        /// </summary>
        public void ForceFlush()
        {
            var batch = _backpressure.Flush();
            if (batch != null && batch.Count > 0 && logTelemetry)
            {
                Debug.Log($"[VehicleTelemetry:{vehicleName}] Force flushed {batch.Count} measurements");
            }
        }

        #endregion
    }

    /// <summary>
    /// Snapshot of vehicle telemetry at a point in time.
    /// </summary>
    [Serializable]
    public struct VehicleTelemetrySnapshot
    {
        public string SensorId;
        public string VehicleName;
        public float Direction;
        public float Speed;
        public float BatteryLevel;
        public Vector3 Position;
        public Vector3 Velocity;
        public Quaternion Rotation;
        public long Timestamp;

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["sensor_id"] = SensorId,
                ["vehicle_name"] = VehicleName,
                ["direction"] = Direction,
                ["speed"] = Speed,
                ["battery_level"] = BatteryLevel,
                ["position"] = new Dictionary<string, object>
                {
                    ["x"] = Position.x,
                    ["y"] = Position.y,
                    ["z"] = Position.z
                },
                ["velocity"] = new Dictionary<string, object>
                {
                    ["x"] = Velocity.x,
                    ["y"] = Velocity.y,
                    ["z"] = Velocity.z
                },
                ["rotation"] = new Dictionary<string, object>
                {
                    ["x"] = Rotation.x,
                    ["y"] = Rotation.y,
                    ["z"] = Rotation.z,
                    ["w"] = Rotation.w
                },
                ["timestamp"] = Timestamp
            };
        }
    }
}
