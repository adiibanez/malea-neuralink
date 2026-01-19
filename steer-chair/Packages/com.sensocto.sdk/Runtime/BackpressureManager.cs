using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sensocto.SDK
{
    /// <summary>
    /// Manages adaptive batching based on backpressure configuration from the server.
    /// Collects measurements and flushes them when batch conditions are met.
    /// </summary>
    public class BackpressureManager
    {
        private BackpressureConfig _config;
        private readonly List<Measurement> _buffer = new List<Measurement>();
        private float _lastFlushTime;
        private readonly object _bufferLock = new object();

        /// <summary>
        /// Current attention level from server.
        /// </summary>
        public AttentionLevel CurrentLevel => _config?.AttentionLevel ?? AttentionLevel.None;

        /// <summary>
        /// Current recommended batch window in milliseconds.
        /// </summary>
        public int BatchWindowMs => _config?.RecommendedBatchWindow ?? 5000;

        /// <summary>
        /// Current recommended batch size.
        /// </summary>
        public int BatchSize => _config?.RecommendedBatchSize ?? 20;

        /// <summary>
        /// Number of measurements currently buffered.
        /// </summary>
        public int BufferedCount
        {
            get
            {
                lock (_bufferLock)
                {
                    return _buffer.Count;
                }
            }
        }

        /// <summary>
        /// Event fired when backpressure config is updated.
        /// </summary>
        public event Action<BackpressureConfig> OnConfigUpdated;

        public BackpressureManager()
        {
            _config = BackpressureConfig.GetDefault(AttentionLevel.None);
            _lastFlushTime = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Update configuration from server backpressure_config message.
        /// </summary>
        public void UpdateConfig(BackpressureConfig config)
        {
            var oldLevel = _config?.AttentionLevel;
            _config = config;

            if (oldLevel != config.AttentionLevel)
            {
                Debug.Log($"[BackpressureManager] Attention level changed: {oldLevel} -> {config.AttentionLevel}, " +
                          $"window: {config.RecommendedBatchWindow}ms, batch: {config.RecommendedBatchSize}");
            }

            OnConfigUpdated?.Invoke(config);
        }

        /// <summary>
        /// Update configuration from raw dictionary payload.
        /// </summary>
        public void UpdateConfig(Dictionary<string, object> payload)
        {
            var config = BackpressureConfig.FromPayload(payload);
            UpdateConfig(config);
        }

        /// <summary>
        /// Add a measurement to the buffer.
        /// </summary>
        public void AddMeasurement(Measurement measurement)
        {
            lock (_bufferLock)
            {
                _buffer.Add(measurement);
            }
        }

        /// <summary>
        /// Add a measurement with individual parameters.
        /// </summary>
        public void AddMeasurement(string attributeId, object payload)
        {
            AddMeasurement(new Measurement(attributeId, payload));
        }

        /// <summary>
        /// Add a measurement with explicit timestamp.
        /// </summary>
        public void AddMeasurement(string attributeId, object payload, long timestamp)
        {
            AddMeasurement(new Measurement(attributeId, payload, timestamp));
        }

        /// <summary>
        /// Check if the batch should be flushed based on size or time.
        /// </summary>
        public bool ShouldFlush()
        {
            lock (_bufferLock)
            {
                if (_buffer.Count == 0)
                    return false;

                // Check batch size
                if (_buffer.Count >= BatchSize)
                    return true;

                // Check time window
                float elapsed = (Time.realtimeSinceStartup - _lastFlushTime) * 1000f;
                if (elapsed >= BatchWindowMs)
                    return true;

                return false;
            }
        }

        /// <summary>
        /// Check if should flush, accounting for high attention mode (immediate send).
        /// </summary>
        public bool ShouldFlushImmediate()
        {
            // In high attention mode, flush immediately
            if (CurrentLevel == AttentionLevel.High)
            {
                lock (_bufferLock)
                {
                    return _buffer.Count > 0;
                }
            }

            return ShouldFlush();
        }

        /// <summary>
        /// Flush the buffer and return all measurements.
        /// Returns null if buffer is empty.
        /// </summary>
        public List<Measurement> Flush()
        {
            lock (_bufferLock)
            {
                if (_buffer.Count == 0)
                    return null;

                var batch = new List<Measurement>(_buffer);
                _buffer.Clear();
                _lastFlushTime = Time.realtimeSinceStartup;
                return batch;
            }
        }

        /// <summary>
        /// Flush only if conditions are met, otherwise return null.
        /// </summary>
        public List<Measurement> FlushIfReady()
        {
            if (ShouldFlushImmediate())
            {
                return Flush();
            }
            return null;
        }

        /// <summary>
        /// Clear the buffer without returning measurements.
        /// </summary>
        public void Clear()
        {
            lock (_bufferLock)
            {
                _buffer.Clear();
            }
        }

        /// <summary>
        /// Get time until next flush opportunity (for scheduling).
        /// </summary>
        public float GetTimeUntilFlush()
        {
            float elapsed = (Time.realtimeSinceStartup - _lastFlushTime) * 1000f;
            float remaining = BatchWindowMs - elapsed;
            return Mathf.Max(0, remaining);
        }

        /// <summary>
        /// Calculate optimal sampling rate based on current backpressure.
        /// Returns samples per second.
        /// </summary>
        public float GetOptimalSamplingRate()
        {
            // Calculate how many samples we can send per second
            // based on batch window and size
            float windowSeconds = BatchWindowMs / 1000f;
            return BatchSize / windowSeconds;
        }
    }
}
