using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sensocto.SDK
{
    /// <summary>
    /// Interface for receiving movement input.
    /// Implement this to receive joystick/direction input from various sources.
    /// </summary>
    public interface IMoveReceiver
    {
        void Move(Vector2 direction);
    }

    /// <summary>
    /// Represents a single sensor measurement.
    /// </summary>
    [Serializable]
    public class Measurement
    {
        /// <summary>
        /// The attribute identifier (e.g., "heart_rate", "temperature", "imu").
        /// </summary>
        public string AttributeId { get; set; }

        /// <summary>
        /// The measurement payload. Can be a number, string, or complex object.
        /// </summary>
        public object Payload { get; set; }

        /// <summary>
        /// Unix timestamp in milliseconds when the measurement was taken.
        /// </summary>
        public long Timestamp { get; set; }

        public Measurement() { }

        public Measurement(string attributeId, object payload)
        {
            AttributeId = attributeId;
            Payload = payload;
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public Measurement(string attributeId, object payload, long timestamp)
        {
            AttributeId = attributeId;
            Payload = payload;
            Timestamp = timestamp;
        }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["payload"] = Payload,
                ["timestamp"] = Timestamp,
                ["attribute_id"] = AttributeId
            };
        }

        public static Measurement FromDictionary(Dictionary<string, object> dict)
        {
            return new Measurement
            {
                Payload = dict.TryGetValue("payload", out var p) ? p : null,
                Timestamp = dict.TryGetValue("timestamp", out var t) ? Convert.ToInt64(t) : 0,
                AttributeId = dict.TryGetValue("attribute_id", out var a) ? a?.ToString() : null
            };
        }
    }

    /// <summary>
    /// Configuration for registering a sensor.
    /// </summary>
    [Serializable]
    public class SensorConfig
    {
        /// <summary>
        /// Unique identifier for the sensor. Auto-generated if not specified.
        /// </summary>
        public string SensorId { get; set; }

        /// <summary>
        /// Human-readable name for the sensor.
        /// </summary>
        public string SensorName { get; set; } = "Unity Sensor";

        /// <summary>
        /// Type of sensor (e.g., "imu", "heartrate", "geolocation").
        /// </summary>
        public string SensorType { get; set; } = "generic";

        /// <summary>
        /// List of attribute IDs this sensor will report.
        /// </summary>
        public List<string> Attributes { get; set; } = new List<string>();

        /// <summary>
        /// Sampling rate in Hz (measurements per second).
        /// </summary>
        public int SamplingRateHz { get; set; } = 10;

        /// <summary>
        /// Number of measurements to batch before sending.
        /// </summary>
        public int BatchSize { get; set; } = 5;
    }

    /// <summary>
    /// Connection state of the Sensocto client.
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Error
    }

    /// <summary>
    /// Backpressure configuration received from the server.
    /// </summary>
    [Serializable]
    public class BackpressureConfig
    {
        /// <summary>
        /// Current attention level for this sensor.
        /// </summary>
        public AttentionLevel AttentionLevel { get; set; }

        /// <summary>
        /// Recommended time window between batch sends (ms).
        /// </summary>
        public int RecommendedBatchWindow { get; set; }

        /// <summary>
        /// Recommended batch size.
        /// </summary>
        public int RecommendedBatchSize { get; set; }

        /// <summary>
        /// Server timestamp when config was generated.
        /// </summary>
        public long Timestamp { get; set; }

        internal static BackpressureConfig FromPayload(Dictionary<string, object> payload)
        {
            var config = new BackpressureConfig();

            if (payload.TryGetValue("attention_level", out var level))
            {
                config.AttentionLevel = ParseAttentionLevel(level?.ToString());
            }

            if (payload.TryGetValue("recommended_batch_window", out var window))
            {
                config.RecommendedBatchWindow = Convert.ToInt32(window);
            }

            if (payload.TryGetValue("recommended_batch_size", out var size))
            {
                config.RecommendedBatchSize = Convert.ToInt32(size);
            }

            if (payload.TryGetValue("timestamp", out var ts))
            {
                config.Timestamp = Convert.ToInt64(ts);
            }

            return config;
        }

        private static AttentionLevel ParseAttentionLevel(string level)
        {
            return level?.ToLower() switch
            {
                "high" => AttentionLevel.High,
                "medium" => AttentionLevel.Medium,
                "low" => AttentionLevel.Low,
                "none" => AttentionLevel.None,
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
                    RecommendedBatchWindow = 100,
                    RecommendedBatchSize = 1
                },
                AttentionLevel.Medium => new BackpressureConfig
                {
                    AttentionLevel = AttentionLevel.Medium,
                    RecommendedBatchWindow = 500,
                    RecommendedBatchSize = 5
                },
                AttentionLevel.Low => new BackpressureConfig
                {
                    AttentionLevel = AttentionLevel.Low,
                    RecommendedBatchWindow = 2000,
                    RecommendedBatchSize = 10
                },
                _ => new BackpressureConfig
                {
                    AttentionLevel = AttentionLevel.None,
                    RecommendedBatchWindow = 5000,
                    RecommendedBatchSize = 20
                }
            };
        }
    }

    /// <summary>
    /// Server attention level for backpressure control.
    /// </summary>
    public enum AttentionLevel
    {
        /// <summary>No active viewers - minimal updates needed.</summary>
        None,
        /// <summary>Low attention - slower updates acceptable.</summary>
        Low,
        /// <summary>Medium attention - normal updates.</summary>
        Medium,
        /// <summary>High attention - fast updates needed.</summary>
        High
    }

    /// <summary>
    /// Room membership role.
    /// </summary>
    public enum RoomRole
    {
        Owner,
        Admin,
        Member
    }

    /// <summary>
    /// Represents a room in Sensocto.
    /// </summary>
    [Serializable]
    public class Room
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string JoinCode { get; set; }
        public bool IsPublic { get; set; }
        public bool CallsEnabled { get; set; }
        public string OwnerId { get; set; }
        public Dictionary<string, object> Configuration { get; set; }
    }

    /// <summary>
    /// Represents a user in Sensocto.
    /// </summary>
    [Serializable]
    public class User
    {
        public string Id { get; set; }
        public string Email { get; set; }
    }

    /// <summary>
    /// Represents a call participant.
    /// </summary>
    [Serializable]
    public class CallParticipant
    {
        public string UserId { get; set; }
        public string EndpointId { get; set; }
        public Dictionary<string, object> UserInfo { get; set; }
        public DateTime JoinedAt { get; set; }
        public bool AudioEnabled { get; set; }
        public bool VideoEnabled { get; set; }
    }

    /// <summary>
    /// ICE server configuration for WebRTC.
    /// </summary>
    [Serializable]
    public class IceServer
    {
        public string[] Urls { get; set; }
        public string Username { get; set; }
        public string Credential { get; set; }
    }
}
