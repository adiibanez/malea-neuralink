using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sensocto.SDK
{
    /// <summary>
    /// Represents a subscription to a sensor for receiving measurements.
    /// Use this to receive real-time data from an external sensor.
    /// </summary>
    public class SensorSubscription : IDisposable
    {
        private readonly PhoenixChannel _channel;
        private readonly string _sensorId;
        private readonly BackpressureManager _backpressure;
        private bool _disposed;

        /// <summary>
        /// The sensor ID this subscription is listening to.
        /// </summary>
        public string SensorId => _sensorId;

        /// <summary>
        /// Whether the subscription is currently active.
        /// </summary>
        public bool IsActive => !_disposed && _channel?.IsJoined == true;

        /// <summary>
        /// Current backpressure configuration from the server.
        /// </summary>
        public BackpressureManager Backpressure => _backpressure;

        /// <summary>
        /// Event fired when a measurement is received from the sensor.
        /// </summary>
        public event Action<Measurement> OnMeasurement;

        /// <summary>
        /// Event fired when backpressure configuration changes.
        /// </summary>
        public event Action<BackpressureConfig> OnBackpressureConfig;

        internal SensorSubscription(PhoenixChannel channel, string sensorId)
        {
            _channel = channel;
            _sensorId = sensorId;
            _backpressure = new BackpressureManager();

            // Listen for measurements
            _channel.On("measurement", HandleMeasurement);

            // Listen for backpressure config
            _channel.On("backpressure_config", HandleBackpressureConfig);
        }

        private void HandleMeasurement(Dictionary<string, object> payload)
        {
            try
            {
                var measurement = Measurement.FromDictionary(payload);
                OnMeasurement?.Invoke(measurement);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SensorSubscription] Error parsing measurement: {ex.Message}");
            }
        }

        private void HandleBackpressureConfig(Dictionary<string, object> payload)
        {
            try
            {
                var config = BackpressureConfig.FromPayload(payload);
                _backpressure.UpdateConfig(config);
                OnBackpressureConfig?.Invoke(config);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SensorSubscription] Error parsing backpressure config: {ex.Message}");
            }
        }

        /// <summary>
        /// Unsubscribes from the sensor and releases resources.
        /// </summary>
        public async System.Threading.Tasks.Task UnsubscribeAsync()
        {
            if (_disposed) return;

            await _channel.LeaveAsync();
            _disposed = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _channel.Off("measurement");
            _channel.Off("backpressure_config");
        }
    }
}
