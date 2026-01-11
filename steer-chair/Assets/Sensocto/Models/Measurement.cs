using System;
using System.Collections.Generic;

namespace Sensocto.Models
{
    /// <summary>
    /// Represents a single sensor measurement.
    /// </summary>
    [Serializable]
    public class Measurement
    {
        public object Payload { get; set; }
        public long Timestamp { get; set; }
        public string AttributeId { get; set; }

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
    /// Represents an attribute update action.
    /// </summary>
    [Serializable]
    public class AttributeUpdate
    {
        public string Action { get; set; }
        public string AttributeId { get; set; }
        public Dictionary<string, object> Metadata { get; set; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["action"] = Action,
                ["attribute_id"] = AttributeId,
                ["metadata"] = Metadata
            };
        }
    }
}
