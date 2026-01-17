using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Sensocto.Core;
using Sensocto.Models;

namespace Sensocto
{
    /// <summary>
    /// Represents a stream for sending sensor measurements to the server.
    /// Supports both single measurements and batch sending.
    /// Created via SensoctoClient.RegisterSensorAsync().
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

        /// <summary>
        /// The sensor ID for this stream.
        /// </summary>
        public string SensorId => _sensorId;

        /// <summary>
        /// Whether the stream is currently active.
        /// </summary>
        public bool IsActive => !_disposed && _channel?.State == ChannelState.Joined;

        /// <summary>
        /// Current recommended batch window from server (ms).
        /// </summary>
        public int RecommendedBatchWindow { get; private set; } = 500;

        /// <summary>
        /// Current recommended batch size from server.
        /// </summary>
        public int RecommendedBatchSize { get; private set; } = 5;

        /// <summary>
        /// Event fired when backpressure config is received.
        /// </summary>
        public event Action<BackpressureConfig> OnBackpressureConfig;

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
        /// Sends a single measurement to the server immediately.
        /// </summary>
        /// <param name="attributeId">The attribute identifier (e.g., "x", "y").</param>
        /// <param name="payload">The measurement payload.</param>
        /// <param name="timestamp">Optional timestamp in milliseconds. Uses current time if not specified.</param>
        public void SendMeasurement(string attributeId, object payload, long? timestamp = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SensorStream));

            var measurement = new Measurement(
                attributeId,
                payload,
                timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            _channel.Push("measurement", measurement.ToDictionary());
        }

        /// <summary>
        /// Sends a single measurement to the server immediately (async).
        /// </summary>
        public async Task SendMeasurementAsync(string attributeId, object payload, long? timestamp = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SensorStream));

            var measurement = new Measurement(
                attributeId,
                payload,
                timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            await _channel.PushAsync("measurement", measurement.ToDictionary());
        }

        /// <summary>
        /// Adds a measurement to the batch buffer.
        /// The batch will be sent when it reaches the configured size or when Flush() is called.
        /// </summary>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <param name="payload">The measurement payload.</param>
        /// <param name="timestamp">Optional timestamp in milliseconds.</param>
        public void AddToBatch(string attributeId, object payload, long? timestamp = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SensorStream));

            var measurement = new Measurement(
                attributeId,
                payload,
                timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            lock (_batchLock)
            {
                _batchBuffer.Add(measurement);

                if (_batchBuffer.Count >= _currentBatchSize)
                {
                    FlushInternal();
                }
            }
        }

        /// <summary>
        /// Flushes any pending measurements in the batch buffer.
        /// </summary>
        public void Flush()
        {
            lock (_batchLock)
            {
                FlushInternal();
            }
        }

        /// <summary>
        /// Flushes any pending measurements in the batch buffer (async).
        /// </summary>
        public async Task FlushAsync()
        {
            List<Dictionary<string, object>> batchToSend;

            lock (_batchLock)
            {
                if (_batchBuffer.Count == 0) return;

                batchToSend = new List<Dictionary<string, object>>(_batchBuffer.Count);
                foreach (var m in _batchBuffer)
                {
                    batchToSend.Add(m.ToDictionary());
                }
                _batchBuffer.Clear();
            }

            await _channel.PushAsync("measurements_batch", batchToSend);
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

        /// <summary>
        /// Leaves the sensor channel and releases resources.
        /// </summary>
        public async Task CloseAsync()
        {
            if (_disposed) return;

            // Flush any remaining measurements
            await FlushAsync();

            await _channel.LeaveAsync();
            _disposed = true;
        }

        /// <summary>
        /// Leaves the sensor channel and releases resources (non-async).
        /// </summary>
        public void Close()
        {
            _ = CloseAsync();
        }

        private void FlushInternal()
        {
            if (_batchBuffer.Count == 0) return;

            var batch = new List<Dictionary<string, object>>(_batchBuffer.Count);
            foreach (var m in _batchBuffer)
            {
                batch.Add(m.ToDictionary());
            }
            _batchBuffer.Clear();

            _channel.Push("measurements_batch", batch);
        }

        private void HandleBackpressureConfig(object payload)
        {
            if (payload is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue("recommended_batch_window", out var window))
                {
                    RecommendedBatchWindow = Convert.ToInt32(window);
                }

                if (dict.TryGetValue("recommended_batch_size", out var size))
                {
                    RecommendedBatchSize = Convert.ToInt32(size);
                }

                var config = BackpressureConfig.FromDictionary(dict);
                OnBackpressureConfig?.Invoke(config);

                Debug.Log($"[SensorStream] Backpressure config: window={RecommendedBatchWindow}ms, size={RecommendedBatchSize}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _channel.Off("backpressure_config");

            lock (_batchLock)
            {
                _batchBuffer.Clear();
            }
        }
    }
}
