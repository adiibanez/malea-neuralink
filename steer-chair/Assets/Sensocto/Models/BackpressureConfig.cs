using System;
using System.Collections.Generic;

namespace Sensocto.Models
{
    /// <summary>
    /// Attention levels that determine data transmission rates.
    /// </summary>
    public enum AttentionLevel
    {
        High,    // User focused - fast updates (100ms window, batch 1)
        Medium,  // User viewing - normal updates (500ms window, batch 5)
        Low,     // Sensor connected but not viewed (2000ms window, batch 10)
        None     // No active connections (5000ms window, batch 20)
    }

    /// <summary>
    /// Backpressure configuration received from the server.
    /// Tells the client how to adjust data transmission rates.
    /// </summary>
    [Serializable]
    public class BackpressureConfig
    {
        public AttentionLevel AttentionLevel { get; set; } = AttentionLevel.None;
        public int RecommendedBatchWindowMs { get; set; } = 5000;
        public int RecommendedBatchSize { get; set; } = 20;
        public long Timestamp { get; set; }

        public static BackpressureConfig FromDictionary(Dictionary<string, object> dict)
        {
            var config = new BackpressureConfig();

            if (dict.TryGetValue("attention_level", out var level))
            {
                config.AttentionLevel = ParseAttentionLevel(level?.ToString());
            }

            if (dict.TryGetValue("recommended_batch_window", out var window))
            {
                config.RecommendedBatchWindowMs = Convert.ToInt32(window);
            }

            if (dict.TryGetValue("recommended_batch_size", out var size))
            {
                config.RecommendedBatchSize = Convert.ToInt32(size);
            }

            if (dict.TryGetValue("timestamp", out var ts))
            {
                config.Timestamp = Convert.ToInt64(ts);
            }

            return config;
        }

        private static AttentionLevel ParseAttentionLevel(string level)
        {
            return level?.ToLowerInvariant() switch
            {
                "high" => AttentionLevel.High,
                "medium" => AttentionLevel.Medium,
                "low" => AttentionLevel.Low,
                _ => AttentionLevel.None
            };
        }

        /// <summary>
        /// Gets default configuration for a given attention level.
        /// </summary>
        public static BackpressureConfig GetDefault(AttentionLevel level)
        {
            return level switch
            {
                AttentionLevel.High => new BackpressureConfig
                {
                    AttentionLevel = AttentionLevel.High,
                    RecommendedBatchWindowMs = 100,
                    RecommendedBatchSize = 1
                },
                AttentionLevel.Medium => new BackpressureConfig
                {
                    AttentionLevel = AttentionLevel.Medium,
                    RecommendedBatchWindowMs = 500,
                    RecommendedBatchSize = 5
                },
                AttentionLevel.Low => new BackpressureConfig
                {
                    AttentionLevel = AttentionLevel.Low,
                    RecommendedBatchWindowMs = 2000,
                    RecommendedBatchSize = 10
                },
                _ => new BackpressureConfig
                {
                    AttentionLevel = AttentionLevel.None,
                    RecommendedBatchWindowMs = 5000,
                    RecommendedBatchSize = 20
                }
            };
        }
    }
}
