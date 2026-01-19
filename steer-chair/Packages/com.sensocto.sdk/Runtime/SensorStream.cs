using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Sensocto.SDK
{
    /// <summary>
    /// Represents a stream for sending sensor measurements to the server.
    /// Supports both single measurements and batch sending.
    /// </summary>
    public class SensorStream : IDisposable
    {
        private readonly PhoenixChannel _channel;
        private readonly string _sensorId;
        private readonly SensorConfig _config;
        private readonly List<Measurement> _batchBuffer;
        private readonly object _batchLock = new object();

        private bool _disposed;
        private int _currentBatchSize;
        private float _lastBatchSendTime;

        /// <summary>
        /// The sensor ID for this stream.
        /// </summary>
        public string SensorId => _sensorId;

        /// <summary>
        /// Whether the stream is currently active.
        /// </summary>
        public bool IsActive => !_disposed && _channel?.IsJoined == true;

        /// <summary>
        /// Current recommended batch window from server (ms).
        /// </summary>
        public int RecommendedBatchWindow { get; private set; } = 500;

        /// <summary>
        /// Current recommended batch size from server.
        /// </summary>
        public int RecommendedBatchSize { get; private set; } = 5;

        internal SensorStream(PhoenixChannel channel, string sensorId, SensorConfig config)
        {
            _channel = channel;
            _sensorId = sensorId;
            _config = config;
            _batchBuffer = new List<Measurement>(config.BatchSize);
            _currentBatchSize = config.BatchSize;

            // Listen for backpressure updates
            _channel.On("backpressure_config", HandleBackpressureConfig);
        }

        /// <summary>
        /// Sends a single measurement to the server.
        /// </summary>
        /// <param name="attributeId">The attribute identifier (e.g., "heart_rate", "temperature").</param>
        /// <param name="payload">The measurement payload (can be a number or a complex object).</param>
        /// <param name="timestamp">Optional timestamp in milliseconds. Uses current time if not specified.</param>
        public async Task SendMeasurementAsync(string attributeId, object payload, long? timestamp = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SensorStream));

            var measurement = new Measurement
            {
                AttributeId = attributeId,
                Payload = payload,
                Timestamp = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var messagePayload = new Dictionary<string, object>
            {
                ["attribute_id"] = measurement.AttributeId,
                ["payload"] = measurement.Payload,
                ["timestamp"] = measurement.Timestamp
            };

            await _channel.PushAsync("measurement", messagePayload);
        }

        /// <summary>
        /// Adds a measurement to the batch buffer.
        /// The batch will be sent when it reaches the configured size or when FlushBatchAsync is called.
        /// </summary>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <param name="payload">The measurement payload.</param>
        /// <param name="timestamp">Optional timestamp in milliseconds.</param>
        public void AddToBatch(string attributeId, object payload, long? timestamp = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SensorStream));

            var measurement = new Measurement
            {
                AttributeId = attributeId,
                Payload = payload,
                Timestamp = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            lock (_batchLock)
            {
                _batchBuffer.Add(measurement);

                if (_batchBuffer.Count >= _currentBatchSize)
                {
                    // Fire and forget the batch send
                    _ = FlushBatchInternalAsync();
                }
            }
        }

        /// <summary>
        /// Flushes any pending measurements in the batch buffer.
        /// </summary>
        public async Task FlushBatchAsync()
        {
            await FlushBatchInternalAsync();
        }

        /// <summary>
        /// Updates the attribute registry for this sensor.
        /// </summary>
        /// <param name="action">The action to perform (add, remove, update).</param>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <param name="metadata">Optional metadata for the attribute.</param>
        public async Task UpdateAttributeAsync(string action, string attributeId, Dictionary<string, object> metadata = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SensorStream));

            var payload = new Dictionary<string, object>
            {
                ["action"] = action,
                ["attribute_id"] = attributeId,
                ["metadata"] = metadata ?? new Dictionary<string, object>()
            };

            await _channel.PushAsync("update_attributes", payload);
        }

        /// <summary>
        /// Updates the batch configuration based on current attention level.
        /// Call this to respect server backpressure recommendations.
        /// </summary>
        public void ApplyBackpressure()
        {
            lock (_batchLock)
            {
                _currentBatchSize = RecommendedBatchSize;
            }
        }

        private async Task FlushBatchInternalAsync()
        {
            List<Dictionary<string, object>> batchToSend;

            lock (_batchLock)
            {
                if (_batchBuffer.Count == 0) return;

                batchToSend = new List<Dictionary<string, object>>(_batchBuffer.Count);
                foreach (var m in _batchBuffer)
                {
                    batchToSend.Add(new Dictionary<string, object>
                    {
                        ["attribute_id"] = m.AttributeId,
                        ["payload"] = m.Payload,
                        ["timestamp"] = m.Timestamp
                    });
                }

                _batchBuffer.Clear();
                _lastBatchSendTime = Time.time;
            }

            await _channel.PushAsync("measurements_batch", batchToSend);
        }

        private void HandleBackpressureConfig(Dictionary<string, object> payload)
        {
            if (payload.TryGetValue("recommended_batch_window", out var window))
            {
                RecommendedBatchWindow = Convert.ToInt32(window);
            }

            if (payload.TryGetValue("recommended_batch_size", out var size))
            {
                RecommendedBatchSize = Convert.ToInt32(size);
            }

            Debug.Log($"[Sensocto] Backpressure config received: window={RecommendedBatchWindow}ms, size={RecommendedBatchSize}");
        }

        /// <summary>
        /// Leaves the sensor channel and releases resources.
        /// </summary>
        public async Task CloseAsync()
        {
            if (_disposed) return;

            // Flush any remaining measurements
            await FlushBatchInternalAsync();

            await _channel.LeaveAsync();
            _disposed = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_batchLock)
            {
                _batchBuffer.Clear();
            }
        }
    }
}
